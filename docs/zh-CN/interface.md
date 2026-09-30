# X2 机器人 Agent 网关协议

**中文** | [English](../en/interface.md) | [Français](../fr/interface.md)

协议参考版本：v1.0，与 LinkSoul AgentSDK v1.4.0 对齐。本页定义 Unity 网关的消息格式和行为。真机的协议与技能支持需单独确认。

开始联调前，先按 [EXE 运行说明](simulator.md) 或 [Unity 工程指南](unity.md) 启动机器人，再按 [示例 Agent 指南](../../example/x2_agent/docs/zh-CN/README.md) 连接；两种启动方式使用相同的网关协议。

## 1. 概述

机器人本体（网关侧，Unity / 真机）作为 **WebSocket 服务端**，Agent（大模型应用侧）作为 **客户端** 接入。网关负责：

- 采集麦克风音频，使用语音活动检测（VAD）划分对话轮次并发送给 Agent。
- 接收语音识别（ASR）文本、大语言模型（LLM）增量文本和语音合成（TTS）音频，更新字幕并播放音频。
- 同步机器人连接状态，接收技能指令并报告执行结果。

```
┌──────────────┐  WebSocket (JSON 文本帧)  ┌──────────────┐
│   机器人网关   │ ◄──────────────────────► │  Agent 客户端 │
│ (Unity/真机)  │   16k PCM base64 内嵌      │ (外部服务)   │
└──────────────┘                           └──────────────┘
```

## 2. 连接与鉴权

### 2.1 端点

| 项 | 值 |
|---|---|
| URL | `ws://<robot-host>:9002/api/V1/open-portal/app/wss/agent-sdk` |
| 协议 | WebSocket（RFC 6455），消息为 **JSON 文本帧** |
| 并发会话 | 默认 **8** 路，可在 `CompetitionLauncher.MaxConnections` 调整 |

> 路径参与签名校验，必须与上面完全一致（含大小写）。

本机连接使用 `127.0.0.1`。跨机接入前，需修改 Unity 网关的监听地址并确保端口可达，详见 [Unity 配置说明](unity.md)。自定义客户端应禁用 WebSocket 压缩；Python `websockets` 使用 `compression=None`。

### 2.2 鉴权（HTTP Upgrade 阶段）

鉴权在 HTTP Upgrade 握手阶段完成，连接后不会再发送单独的 `auth` 应答。示例默认凭据为 `demo-app` / `demo-key` / `demo-secret`，分别对应应用 ID、Key 和签名密钥。请求头如下：

| Header | 说明 |
|---|---|
| `X-App-Id` | 应用 ID |
| `X-App-Key` | 应用 Key |
| `X-Timestamp` | 毫秒 Unix 时间戳（±5 分钟内有效） |
| `X-Nonce` | 随机串，每次连接唯一 |
| `X-Signature` | 签名，见下 |
| `X-Callback-Types` | 回调类型 JSON 数组，语音交互使用 `["audio2tts"]` |

**签名算法**（HMAC-SHA256，小写 hex）：

```python
import hashlib, hmac, time, uuid

payload = "GET\n" + path + "\n" + ts + "\n" + nonce
signature = hmac.new(app_secret.encode(), payload.encode(),
                     hashlib.sha256).hexdigest()
```

### 2.3 握手响应

| HTTP 状态 | 含义 |
|---|---|
| `101` | 成功，升级为 WebSocket |
| `400` | 非 WebSocket 升级请求 |
| `401` | 签名/凭证/时间戳校验失败 |
| `404` | 路径不匹配 |
| `503` | 已达到并发连接上限 |

> 示例客户端始终发送签名。是否强制校验由网关构建配置决定；不要依赖宽松模式。
> 修改 `StrictAuth` 后需从 [Unity 工程](../../unity-agent-playground/) 重新构建网关。

### 2.4 会话生命周期

- 连接成功后等待 `robot_state.sync`（state=online，见 §4.1）；音频事件可能先到达，不要假定同步是第一帧
- 断线后 Agent 应自动重连（建议间隔 3s），机器人重复上线以最新 sync 为准
- 机器人侧状态周期性推送见 §4.5

### 2.5 多连接、控制权和语音接收权

握手可附加以下字段；旧 Agent 不传时仍能连接：

| Header | 默认值 | 说明 |
|---|---|---|
| `X-Client-Role` | `agent` | `controller` / `agent` / `observer` |
| `X-Client-Name` | 应用 ID | 用于控制台区分连接，最多 64 字符，HTTP 头使用 ASCII |
| `X-Audio-Enabled` | agent 为 `true`，其它角色为 `false` | 是否参与麦克风接收端选举；observer 始终不接收音频 |
| `X-Control-Enabled` | 未声明时为 `true`（observer 除外） | 握手时声明动作控制资格；管理控制台固定发送 `false`，避免连接时短暂抢占 Agent |

控制台声明 `controller`，优先级固定 **1000**；普通 Agent 初始为 **50**，observer 为 **0**。
当前管理控制台（最先连接的在线 controller）可以将其它非控制台连接调为 **0–999**；
不能修改任何控制台的固定优先级。
优先级属于本次连接，断线重连后恢复默认值。同级时先连接者优先。

动作和打断指令仅由参与接管的最高优先级在线非 observer 连接执行；低优先级请求以 `4091` 拒绝，
不会排队后重放。日志、技能状态和周期状态广播给所有连接。
麦克风只发给启用语音的最高优先级连接；管理控制台固定发送 `X-Audio-Enabled: false`，
不接收麦克风、不执行语音服务。语音对话由独立 Agent 处理。
控制台断开或修改其它优先级时自动重新选举，切换语音接收端会清空旧的语音状态。
控制台打断后旧 Agent 的迟到语音回复会被拒绝，直到下一轮录音开始。
角色沿用现有握手鉴权，由客户端声明，面向受信任的本机客户端。

每个连接会收到自己的 `agentsdk.session.state`：

```json
{
  "type": "agentsdk.session.state",
  "robotCid": "cid-controller",
  "revision": 4,
  "controlOwnerCid": "cid-agent",
  "managementOwnerCid": "cid-controller",
  "audioOwnerCid": "cid-agent",
  "sessions": [
    {"robotCid": "cid-controller", "name": "X2 Console", "role": "controller",
     "priority": 1000, "controlEnabled": false, "audioEnabled": false, "controlActive": false, "audioActive": false},
    {"robotCid": "cid-agent", "name": "Agent", "role": "agent",
     "priority": 800, "controlEnabled": true, "audioEnabled": true, "controlActive": true, "audioActive": true}
  ]
}
```

上线 `robot_state.sync` 也附带 `robotCid`、`clientRole`、`priority`、
`controlActive`、`audioActive`、`controlEnabled`、`canManage`。备用 Agent 应跳过开场白和云端预连接。
客户端以最新 `revision` 为准；旧客户端可以忽略此扩展。

管理控制台修改优先级：

```json
{"type": "agentsdk.session.priority.set", "eventId": "evt-priority",
 "targetRobotCid": "cid-agent", "priority": 800}
```

成功后广播新的 `session.state`。没有管理权限、目标已断开、试图修改控制台，
或数值不是 0–999 的整数时返回 `agentsdk.error`，`errorCode=4093`。

控制台可主动交还或收回自己的动作控制资格；本管理控制台连接时默认关闭，手动操作时开启：

```json
{"type": "agentsdk.session.control.set", "eventId": "evt-release", "enabled": false}
```

仅 controller 能修改自己的 `controlEnabled`；`enabled` 必须为 JSON 布尔值。
关闭后，该连接不参与动作控制选举，但保留固定优先级、日志连接以及原有管理资格，
也不改变语音接收设置。管理控制台由 `managementOwnerCid` 指定，与动作归属分开。
重新设为 `true` 后按原有优先级选举；成功广播新的 `session.state`，非法请求返回 `4093`。
Agent 应在接收线程及时更新权限状态，不要等当前语音轮次结束；发送技能前再次检查权限。

## 3. 消息信封

所有消息为 JSON 对象，公共字段：

```jsonc
{
  "type": "agentsdk.xxx.yyy",   // 消息类型，见下表
  "agentId": "demo-app",        // Agent 应用 ID（原样回传）
  "agentMode": "passive",       // Agent 工作模式（Agent→网关方向携带）
  "robotCid": "cid-xxxx",       // 机器人会话 ID（原样回传）
  "cid": "cid-xxxx",            // 同 robotCid
  "eventId": "evt-xxxx",        // 轮次 ID：commit 的轮次内所有回包共用
  "itemId": "item-xxxx"         // 条目 ID：本轮 ASR/LLM/TTS 条目共用（可选）
}
```

**回复录音事件时，保留收到的 `agentId`、`robotCid` / `cid`、`eventId`，以及存在时的 `itemId`。** 网关使用这些字段关联机器人、对话轮次和条目。开场白是连接同步后由 Agent 主动发起的独立轮次。

## 4. 网关 → Agent（机器人下发）

### 4.1 机器人上下线 `agentsdk.robot_state.sync`

```json
{
  "type": "agentsdk.robot_state.sync",
  "agentId": "demo-app",
  "state": "online",
  "callbackType": "audio2tts",
  "agentMeta": {}
}
```
连接建立后发送的状态同步帧，不保证是第一帧。`agentMeta` 为机器人能力元信息。

### 4.2 音频流开始 `agentsdk.audio_request.start`

VAD 检测到用户开始说话时发送此消息。`audio2tts` 模式携带 `itemId`。

```json
{ "type": "agentsdk.audio_request.start", "agentId": "...",
  "robotCid": "cid-...", "cid": "cid-...", "eventId": "evt-...", "itemId": "item-..." }
```

### 4.3 音频流中间帧 `agentsdk.audio_request.append`

```json
{ "type": "agentsdk.audio_request.append", "...": "...",
  "itemId": "item-...",
  "audio": "<base64 PCM>",
  "audioLen": 3200 }
```

`append` 中的 `audioLen` 是 base64 解码后的 PCM 字节数，不是 base64 字符串长度。

### 4.4 音频流结束 `agentsdk.audio_request.commit`

用户停止说话（静音超时）或达到最长说话时长。

```json
{ "type": "agentsdk.audio_request.commit", "...": "...", "itemId": "item-..." }
```

收到此帧即表示一轮录音已结束。默认客户端已在 start/append 阶段上传 ASR 音频，commit 后等待最终识别再回包（见 §6）。

### 4.5 状态推送 `agentsdk.state_request.meta`

```json
{ "type": "agentsdk.state_request.meta", "...": "...",
  "stateName": "power", "stateValue": "ok" }
```

### 4.6 Unity 运行时日志 `agentsdk.runtime.log`（仿真扩展）

通过同一个 WebSocket 连接发送 Unity 的 Info、Warning、Error、Assert 和 Exception，
包含引擎与脚本通过 Unity 日志回调产生的消息。无需额外订阅；
`agent.py` 和 `demo.py` 会在终端显示，即使开场白或对话轮次尚未结束也会继续接收。

```json
{
  "type": "agentsdk.runtime.log",
  "agentId": "agent-001",
  "robotCid": "cid-...",
  "cid": "cid-...",
  "eventId": "evt-...",
  "source": "unity",
  "level": "error",
  "logType": "Exception",
  "message": "InvalidOperationException: example",
  "stackTrace": "Example.Update () (at Assets/Example.cs:42)",
  "timestampMs": 1790388000000,
  "sequence": 17,
  "threadId": 1,
  "droppedCount": 0,
  "truncated": false
}
```

- `level`：`info` / `warning` / `error`；Assert 和 Exception 归入 `error`，
  `logType` 保留 Unity 原始类型 `Log` / `Warning` / `Error` / `Assert` / `Exception`。
- `timestampMs`：捕获时的 UTC Unix 毫秒时间；`sequence`：本次运行内递增序号。
  `stackTrace` 取自 Unity 回调，是否存在及详细程度由 Unity 的堆栈配置决定。
- 未连接或发送积压时保留最近 256 条，满时丢弃最旧条目；
  `droppedCount` 为本次运行内捕获缓冲区累计丢弃数。
  每帧最多转交 32 条，每个会话最多排队 128 条日志，普通语音消息优先发送。
  慢客户端单独丢弃积压日志，以 `sessionDroppedCount` 报告；不阻塞其它客户端。
- 正文最多 8192 个 UTF-16 代码单元，堆栈最多 16384；超长时 `truncated=true`。
  转发自身不产生日志，避免循环。
- 日志为尽力交付，无 ACK；断线时已交给旧连接的日志不重发。
  编译失败、进入运行时前的编辑器消息及进程崩溃后未发送的日志不在此通道保证范围内。
  自定义客户端不支持该扩展时可忽略此类型。

网关对单条消息（含所有分片）限制为 16 MiB，超限关闭码为 `1009`。

## 5. Agent → 网关（Agent 回传）

以下消息均需携带 §3 信封字段。

### 5.1 ASR 识别结果

| type | 字段 | 说明 |
|---|---|---|
| `agentsdk.asr_response.middle` | `text` | 中间识别结果（可多次） |
| `agentsdk.asr_response.final` | `text` | 最终识别结果（每轮一次） |

### 5.2 LLM 回复（流式增量）

| type | 字段 | 说明 |
|---|---|---|
| `agentsdk.llm_response.item.delta` | `itemId`, `text` | **文本增量**，网关侧逐段累积 |
| `agentsdk.llm_response.item.done` | `itemId` | 本条 LLM 内容结束 |
| `agentsdk.llm_response.done` | — | 本轮 LLM 处理结束 |

> `text` 为增量 delta 而非全量。发送多段 delta 后跟一个 item.done。

### 5.3 TTS 音频（流式）

| type | 字段 | 说明 |
|---|---|---|
| `agentsdk.tts_response.item.delta` | `itemId`, `audio`, `audioLen` | PCM 分段，边收边播 |
| `agentsdk.tts_response.item.done` | `itemId` | 本条音频结束 |
| `agentsdk.tts_response.done` | — | 本轮 TTS 结束，网关复位状态 |

- `audio`：base64(16kHz / 16bit / mono PCM)
- 网关收到第一段即开始播放，后续分段自动排队，无需等待合成完成

### 5.4 技能指令 `agentsdk.xlm_response.skill`

```json
{ "type": "agentsdk.xlm_response.skill", "...": "...",
  "itemId": "item-...",
  "skillType": "gesture",
  "skillName": "wave_hands",
  "skillParam": {} }
```

下表对应 Unity 源码的默认技能表。修改技能后，应重新构建并验证目标程序；具体配置位置见 [Unity 工程指南](unity.md)。

| skillType | skillName | skillParam | 说明 |
|---|---|---|---|
| gesture | `wave_hands` | — | 挥手，名义时长约 6 秒 |
| gesture | `open_arms` | — | 张开双臂，名义时长约 6.5 秒 |
| movement | `walk` | `{"distanceM": 1.0}` | 前进指定米数（0.2~5） |
| movement | `turn` | `{"angleDeg": 90}` | 原地转角（右转为正） |
| movement | `stop` | — | 立即停止 |
| emotion | `happy` / `sad` / `surprised` / `angry` / `love` / `neutral` | `{"durationMs": 3000}` | 头部表情屏，`neutral` 用于复位 |

- 手势协调肩、肘、腕与头部 yaw，准备、表达和收回分阶段执行；双臂展开带轻微左右错峰。
- 目标角度受关节限位与速度限制，切换手势从当前目标衔接；打断立即回报 `failed` 并平滑归位。
- 6 秒 / 6.5 秒为名义时长，平滑收尾可能略有延长；衔接后续动作请等待 `done`。
- 手势不写腿部、腰部或根位姿；episode reset 会清掉旧手势，避免复位后恢复旧动作。
- 运动指令在步态支撑相位切换、结束在支撑相位归零，动作完成即回报
- 表情屏同时联动 TTS 播放能量做口型张合
- 未知 `skillName` 回报 `failed`，不影响语音链路
- 示例 `agent.py` 将上表注册为 LLM 的 `robot_skill` 工具，模型根据“挥挥手”“做个开心的表情”“往前走一米”等请求选择技能。离线 demo 不进行语音意图识别。

### 5.5 技能状态回报 `agentsdk.skill_response.state`（网关 → Agent，v1 仿真扩展）

```json
{ "type": "agentsdk.skill_response.state", "...": "...",
  "skillName": "walk",
  "state": "done",
  "detail": "" }
```

| state | 含义 |
|---|---|
| `running` | 已接受并开始执行 |
| `done` | 执行完成（手势播完 / 运动站稳 / 表情已设置） |
| `failed` | 未知技能名 / 无对应执行器 / 执行中的运动或手势被替换、停止或打断 |

技能异步执行，发送完成不代表动作已经完成。需要衔接后续动作时，Agent 应等待对应的 `done` 或 `failed` 状态。状态回报属于仿真扩展，真机接入时需先确认设备是否支持。

### 5.6 打断指令 `agentsdk.xlm_response.interrupt`

```json
{ "type": "agentsdk.xlm_response.interrupt", "...": "...",
  "interruptType": "chat",
  "interruptTips": "好的，请说" }
```

网关收到显式打断指令后，会停止 TTS 播放及当前运动、手势。半双工模式下，播报期间说话不会自动触发此指令。

### 5.7 错误上报 `agentsdk.error`

```json
{ "type": "agentsdk.error", "...": "...",
  "errorCode": 3201, "errorMsg": "llm failed: ..." }
```

错误码约定（示例项目使用）：

| code | 阶段 |
|---|---|
| 3101 / 3102 | ASR 失败 / 空结果 |
| 3201 / 3202 | LLM 失败 / 空回复 |
| 3301 | TTS 失败 |

## 6. 典型时序（一轮语音对话）

```
机器人(网关)                         Agent
    │  robot_state.sync(online)  ──► │  连接后立即
    │                                │
    │  audio_request.start      ──►  │  VAD 开始
    │  audio_request.append ×N  ──►  │  语音流（100ms/帧）
    │  audio_request.commit     ──►  │  一轮语音就绪
    │                                │  ┌─ ASR 最终结果（此前边录边传）
    │  ◄── asr_response.final        │  ├─ LLM 流式
    │  ◄── llm_response.item.delta×N │  │   （每段立即下发）
    │  ◄── llm_response.item.done    │  ├─ LLM 增量直接送双向 TTS
    │  ◄── llm_response.done         │  │
    │  ◄── tts_response.item.delta×N │  ├─ TTS PCM 分段下发
    │      （收到即播放）             │  │
    │  ◄── tts_response.item.done    │  └─ 剩余文本收尾
    │  ◄── tts_response.done         │
    │  ◄── xlm_response.skill       │  （LLM function calling 触发动作时）
    │  skill_response.state ──►      │  running → done/failed（仿真扩展）
    │  （本轮复位，等待下一轮）        │
```

LLM delta 与 TTS 音频可交错到达，无需等待 LLM 完成后再开始 TTS。默认使用双向 TTS，逐句合成为兼容模式。延迟测量分别记录录音与静音检测、commit 后 ASR 等待、LLM 首 token 和 TTS 首包。

## 7. 音频与 VAD 约定

下表为 `Assets/X02Competition/Robot/Audio/VadGate.cs` 等音频组件的源码默认值。便携程序未提供所有参数的界面配置，实际录音时序以运行日志为准。

| 项 | 值 |
|---|---|
| 采样率 / 位深 / 声道 | 16000 Hz / 16 bit / 单声道 |
| 上行分帧 | 约 100 ms/帧（1600 samples = 3200 bytes） |
| 传输编码 | base64 内嵌 JSON `audio` 字段 |
| VAD 起说话阈值（RMS） | 0.02 |
| VAD 停止阈值（RMS） | 0.008 |
| 静音判定 | 600 ms |
| 最长单轮语音 | 15000 ms（强制 commit） |
| 半双工 | 录音 commit 后等待 ASR/LLM/TTS，直至回复及播放结束才恢复 VAD，避免后续录音覆盖正在处理的 eventId；空识别、失败或连续 90 秒无进展会释放等待 |

## 8. 兼容性说明

- 网关对 `llm_response.item.done`、`vlm_*`、`greet_*`、`xlm_response.control` 等类型**接收但忽略**（v1 范围外，日志可见 `gw ignore`），不影响链路
- 未知 `type` 同样忽略，向前兼容
- Agent 侧心跳无强制要求，依赖 TCP 层保活

## 9. 调试（Unity EXE / Play Mode）

- **调试面板**：**F1** 显示或隐藏，默认隐藏。面板显示连接状态、ASR/LLM 字幕和技能记录；按钮直接调用 Unity 执行器，无需 Agent 或云端密钥。
- **技能按钮**：2 个基本动作（挥手/张臂）/ 5 种表情（开心/难过/惊讶/生气/爱心）/ 前进 1m / 右转 90° / 停止，与协议技能表一一对应
- **开场播报**：Agent 收到 `robot_state.sync` 后主动发送一轮 LLM 文本和 TTS 音频，网关直接播放。`agent.py --greeting` 同时修改文本与合成语音；demo 的同名参数只修改字幕，保留内置录音。传入空值可禁用开场白。
- **打断验证**：机器人播报中开口说话 → VAD 冻结到播完（半双工），Agent 下发 `interrupt` 则立即停播停动作

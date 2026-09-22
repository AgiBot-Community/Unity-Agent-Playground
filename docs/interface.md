# X02 机器人 Agent 网关接口文档

**中文** | [English](interface.en.md) | [Français](interface.fr.md)

版本：v1.0（对应 LinkSoul AgentSDK v1.4.0 线上协议）

开始联调前，先按 [EXE 运行说明](simulator.md) 或 [Unity 工程指南](unity.md) 启动机器人，再按 [示例 Agent 指南](../example/README.md) 连接；两种启动方式使用相同的网关协议。

## 1. 概述

机器人本体（网关侧，Unity / 真机）作为 **WebSocket 服务端**，Agent（大模型应用侧）作为 **客户端** 接入。网关负责：

- 采集机器人麦克风音频，经 VAD 切分后推送给你（Agent）
- 接收你回传的 ASR 文本 / LLM 增量文本 / TTS 音频，并驱动机器人扬声器播放
- 维护机器人上下线状态与技能指令通道

```
┌──────────────┐  WebSocket (JSON 文本帧)  ┌──────────────┐
│   机器人网关   │ ◄──────────────────────► │  Agent 客户端 │
│ (Unity/真机)  │   16k PCM base64 内嵌      │ (你的服务)   │
└──────────────┘                           └──────────────┘
```

## 2. 连接与鉴权

### 2.1 端点

| 项 | 值 |
|---|---|
| URL | `ws://<robot-host>:9002/api/V1/open-portal/app/wss/agent-sdk` |
| 协议 | WebSocket（RFC 6455），消息为 **JSON 文本帧** |
| 并发会话 | **1**（单客户端；已有连接未释放时新连接被拒） |

> 路径参与签名校验，必须与上面完全一致（含大小写）。

### 2.2 鉴权（HTTP Upgrade 阶段）

握手即认证，无连接后 auth 应答消息。请求头：

| Header | 说明 |
|---|---|
| `X-App-Id` | 应用 ID |
| `X-App-Key` | 应用 Key |
| `X-Timestamp` | 毫秒 Unix 时间戳（±5 分钟内有效） |
| `X-Nonce` | 随机串，每次连接唯一 |
| `X-Signature` | 签名，见下 |
| `X-Callback-Types` | 回调类型 JSON 数组，主路径填 `["audio2tts"]` |

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
| `503` | 已有会话占用（单会话限制） |

> 示例客户端始终发送签名。是否强制校验由网关构建配置决定；不要依赖宽松模式。
> 修改 `StrictAuth` 后需从 [Unity 工程](../unity-agent-playground/) 重新构建网关。

### 2.4 会话生命周期

- 连接成功后等待 `robot_state.sync`（state=online，见 §4.1）；音频事件可能先到达，不要假定同步是第一帧
- 断线后 Agent 应自动重连（建议间隔 3s），机器人重复上线以最新 sync 为准
- 机器人侧状态周期性推送见 §4.5

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

**核心约定：回传时 `agentId` / `robotCid` / `eventId` / `itemId` 必须原样使用网关下发值**，网关按这些字段路由与关联。

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

VAD 检测到用户开始说话（ Rising energy）。`audio2tts` 模式携带 `itemId`。

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

仿真技能表（`skillType` / `skillName` / `skillParam`）：

下表对应当前 Unity 源码默认技能表；便携 EXE 已于 2026-09-23 根据当前工程重新构建。

| skillType | skillName | skillParam | 说明 |
|---|---|---|---|
| gesture | `wave_hands` | — | 挥手（手臂举起摆动） |
| gesture | `open_arms` | — | 张开双臂（欢迎姿势） |
| movement | `walk` | `{"distanceM": 1.0}` | 前进指定米数（0.2~5） |
| movement | `turn` | `{"angleDeg": 90}` | 原地转角（右转为正） |
| movement | `stop` | — | 立即停止 |
| emotion | `happy` / `sad` / `surprised` / `angry` / `love` / `neutral` | `{"durationMs": 3000}` | 头部表情屏（5 种经典表情 + 复位） |

- 手势由程序化关节轨迹驱动（肩/肘/腕/腰/头），步态机器人可边走边做上半身手势
- 运动指令在步态支撑相位切换、结束在支撑相位归零，动作完成即回报
- 表情屏同时联动 TTS 播放能量做口型张合
- 未知 `skillName` 回报 `failed`，不影响语音链路
- LLM function calling 绑定：示例项目把上表注册为 `robot_skill` 工具（见 example），模型对"挥挥手/做个开心的表情/往前走一米"类意图自动调用

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
| `failed` | 未知技能名 / 无对应执行器（如无步态机器人时下 `walk`） |

技能下发是 fire-and-forget；状态回报为仿真扩展帧，Agent 可据此感知动作完成时机（如"走到跟前再说话"场景）。真机对接时可忽略。

### 5.6 打断指令 `agentsdk.xlm_response.interrupt`

```json
{ "type": "agentsdk.xlm_response.interrupt", "...": "...",
  "interruptType": "chat",
  "interruptTips": "好的，请说" }
```

打断后网关会停播 TTS 并中止当前运动/手势。

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

**流式要点**：LLM delta 与 TTS 音频可交错到达，以上图示并非要求先完成 LLM 再开始 TTS。默认使用双向 TTS，逐句合成为兼容模式。延迟应区分录音与静音检测、commit 后 ASR 等待、LLM 首 token 和 TTS 首包；不保证固定的端到端耗时。

## 7. 音频与 VAD 约定

下表为原构建的参考参数；当前交付二进制没有暴露全部可配置项，实际录音时序以运行日志为准。

| 项 | 值 |
|---|---|
| 采样率 / 位深 / 声道 | 16000 Hz / 16 bit / 单声道 |
| 上行分帧 | 约 100 ms/帧（1600 samples = 3200 bytes） |
| 传输编码 | base64 内嵌 JSON `audio` 字段 |
| VAD 起说话阈值（RMS） | 0.02 |
| VAD 停止阈值（RMS） | 0.008 |
| 静音判定 | 600 ms |
| 最长单轮语音 | 15000 ms（强制 commit） |
| 半双工 | TTS 播放期间 VAD 冻结，避免自采集 |

## 8. 兼容性说明

- 网关对 `llm_response.item.done`、`vlm_*`、`greet_*`、`xlm_response.control` 等类型**接收但忽略**（v1 范围外，日志可见 `gw ignore`），不影响链路
- 未知 `type` 同样忽略，向前兼容
- Agent 侧心跳无强制要求，依赖 TCP 层保活

## 9. 仿真演示说明（Unity exe / Play Mode）

- **调试面板**：默认隐藏，**F1** 呼出/隐藏（录屏时画面干净，收起态左上角保留状态与视角切换入口）。面板含连接状态、ASR/LLM 字幕、技能触发记录；按钮可手动触发技能（不经过 Agent，直接路由到 Unity 执行器），方便在无云端 Key 时验证动作
- **技能按钮**：2 个基本动作（挥手/张臂）/ 5 种表情（开心/难过/惊讶/生气/爱心）/ 前进 1m / 右转 90° / 停止，与协议技能表一一对应
- **开场播报**：Agent 收到 `robot_state.sync` 后主动下发一轮 `llm_response` + `tts_response`（"你好，我是灵犀，有什么可以帮您？"，示例项目 `--greeting` 可改），网关直接播放，不依赖对话轮次
- **打断验证**：机器人播报中开口说话 → VAD 冻结到播完（半双工），Agent 下发 `interrupt` 则立即停播停动作

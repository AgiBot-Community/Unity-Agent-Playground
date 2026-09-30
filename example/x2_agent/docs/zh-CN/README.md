# 示例 Agent：语音对话与机器人技能

**中文** | [English](../en/README.md) | [Français](../fr/README.md)

Python 客户端通过 WebSocket 连接 Unity 机器人网关。`agent.py` 使用豆包语音服务和火山方舟完成对话与技能调用，`demo.py` 用固定文字和录音验证连接与音频播放。

按测试目标选择客户端：

| 命令 | 用途 |
|---|---|
| `python agent.py` | **语音对话与技能调用**。需要云服务密钥；录音期间上传 ASR，接收 LLM 流式回复并同步进行 TTS 合成 |
| `python demo.py` | **离线连接测试**。收到语音后返回固定文字并播放内置录音；录音不可用时使用正弦提示音 |

下文命令均在仓库的 `example/x2_agent/` 目录运行，使用 Windows PowerShell。Linux/macOS 可按本机环境将 `python` 改为 `python3`；仓库提供的便携模拟器仅适用于 Windows。

## 工程结构

`example/x2_agent/` 是独立 Agent 项目，可整体复制运行，配置读取该项目根目录的 `.env`。下表路径均相对于该项目根目录。
图形控制台是另一个独立项目，见[控制台指南](../../../../docs/zh-CN/console.md)。

| 路径 | 用途 |
|---|---|
| `agent.py` / `demo.py` | 语音 Agent 与离线 demo 的脚本入口 |
| `requirements.txt` | 第三方依赖列表 |
| `x2_agent/agent.py` | 对话调度、历史管理、技能调用与豆包 CLI |
| `x2_agent/asr.py` | 录音期间上传、识别结果接收与取消清理 |
| `x2_agent/llm.py` | LLM 流式请求与连接复用 |
| `x2_agent/tts.py` | 双向流式 TTS 合成 |
| `x2_agent/sentence_tts.py` | 逐句 TTS 兼容模式 |
| `x2_agent/speech_protocol.py` | 豆包 V3 二进制帧编解码 |
| `x2_agent/gateway.py` | Unity 网关事件、握手鉴权与消息封装 |
| `x2_agent/demo.py` | 无云服务的网关联调客户端 |
| `x2_agent/config.py` / `audio.py` | 配置加载、PCM 文件输出及日志截断 |
| `tests/` | ASR 上传、流式语音管线的离线回归测试 |
| `.env.example` | 配置模板；复制为 `.env` 后填写密钥 |

配置项见 [`.env.example`](../../.env.example)。密钥保存在本地 `.env`，不提交到 Git。

## 安装依赖

- Python 3.10+
- 火山引擎账号（仅 doubao 客户端需要），开通：
  - 语音技术（ASR + TTS）→ `DOUBAO_SPEECH_API_KEY`
  - 火山方舟（LLM）→ `ARK_API_KEY`

在 `example/x2_agent/` 目录打开 PowerShell：

```powershell
python -m pip install -r requirements.txt
```

使用同一个 Python 环境安装依赖和运行脚本，避免出现已安装依赖但启动时找不到模块的问题。

## 连接机器人

先选择一种方式启动机器人侧：

- **便携模拟器**：下载并运行 GitHub Releases 中的 Windows EXE，详见[模拟器指南](../../../../docs/zh-CN/simulator.md)。
- **从 Unity 工程开始**：用 Unity **2022.3.62f3c1** 打开 `unity-agent-playground/`，打开 `Assets/X02Competition/Scenes/scene.unity` 并点击 Play，详见 [Unity 工程指南](../../../../docs/zh-CN/unity.md)。

两种方式使用同一个本机网关端口 `9002`，只启动其中一种。然后在 `example/x2_agent/` 目录启动一个 Agent：

```powershell
# 离线连接与音频测试
python demo.py
```

看到 `state=online` 后，等待开场白结束，再说一句话检查固定回复和音频播放。完成后按 **Ctrl+C** 退出 demo，再配置真实对话：

```powershell
# 已有 .env 时保留原配置
if (-not (Test-Path .env)) { Copy-Item .env.example .env }
# 编辑 .env，填写 DOUBAO_SPEECH_API_KEY 和 ARK_API_KEY
python agent.py
```

再次看到 `agent 会话就绪 state=online` 表示豆包客户端已连接。等待开场白结束后开始对话；可同时连接控制台或其它 Agent，麦克风只发送给选中的语音接收端。

直接运行 `agent.py` 或 `demo.py`。`x2_agent/` 存放内部实现，保留在入口脚本旁即可；只需安装 `requirements.txt` 中的第三方依赖。

机器人不在本机时可指定 `--host 192.168.x.x --port 9002`，前提是网关确实对外监听、端口可达；仅修改客户端参数不会改变 Unity 的监听范围。

## 调用技能

使用 `agent.py` 连接后，机器人先播放默认开场白“你好，我是灵犀，有什么可以帮您？”。等待播报结束，再尝试下表中的语句；具体技能由模型根据请求选择。`demo.py` 不识别这些语句，测试技能时请使用 F1 按钮或 `--skill` 参数。

| 语音请求示例 | 对应行为 |
|---|---|
| "挥挥手" / "张开双臂" | 挥手 / 张开双臂（2 个基本动作） |
| "你开心吗" / "给我比个爱心" / "我有点难过" / "你生气啦" | 切换头部表情：开心、难过、惊讶、生气、爱心；`neutral` 用于复位 |
| "往前走一米" / "向左转" | 步态前进 / 原地转向 |
| "停" | 识别请求并下发 `stop` 技能后停止；播报期间请使用面板停止按钮或显式打断指令 |

回答时嘴巴随语音张合。技能名称和参数见[网关协议的技能表](../../../../docs/zh-CN/interface.md)。

当前语音流程是半双工，录音提交后，识别、生成回复及播报期间 VAD 暂停；等待回答结束后再说话。空识别、失败或连续 90 秒无回复进展会恢复聆听。默认音色和提示词面向中文，文档翻译不改变语音服务或界面语言。

没有 Key 也能验证动作：按 **F1** 呼出调试面板（默认隐藏），上面的按钮可以直接触发挥手、表情、行走。

demo 的 `--reply` 和 `--greeting` 修改字幕，不会重新合成内置录音。录音位于 `x2_agent/greeting.wav` 和 `x2_agent/tts.wav`，按 200 ms 分帧发送。

## 常用参数

```powershell
# 自定义机器人人设
python agent.py --system-prompt "你是导览机器人小X"

# 保存一轮音频（排查"听不清/说不清"用）
python agent.py --save-audio reply.wav --save-input input.wav

# ASR 默认收到录音 start 就建连，录音期间上传，减少说完后的等待。
# 需要对照排查时，切换为收到 commit 后才开始上传：
python agent.py --asr-after-commit

# 指定模型，也可通过 .env 中的 DOUBAO_LLM_MODEL 设置
python agent.py --llm-model doubao-seed-2-1-turbo-260628

# 当前语音资源不支持双向合成时，使用逐句合成兼容模式
python agent.py --tts-mode sentence

# 开场白自定义 / 禁用
python agent.py --greeting "大家好，我是导览机器人"
python agent.py --greeting=

# demo 客户端：自定义回环台词 / 主动下发技能 / 测打断
python demo.py --reply "你好，我是灵犀"
python demo.py --skill gesture/wave_hands
python demo.py --interrupt chat
```

## .env 变量

配置在启动时加载，导入模块不会自动读取密钥。优先级为：命令行参数 > 已有环境变量 > `.env` > 内置默认值。
脚本默认读取 `example/x2_agent/` 项目根目录的 `.env`。
从仓库根目录可直接执行 `python example/x2_agent/agent.py`，默认仍读取 `example/x2_agent/.env`。也可通过 `--env-file` 参数指定自己的配置文件路径；指定的文件不存在会报错。

| 变量 | 必填 | 说明 |
|---|---|---|
| `DOUBAO_SPEECH_API_KEY` | 是 | 语音控制台 API Key（ASR + TTS 共用） |
| `ARK_API_KEY` | 是 | 方舟 API Key（LLM） |
| `DOUBAO_LLM_MODEL` | 否 | 默认 `doubao-seed-2-0-mini-260428` |
| `DOUBAO_TTS_SPEAKER` | 否 | 默认 `zh_female_wanqudashu_moon_bigtts`（1.0 音色，勿混用 2.0 音色） |
| `DOUBAO_ASR_RESOURCE_ID` | 否 | 默认 `volc.bigasr.sauc.duration` |

服务端点可用 `ARK_API_URL`、`ASR_WS_URL`、`TTS_WS_URL`、`TTS_SENTENCE_WS_URL`
配置，也可分别使用 `--ark-api-url`、`--asr-ws-url`、`--tts-ws-url`、
`--tts-sentence-ws-url` 覆盖。单向和双向 TTS 使用独立端点。
Agent 默认使用项目 `skills.yaml` 定义工具、参数和即时口播，`--skills-file` 可替换它。
`--log-level`、`--log-file` 控制调度日志，`--metrics-file` 在正常退出或 Ctrl+C 时导出
计数与最近 1000 个耗时样本的统计。指标不包含真人录音时长；下发技能计数不代表执行成功。
完整示例见[配置参考](../../../../docs/zh-CN/environment-config.md)。

## 常见问题

| 现象 | 排查方法 |
|---|---|
| 连不上（Connection refused） | 确认 EXE 已运行或 Editor 正在 Play，再核对地址与端口。跨机连接还需修改网关监听地址并确保端口可达，单独添加 `--host` 不够 |
| 握手 401 | 签名错（严格模式下）；核对 appSecret 与签名串格式 |
| 握手 503 | 已达到网关连接数上限（默认 8 路）；关闭不用的连接或调整 Unity 的 MaxConnections |
| 语音说要挥手但没动作 / `4091` | 若手动控制台同时在线，在“会话 / 优先级”中取消“控制台接管动作”。控制台优先级仍为 1000，但会将动作资格交还 Agent；查看技能的 `running/done` 状态确认执行 |
| 没有识别结果 | 音频太短/太轻；确认麦克风没被系统静音 |
| 说完后等待很久 | 对比 `[录音 start→commit]`、`[ASR …ms（commit 后）]`、`[LLM 首 token]`；ASR 建连应与录音重叠。start→commit 包含说话时长及 Unity 的静音检测等待，不等于 ASR 耗时 |
| LLM 404 | 模型 ID 不完整；须用带日期后缀的完整 ID（如 `doubao-seed-2-0-mini-260428`）或接入点 `ep-xxx` |
| TTS 403 | 音色与资源不匹配；`seed-tts-1.0` 只能用 1.0 音色 |
| 收到回复但听不到声音 | 检查 Windows 输出设备、音量和静音状态；对照 TTS 日志及 `--save-audio` 结果 |
| 想看机器人到底听到什么 | `--save-input input.wav` 保存上行音频 |

默认配置在连接就绪时提前建立 LLM/TTS 连接；对话时 LLM 文本直接追加到同一个
TTS 会话，接收文字和合成音频并行。开场白也走同一条双向合成链路。
首包日志分别记录 ASR 提交后等待、LLM 首 token、首文字到首音频，避免混淆计时范围。

依赖更新后，在 `example/x2_agent/` 目录重新执行 `python -m pip install -r requirements.txt`。

## 运行回归测试

在 `example/x2_agent` 目录执行：

```powershell
python -B -m unittest discover -s tests -v
```

测试使用本机模拟服务，不需要启动 Unity 或调用豆包云服务。`-B` 避免生成新的 Python 字节码缓存。

协议细节见[接口文档](../../../../docs/zh-CN/interface.md)，模块与验证流程见[开发指南](../../../../docs/zh-CN/development.md)，提交修改见[贡献指南](../../../../CONTRIBUTING.md)。

## 运行日志

Unity 运行时日志会通过同一连接以 `agentsdk.runtime.log` 外发。
两个客户端均显示 Info、Warning、Error，以及异常堆栈；开场白和对话期间也继续接收。
日志缓存溢出或内容过长时会显示丢弃计数、截断提示。字段和交付边界见接口文档 §4.6。

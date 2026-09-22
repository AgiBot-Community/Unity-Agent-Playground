# 示例 Agent：让机器人开口说话、会做动作

**中文** | [English](docs/README.en.md) | [Français](docs/README.fr.md)

`example/` 目录是一个能直接跑的 Agent 参考实现。跑起来之后，你可以对着机器人说话，它会用语音回答你；说"挥挥手""做个开心的表情""往前走一米"，机器人真的会做。

两个客户端，用途不同：

| 文件 | 什么时候用 |
|---|---|
| `python agent.py` | **正式体验/演示**。ASR 边录边传、Mini 模型流式输出、双向 TTS 增量合成，LLM/TTS 并行 |
| `python demo.py` | **先跑通网关**。不依赖云服务，收到语音后回固定文字并播放内置录音，不进行真实识别或实时语音合成；录音不可用时退回正弦提示音 |

建议路径：先用 demo 跑通 → 再配 Key 切 doubao 全链路。

## 工程结构

| 路径 | 用途 |
|---|---|
| `agent.py` / `demo.py` | 正式 Agent 与离线 demo 的脚本入口 |
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
| `.env.example` | 可提交的配置模板；复制为 `.env` 后填写自己的 Key |

配置模板由 Git 管理，见 [`.env.example`](.env.example)；填写密钥后的 `.env` 不提交。文件纳入范围以仓库 [`.gitignore`](../.gitignore) 为准。

## 第一步：准备依赖（一次性）

- Python 3.10+
- 火山引擎账号（仅 doubao 客户端需要），开通：
  - 语音技术（ASR + TTS）→ `DOUBAO_SPEECH_API_KEY`
  - 火山方舟（LLM）→ `ARK_API_KEY`

在 `example/` 目录打开 PowerShell：

```powershell
python -m pip install -r requirements.txt
```

下文以 Windows PowerShell 启动命令为例。使用 Python 3.10+；Linux/macOS 可按本机命令使用 `python3`。已有所需依赖时可直接启动脚本。

## 第二步：连上机器人

先选择一种方式启动机器人侧：

- **从 EXE 开始**：在 Windows 上双击 [`../exe/x2模拟器.exe`](../exe/x2模拟器.exe)，详见 [EXE 运行说明](../docs/simulator.md)。
- **从 Unity 工程开始**：用 Unity **2022.3.62f3c1** 打开 `unity-agent-playground/`，打开 `Assets/X02Competition/Scenes/scene.unity` 并点击 Play，详见 [Unity 工程指南](../docs/unity.md)。

两种方式使用同一个本机网关端口 `9002`，只启动其中一种。然后在 `example/` 目录启动一个 Agent：

```powershell
# 先验证连通（无需 Key，机器人会回固定台词）
python demo.py

# 用 Ctrl+C 退出 demo 后，配置 Key 跑全链路；已有 .env 时保留原配置
if (-not (Test-Path .env)) { Copy-Item .env.example .env }
# 编辑 .env，填入两个 API Key
python agent.py
```

看到 `agent 会话就绪 state=online` 即连接成功。对机器人说话试试。

直接运行 `agent.py` 或 `demo.py`。`x2_agent/` 存放内部实现，保留在入口脚本旁即可；只需安装 `requirements.txt` 中的第三方依赖。

机器人不在本机时可指定 `--host 192.168.x.x --port 9002`，前提是网关确实对外监听、端口可达；仅修改客户端参数不会改变 Unity 的监听范围。

## 第三步：体验技能

连上后机器人先播开场白"你好，我是灵犀，有什么可以帮您？"（doubao 客户端为合成语音；demo 客户端播放内置开场白录音），随后就可以对机器人说：

| 你说 | 机器人做 |
|---|---|
| "挥挥手" / "张开双臂" | 挥手 / 张开双臂（2 个基本动作） |
| "你开心吗" / "给我比个爱心" / "我有点难过" / "你生气啦" | 头部表情屏切换（5 种经典表情：开心/难过/惊讶/生气/爱心，另支持 neutral 复位） |
| "往前走一米" / "向左转" | 步态前进 / 原地转向 |
| "停" | 立即停止 |

回答时嘴巴随语音张合。技能列表对应当前 Unity 源码；仓库中的便携 EXE 已于 2026-09-23 从当前工程重新构建。

当前语音流程是半双工，播报期间 VAD 暂停；等待播报结束后再说话。默认音色和提示词面向中文，文档翻译不改变语音服务或界面语言。

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

# 默认优先速度：Mini 模型 + 双向 TTS + 自动预连接/连接复用
# 如需切回原来的 Turbo 模型（.env 的 DOUBAO_LLM_MODEL 也可覆盖默认值）
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
脚本默认读取 `example/` 项目根目录的 `.env`。
从仓库根目录可直接执行 `python example/agent.py`，默认仍读取 `example/.env`。也可通过 `--env-file` 参数指定自己的配置文件路径；指定的文件不存在会报错。

| 变量 | 必填 | 说明 |
|---|---|---|
| `DOUBAO_SPEECH_API_KEY` | 是 | 语音控制台 API Key（ASR + TTS 共用） |
| `ARK_API_KEY` | 是 | 方舟 API Key（LLM） |
| `DOUBAO_LLM_MODEL` | 否 | 默认 `doubao-seed-2-0-mini-260428` |
| `DOUBAO_TTS_SPEAKER` | 否 | 默认 `zh_female_wanqudashu_moon_bigtts`（1.0 音色，勿混用 2.0 音色） |
| `DOUBAO_ASR_RESOURCE_ID` | 否 | 默认 `volc.bigasr.sauc.duration` |

## 出问题了？

| 现象 | 怎么办 |
|---|---|
| 连不上（Connection refused） | 机器人侧没启动，或不在本机 → 加 `--host` |
| 握手 401 | 签名错（严格模式下）；核对 appSecret 与签名串格式 |
| 握手 503 | 已有一个会话没退出（单会话限制）；关掉旧的客户端再连 |
| 没有识别结果 | 音频太短/太轻；确认麦克风没被系统静音 |
| 说完后等待很久 | 对比 `[录音 start→commit]`、`[ASR …ms（commit 后）]`、`[LLM 首 token]`；ASR 建连应与录音重叠。start→commit 包含说话时长及 Unity 的静音检测等待，不等于 ASR 耗时 |
| LLM 404 | 模型 ID 不完整；须用带日期后缀的完整 ID（如 `doubao-seed-2-0-mini-260428`）或接入点 `ep-xxx` |
| TTS 403 | 音色与资源不匹配；`seed-tts-1.0` 只能用 1.0 音色 |
| 收到回复但听不到声音 | 检查 Windows 输出设备、音量和静音状态；对照 TTS 日志及 `--save-audio` 结果 |
| 想看机器人到底听到什么 | `--save-input input.wav` 保存上行音频 |

默认配置在连接就绪时提前建立 LLM/TTS 连接；对话时 LLM 文本直接追加到同一个
TTS 会话，接收文字和合成音频并行。开场白也走同一条双向合成链路。
Mini 优先语音交互速度，复杂推理能力与原 Turbo 模型可能不同；动作工具调用保留。
首包日志分别记录 ASR 提交后等待、LLM 首 token、首文字到首音频，避免混淆计时范围。

依赖统一维护在 `requirements.txt`，更新后请在`example/` 目录执行 `python -m pip install -r requirements.txt`。

## 运行回归测试

在 `example` 目录执行：

```powershell
python -B -m unittest discover -s tests -v
```

测试使用本机模拟服务，不需要启动 Unity 或调用豆包云服务。`-B` 避免生成新的 Python 字节码缓存。

## 开发约定

在 `example/` 目录执行 `python -m pip install -r requirements.txt` 安装依赖，再使用 `python agent.py` 或 `python demo.py` 启动。

- 业务变更写入 `x2_agent/`，`example/` 根目录提供入口脚本、依赖列表、配置模板和使用说明。
- 云端传输分别维护在 ASR、LLM 和 TTS 模块；Unity 消息结构统一维护在 `gateway.py`。
- 添加或修改行为时，在 `tests/` 中补充相应的本机模拟测试。
- 新增文件遵循仓库 [`.gitignore`](../.gitignore)，文档中的工程结构只列出需要版本管理的文件。

协议细节见 [接口文档](../docs/interface.md)，维护流程见 [开发指南](../docs/development.md)。

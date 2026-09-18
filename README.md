# Unity Agent Playground · X2

**中文** | [English](docs/README.en.md) | [Français](docs/README.fr.md)

通过 Python 语音 Agent 与 Unity 中的 X2 机器人对话，并触发挥手、移动和表情。仓库提供 Windows 单文件模拟器、可安装的 Python 示例及网关协议文档。

## 仓库结构

| 路径 | 内容 |
|---|---|
| [exe/](docs/simulator.md) | `x2模拟器.exe`，66.88 MB 的 Unity 单文件便携程序 |
| [example/](example/README.md) | Python 包 `x2_agent`、配置模板及本机回归测试 |
| [docs/](docs/index.md) | 三语文档索引、接口规范及开发指南 |
| `scripts/check_docs.py` | 文档语言覆盖与本地链接检查 |
| `.github/workflows/ci.yml` | Windows Python 测试与文档检查 |

Unity 是 WebSocket 服务端；Python Agent 接收麦克风音频，调用 ASR、LLM 和 TTS，再将文字、音频及动作指令发回 Unity。模拟器和 Agent 分别启动。

本仓库不包含完整 Unity 工程。独立打包资料保存在维护者本机的同级 `../x2-simulator/`，不随仓库分发。仅使用模拟器不需要这些资料。

## 快速开始

需要 Windows 10/11 x64、Python 3.10+、麦克风和扬声器。豆包对话还需火山引擎语音 API Key 和方舟 API Key；离线 demo 不需要云服务。

1. 双击 [exe/x2模拟器.exe](exe/x2模拟器.exe)，等待机器人窗口出现。按 **F1** 显示调试面板。
2. 在仓库根目录打开 PowerShell，安装示例并运行 demo：

```powershell
cd example
python -m venv .venv
.\.venv\Scripts\python.exe -m pip install -e .
.\.venv\Scripts\python.exe -m x2_agent.demo
```

demo 返回固定文字和正弦提示音，用来验证网关与音频链路，不进行真实语音识别或语音合成。

3. 按 Ctrl+C 退出 demo，然后配置并启动豆包客户端：

```powershell
if (-not (Test-Path .env)) { Copy-Item .env.example .env }
# 编辑 .env，填写 DOUBAO_SPEECH_API_KEY 和 ARK_API_KEY
.\.venv\Scripts\python.exe -m x2_agent
```

等待开场白结束再说话，例如“你好”“挥挥手”“往前走一米”。同一时间只能连接一个 Agent。当前流程为半双工：播报期间暂停语音检测，普通说话不会打断播报；协议提供显式打断指令。

文档提供三种语言，不代表语音模型、默认中文音色或 Unity 界面已完成三语适配。

## 单文件分发

只分发 `exe/x2模拟器.exe` 即可运行模拟器，不包含 Python 或 API Key。首次启动静默释放资源到 `%LOCALAPPDATA%\x2sim\`，以后复用缓存，无需手动解压或配置。建议至少留出 500 MB 磁盘空间。

本机曾测得首次打开窗口约 12.6 秒、后续约 1.8 秒，其他电脑可能不同。语音响应耗时还受静音检测、网络、模型和语音资源影响，不保证固定延迟。

## 开发与验证

```powershell
# 从仓库根目录检查文档
python -B scripts/check_docs.py
cd example
# 本机测试，不调用云服务
.\.venv\Scripts\python.exe -B -m unittest discover -s tests -v
```

详见[开发指南](docs/development.md)。密钥、录音、缓存与本地诊断资料不提交到 Git；模拟器 EXE 是有意保留的分发产物。

## 文档

| 需求 | 文档 |
|---|---|
| 模型、音色、开场白、参数及排障 | [示例 Agent](example/README.md) |
| 缓存、启动与重新封装 | [模拟器说明](docs/simulator.md) |
| 自己实现 Agent、鉴权和消息时序 | [网关协议](docs/interface.md) |
| 所有语言文档 | [文档索引](docs/index.md) |

# X2 Agent Playground

[![AgiBot Community](https://img.shields.io/badge/Community-AgiBot-181717?style=flat-square&logo=github&logoColor=white)](https://github.com/AgiBot-Community)
[![GitHub Issues](https://img.shields.io/badge/Feedback-GitHub_Issues-238636?style=flat-square&logo=github&logoColor=white)](https://github.com/AgiBot-Community/Unity-Agent-Playground/issues)

![Unity 2022.3](https://img.shields.io/badge/Unity-2022.3-222222?style=flat-square&logo=unity&logoColor=white)
![C#](https://img.shields.io/badge/C%23-512BD4?style=flat-square&logo=dotnet&logoColor=white)
![Python 3.10+](https://img.shields.io/badge/Python-3.10%2B-3776AB?style=flat-square&logo=python&logoColor=white)

🌐 **中文** | [English ↗](docs/README.en.md) | [Français ↗](docs/README.fr.md)

基于 Unity 的 X2 人形机器人 Agent 开发环境：机器人侧提供网关与动作/表情/步态技能，接入你自己的 Agent 即可实现语音对话驱动机器人。

## 仓库结构

| 路径 | 内容 |
|---|---|
| [exe/](docs/simulator.md) | `x2模拟器.exe` 单文件便携程序及 SHA-256 校验文件 |
| [unity-agent-playground/](docs/unity.md) | Unity 工程源码、机器人模型、技能与网关实现 |
| [example/](example/README.md) | Python Agent 脚本、配置模板及本机回归测试 |
| [docs/](docs/index.md) | 三语文档索引、接口规范及开发指南 |

Unity 是 WebSocket 服务端；Python Agent 接收麦克风音频，调用 ASR、LLM 和 TTS，再将文字、音频及动作指令发回 Unity。模拟器和 Agent 分别启动。

本文及配套指南以仓库中纳入 Git 管理的程序、源码、配置模板和文档为入口。Unity 编辑器版本以 [ProjectVersion.txt](unity-agent-playground/ProjectSettings/ProjectVersion.txt) 为准，当前为 `2022.3.62f3c1`。

## 快速开始

先选择一种方式启动机器人。仅运行 EXE 不需要 Unity 或 Python；启动示例 Agent 才需要 Python 3.10+。语音交互需要麦克风和扬声器，真实豆包对话还需火山引擎语音与方舟 API Key。

### 路径 A：从 EXE 开始

1. 在 Windows 10/11 x64 上双击 [exe/x2模拟器.exe](exe/x2模拟器.exe)，等待机器人窗口出现。可使用随附的 [SHA-256 文件](exe/x2模拟器.sha256) 核对下载文件。
2. 按 **F1** 显示调试面板。无需 Agent，也可用技能按钮测试动作。
3. 需要语音交互时，继续下面的“启动 Agent”。完整操作见 [EXE 使用指南](docs/simulator.md)。

### 路径 B：从 Unity 项目开始

1. 安装 Unity Hub 和 **2022.3.62f3c1** 编辑器；安装与版本选择见 [Unity 工程指南](docs/unity.md)。
2. 在 Hub 中选择 **Add project from disk**，添加包含 `Assets/`、`Packages/`、`ProjectSettings/` 的 [unity-agent-playground/](unity-agent-playground/) 目录。
3. 等待依赖导入与编译完成，打开 `Assets/X02Competition/Scenes/scene.unity`，点击 **Play**。
4. 点击 Game 窗口后按 **F1** 查看状态，继续下面的“启动 Agent”。再次点击 Play 可停止场景。

两条路径都监听 `127.0.0.1:9002`，同一时间只运行一个机器人窗口或 Editor Play 场景。**C** 轮换视角，**F2–F5** 分别切换全景、正面跟随、侧面跟随与自由环绕；自由环绕支持右键拖动和滚轮。

### 启动 Agent（两条路径通用）

保持机器人运行，在仓库根目录打开 PowerShell：

```powershell
cd example
python -m pip install -r requirements.txt
python demo.py
```

demo 返回固定文字并播放内置录音，用来验证网关与音频链路，不进行真实语音识别或实时语音合成。录音缺失或格式不符时退回正弦提示音。

看到 `state=online` 表示已连接。按 Ctrl+C 退出 demo，然后在同一个 `example/` 终端中根据仓库的 `.env.example` 配置并启动豆包客户端：

```powershell
if (-not (Test-Path .env)) { Copy-Item .env.example .env }
# 编辑 .env，填写 DOUBAO_SPEECH_API_KEY 和 ARK_API_KEY
python agent.py
```

等待开场白结束再说话，例如“你好”“挥挥手”“往前走一米”。同一时间只能连接一个 Agent。当前流程为半双工：播报期间暂停语音检测，普通说话不会打断播报；协议提供显式打断指令。

文档提供三种语言，不代表语音模型、默认中文音色或 Unity 界面已完成三语适配。

## 分发与源码构建

仓库中的 `exe/x2模拟器.exe` 可单文件分发，校验值见同目录的 `.sha256` 文件；Python Agent 由 `example/` 单独提供。

修改 Unity 源码后，按照 [Unity 工程指南](docs/unity.md) 使用 Build Settings 构建，并完整保留 Unity 输出目录。源码修改不会自动更新仓库中的便携 EXE。

## 开发与验证

```powershell
cd example
# 本机测试，不调用云服务
python -B -m unittest discover -s tests -v
```

详见[开发指南](docs/development.md)。文件纳入范围以 [.gitignore](.gitignore) 和工程内的忽略规则为准；配置模板及模拟器 EXE 属于仓库交付内容，个人密钥不提交。

## 文档

| 需求 | 文档 |
|---|---|
| Unity Hub 下载安装、独立工程导入、场景运行与构建 | [Unity 工程入门](docs/unity.md) |
| 模型、音色、开场白、参数及排障 | [示例 Agent](example/README.md) |
| 从 EXE 启动、校验文件与切换视角 | [模拟器说明](docs/simulator.md) |
| 自己实现 Agent、鉴权和消息时序 | [网关协议](docs/interface.md) |
| 所有语言文档 | [文档索引](docs/index.md) |

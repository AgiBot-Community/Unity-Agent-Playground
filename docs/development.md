# 开发与维护

**中文** | [English](development.en.md) | [Français](development.fr.md)

本指南面向修改 Python 客户端、Unity 网关或发布文件的开发者。首次运行请先阅读[快速开始](../README.md)；这里重点说明代码职责、验证步骤和交付要求。

## 环境与入口

先启动机器人侧：直接运行 [仓库 EXE](simulator.md)，或通过 [Unity 工程](unity.md) 打开主场景并进入 Play。两者使用相同的 Agent 接口，不同时启动。

使用 Python 3.10+，在 `example/x2_agent/` 中执行 `python -m pip install -r requirements.txt` 安装第三方依赖，再运行 `python agent.py` 或 `python demo.py`。完整步骤见 [Agent 指南](../example/x2_agent/README.md)。

两个项目分别维护自己的 `requirements.txt`；语音配置 `.env.example` 和 `.env` 仅属于 Agent。
Agent 配置优先级为命令行参数 > 已有环境变量 > `example/x2_agent/.env` > 默认值；
控制台不读取 `.env`，网关签名参数通过界面输入。导入模块不会加载密钥。
Agent 可通过 `--env-file <路径>` 指定其它配置。

## 模块边界

`example/x2_agent/x2_agent/agent.py` 负责会话和并发调度；同目录的 `asr.py`、`llm.py`、`tts.py`、`sentence_tts.py` 分别负责语音与模型传输。Unity 消息协议集中于 `gateway.py`，豆包二进制帧集中于 `speech_protocol.py`。音频文件和配置属于 `audio.py`、`config.py`。`example/x2_agent/agent.py` 和 `example/x2_agent/demo.py` 是启动入口。

控制台实现位于 `example/x2_console/x2_console/`，入口是 `example/x2_console/main.py`。
控制台只包含管理与监控协议，不导入 Agent 项目或云服务库。语音实现统一维护在 Agent；
修改共同支持的网关协议时，分别更新两项目并运行各自测试。

新增传输行为时保持取消、错误传播和连接清理逻辑；不要让同步网络请求阻塞语音管线。修改协议字段时同步核对 demo 和正式客户端。

## 检查与测试

从仓库根目录：

```powershell
cd example/x2_agent
python -B -m unittest discover -s tests -v
cd ../x2_console
python -B -m unittest discover -s tests -v
cd ../..
```

请使用已安装本项目依赖的 Python。测试使用本机模拟服务，不需要 Unity 或 API Key。

Unity 侧的审计回归会进入真实 Play Mode，验证短音频、输入缓存、技能取消、
WebSocket 分片与大小限制、慢客户端关闭以及 Unity 日志外发。在仓库根目录执行：

```powershell
# $unityEditor 指向本机 2022.3.62f3c1 的 Editor/Unity.exe
& $unityEditor -batchmode -projectPath "$PWD/unity-agent-playground" `
  -executeMethod AuditRegressionVerification.Run -logFile "$PWD/unity-regressions.log"
```

测试自行退出，不要添加 `-quit`；成功标记为 `AUDIT_REGRESSION_PASSED`。
它会有意生成 warning、error、assert 和 exception 来验证转发，不能仅凭日志里有
“error”判断测试失败，应同时检查成功标记和退出码。关闭占用该项目的编辑器后运行。
此验证不调用云服务或使用真实麦克风。它同时运行多连接验证，成功标记
`MULTI_SESSION_VERIFICATION_PASSED`，覆盖控制台固定最高优先级、其它优先级调整、
低优先级请求拒绝、音频单路发送、日志广播和断线交接。

手动验收分两步进行：

1. **本机联调：** 启动一个模拟器，连接 `demo.py`，确认 `state=online`、字幕和音频播放，再测试 F1 面板中的技能按钮。
2. **云端语音：** 停止 demo，配置密钥并启动 `agent.py`。等开场白结束后说一句话，核对 ASR 识别、LLM 回复与 TTS 播放，再请求一个技能，最后退出并重新连接。

记录所用模型、语音资源、测试结果和各阶段耗时。区分录音时间、静音检测和云端处理时间，避免把一次测量当作固定性能。

## 提交与文档

- 功能变更包含相应的本机模拟测试；文档变更检查相对链接和三语覆盖。
- 中文无后缀，英文 `.en.md`，法文 `.fr.md`；技术标识保持原样。
- 文档的目录表、链接和操作入口只引用纳入 Git 管理的文件；新增文件先确认不被忽略规则排除。忽略范围见 [仓库规则](../.gitignore)，配置以 [模板](../example/x2_agent/.env.example) 为准；个人密钥不提交。
- `exe/x2模拟器.exe` 是明确的交付产物，不能被通用 `*.exe` 规则忽略。更新时记录体积、SHA-256 和启动验证。
- 不将维护者个人路径、临时工具或未随仓库交付的脚本写成使用前提。
- PR 说明应包含问题、最终行为和验证结果；未做的云端或 Unity 测试应明确注明。

## Unity 源码与发布文件

手势修改可额外运行真实场景验证：

```powershell
& $unityEditor -batchmode -projectPath "$PWD/unity-agent-playground" `
  -executeMethod GestureVerification.Run -logFile "$PWD/unity-gestures.log"
```

验证会播放挥手和双臂展开，检查关节限位、目标速度、身体倾斜与位移、切换/打断回调和
episode reset；成功标记为 `GESTURE_VERIFICATION_PASSED`。测试自行退出，不加 `-quit`。
正面与斜侧面截图、关节采样 CSV 和指标写入仓库 `.diagnostics/gesture-polish/`。
测试关闭麦克风，不调用云服务。动作曲线位于 `GestureMotion.cs`，
运行时平滑、限位和生命周期处理位于 `GesturePlayer.cs`。

Unity 工程位于 [unity-agent-playground/](../unity-agent-playground/)，使用 `2022.3.62f3c1`。启动入口、HUD 和相机位于 `Assets/X02Competition/Bootstrap/`，回归检查位于 `Assets/X02Competition/Tests/Editor/`。修改后按 [Unity 指南](unity.md) 使用 Build Settings 构建，并保留完整输出。

Git 中的发布文件为 [x2模拟器.exe](../exe/x2模拟器.exe) 和 [校验文件](../exe/x2模拟器.sha256)。替换发布文件时同步更新校验值并验证启动、Agent 连接与视角切换。普通 Unity Build 不会自动更新这两个文件。

也可通过批处理构建 Windows x64 Player：

```powershell
& $unityEditor -batchmode -quit -projectPath "$PWD/unity-agent-playground" `
  -executeMethod X2PlayerBuild.Windows -x2Output "$PWD/build/UnityEnvironment.exe" `
  -logFile "$PWD/unity-build.log"
```

`-x2Output` 必须是绝对 EXE 路径。成功标记为 `X2_WINDOWS_BUILD_PASSED`；
再使用 `tools/unity-packager/pack-x2.ps1` 将完整 Player 目录打包为便携 EXE。

# 开发与维护

**中文** | [English](../en/development.md) | [Français](../fr/development.md)

本页记录模块职责、测试和发布流程。首次运行见[快速开始](../../README.md)，Issue 与 Pull Request 的提交方式见[贡献指南](../../CONTRIBUTING.md)。

## 环境与入口

联调时先运行[便携模拟器](simulator.md)，或在 [Unity 工程](unity.md)中打开主场景并进入 Play。两者使用相同的 Agent 接口，同一时间运行一个实例。

使用 Python 3.10+，在 `example/x2_agent/` 中执行 `python -m pip install -r requirements.txt` 安装第三方依赖，再运行 `python agent.py` 或 `python demo.py`。完整步骤见 [Agent 指南](../../example/x2_agent/docs/zh-CN/README.md)。

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

Unity 回归测试进入 Play Mode，验证短音频、输入缓存、技能取消、
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

## Unity 专项测试

表情修改可运行画板渲染验证：

```powershell
& $unityEditor -batchmode -quit -projectPath "$PWD/unity-agent-playground" `
  -executeMethod EmotionVerification.Run -logFile "$PWD/emotion-verification.log"
```

不要添加 `-nographics`。成功标记为 `EMOTION_VERIFICATION_PASSED`；
`.diagnostics/emotions/atlas.png` 从左到右显示 neutral、happy、sad、surprised、
angry、love，上排静态、下排说话。验证覆盖画板位置、旋转、尺寸及分辨率、
表情轮廓差异、说话时保留眼眉、定时回到 neutral。发布前同时检查实际画面。

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

## 构建与发布

Unity 工程位于 [unity-agent-playground/](../../unity-agent-playground/)，使用 `2022.3.62f3c1`。启动入口、HUD 和相机位于 `Assets/X02Competition/Bootstrap/`，回归检查位于 `Assets/X02Competition/Tests/Editor/`。修改后按 [Unity 指南](unity.md) 使用 Build Settings 构建，并保留完整输出。

发布文件为同一 [GitHub Release](https://github.com/AgiBot-Community/Unity-Agent-Playground/releases) 下的便携 EXE 和校验文件。构建后运行便携打包器，验证启动、Agent 连接与视角切换，再上传两个附件。普通 Unity Build 不会自动打包或发布。

也可通过批处理构建 Windows x64 Player：

```powershell
& $unityEditor -batchmode -quit -projectPath "$PWD/unity-agent-playground" `
  -executeMethod X2PlayerBuild.Windows -x2Output "$PWD/build/UnityEnvironment.exe" `
  -logFile "$PWD/unity-build.log"
```

`-x2Output` 必须是绝对 EXE 路径。成功标记为 `X2_WINDOWS_BUILD_PASSED`；
再使用 `tools/unity-packager/pack-x2.ps1` 将完整 Player 目录打包为便携 EXE。

发布时：

1. 使用 `x2-simulator-windows-x64.exe` 作为附件名，确保 `.sha256` 文件记录同名 EXE。
2. 验证启动、Agent 连接、技能与视角切换，记录构建版本、文件大小和 SHA-256。
3. 将 EXE 和校验文件上传到同一个 GitHub Release。

`exe/`、`build/` 和 `release/` 是被 Git 忽略的本地目录。发布附件不提交到源码仓库。

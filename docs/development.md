# 开发与维护

**中文** | [English](development.en.md) | [Français](development.fr.md)

本指南面向修改 Python 客户端、Unity 网关或发布文件的开发者。首次运行请先阅读[快速开始](../README.md)；这里重点说明代码职责、验证步骤和交付要求。

## 环境与入口

先启动机器人侧：直接运行 [仓库 EXE](simulator.md)，或通过 [Unity 工程](unity.md) 打开主场景并进入 Play。两者使用相同的 Agent 接口，不同时启动。

使用 Python 3.10+，在 `example/` 中执行 `python -m pip install -r requirements.txt` 安装第三方依赖，再运行 `python agent.py` 或 `python demo.py`。完整步骤见 [Agent 指南](../example/README.md)。

Python 第三方依赖统一声明在 `example/requirements.txt`。配置优先级为命令行参数 > 已有环境变量 > `example/.env` > 默认值；导入模块不会加载密钥。需要使用其他配置文件时，传入 `--env-file <路径>`。

## 模块边界

`example/x2_agent/agent.py` 负责会话和并发调度；同目录的 `asr.py`、`llm.py`、`tts.py`、`sentence_tts.py` 分别负责语音与模型传输。Unity 消息协议集中于 `gateway.py`，豆包二进制帧集中于 `speech_protocol.py`。音频文件和配置属于 `audio.py`、`config.py`。`example/agent.py` 和 `example/demo.py` 是启动入口。

新增传输行为时保持取消、错误传播和连接清理逻辑；不要让同步网络请求阻塞语音管线。修改协议字段时同步核对 demo 和正式客户端。

## 检查与测试

从仓库根目录：

```powershell
cd example
python -B -m unittest discover -s tests -v
```

请使用已安装本项目依赖的 Python。测试使用本机模拟服务，不需要 Unity 或 API Key。

手动验收分两步进行：

1. **本机联调：** 启动一个模拟器，连接 `demo.py`，确认 `state=online`、字幕和音频播放，再测试 F1 面板中的技能按钮。
2. **云端语音：** 停止 demo，配置密钥并启动 `agent.py`。等开场白结束后说一句话，核对 ASR 识别、LLM 回复与 TTS 播放，再请求一个技能，最后退出并重新连接。

记录所用模型、语音资源、测试结果和各阶段耗时。区分录音时间、静音检测和云端处理时间，避免把一次测量当作固定性能。

## 提交与文档

- 功能变更包含相应的本机模拟测试；文档变更检查相对链接和三语覆盖。
- 中文无后缀，英文 `.en.md`，法文 `.fr.md`；技术标识保持原样。
- 文档的目录表、链接和操作入口只引用纳入 Git 管理的文件；新增文件先确认不被忽略规则排除。忽略范围见 [仓库规则](../.gitignore)，配置以 [模板](../example/.env.example) 为准；个人密钥不提交。
- `exe/x2模拟器.exe` 是明确的交付产物，不能被通用 `*.exe` 规则忽略。更新时记录体积、SHA-256 和启动验证。
- 不将维护者个人路径、临时工具或未随仓库交付的脚本写成使用前提。
- PR 说明应包含问题、最终行为和验证结果；未做的云端或 Unity 测试应明确注明。

## Unity 源码与发布文件

Unity 工程位于 [unity-agent-playground/](../unity-agent-playground/)，使用 `2022.3.62f3c1`。启动入口、HUD 和相机位于 `Assets/X02Competition/Bootstrap/`，回归检查位于 `Assets/X02Competition/Tests/Editor/`。修改后按 [Unity 指南](unity.md) 使用 Build Settings 构建，并保留完整输出。

Git 中的发布文件为 [x2模拟器.exe](../exe/x2模拟器.exe) 和 [校验文件](../exe/x2模拟器.sha256)。替换发布文件时同步更新校验值并验证启动、Agent 连接与视角切换。普通 Unity Build 不会自动更新这两个文件。

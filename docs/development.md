# 开发与维护

**中文** | [English](development.en.md) | [Français](development.fr.md)

## 环境与入口

在 `example/` 中创建 Python 3.10+ 虚拟环境，执行 `python -m pip install -e .`。使用 `python -m x2_agent` 或 `python -m x2_agent.demo` 启动，不生成客户端 EXE。完整安装步骤见 [Agent 指南](../example/README.md)。

`pyproject.toml` 是依赖的唯一声明位置。配置优先级为命令行、现有环境变量、`.env`、默认值；导入模块不会加载密钥。显式配置使用 `--env-file`。

## 模块边界

`agent.py` 负责会话和并发调度；ASR、LLM、双向 TTS、逐句 TTS 分别由 `asr.py`、`llm.py`、`tts.py`、`sentence_tts.py` 负责。Unity 消息协议集中于 `gateway.py`，豆包二进制帧集中于 `speech_protocol.py`。音频文件和配置属于 `audio.py`、`config.py`。

新增传输行为时保持取消、错误传播和连接清理逻辑；不要让同步网络请求阻塞语音管线。修改协议字段时同步核对 demo 和正式客户端。

## 检查与测试

从仓库根目录：

```powershell
python -B scripts/check_docs.py
cd example
python -B -m unittest discover -s tests -v
```

请使用已安装本项目依赖的 Python。测试使用本机模拟服务，不需要 Unity 或 API Key。CI 配置在 Windows 上对 Python 3.10 和 3.12 执行文档检查、本机测试和模块入口检查；只有工作流实际运行后才能声称远程 CI 通过。

手动全链路验收：启动模拟器 → 启动一个 Agent → 等开场白结束 → 说一句话 → 确认 ASR、LLM 和音频 → 测一个技能 → 退出并重连。记录模型、语音资源和各阶段耗时，不将单次测量写成保证。

## 提交与文档

- 功能变更包含相应的本机模拟测试；文档变更检查相对链接和三语覆盖。
- 中文无后缀，英文 `.en.md`，法文 `.fr.md`；技术标识保持原样。
- 不提交 `.env`、录音、日志、虚拟环境、缓存和 `.diagnostics/`。不要把原始含密钥日志粘贴进问题或 PR。
- `exe/x2模拟器.exe` 是明确的交付产物，不能被通用 `*.exe` 规则忽略。更新时记录体积、SHA-256 和启动验证。
- `build/`、`dist/`、`*.egg-info/` 为可再生成产物；清理仅限确认的生成目录，保留 `.env` 和源码。
- PR 说明应包含问题、最终行为和验证结果；未做的云端或 Unity 测试应明确注明。

## Unity 与权限边界

仓库没有完整 Unity 工程。维护者的 `../x2-simulator/` 保存独立打包材料，但普通克隆不会得到它。封装程序使用磁盘缓存，不能描述为零落盘运行。完整封装步骤见 [模拟器指南](simulator.md)。

# Agent 配置

以下命令在 `example/x2_agent/` 目录执行。凭据、模型和音色见
[Agent 指南](../../example/x2_agent/docs/zh-CN/README.md)；
配置模板为 [`.env.example`](../../example/x2_agent/.env.example)。

## 加载顺序

优先级为 **命令行参数 > 已有环境变量 > `.env` > 默认值**。
默认读取 Agent 项目根目录的 `.env`，与启动命令的工作目录无关。
导入 Python 模块不会读取密钥。指定配置文件不存在时，启动会报错。

```powershell
python agent.py --env-file .env.development
```

`.env.development` 等文件不会自动加载，需使用 `--env-file` 指定。

## 服务端点

| 环境变量 | 命令行参数 | 默认地址 |
|---|---|---|
| `ARK_API_URL` | `--ark-api-url` | `https://ark.cn-beijing.volces.com/api/v3/chat/completions` |
| `ASR_WS_URL` | `--asr-ws-url` | `wss://openspeech.bytedance.com/api/v3/sauc/bigmodel_nostream` |
| `TTS_WS_URL` | `--tts-ws-url` | `wss://openspeech.bytedance.com/api/v3/tts/bidirection` |
| `TTS_SENTENCE_WS_URL` | `--tts-sentence-ws-url` | `wss://openspeech.bytedance.com/api/v3/tts/unidirectional/stream` |

双向流式 TTS 与 `--tts-mode sentence` 使用不同端点。自建代理必须支持相应的
HTTP 或 WebSocket 协议及消息格式。仅更改地址不能适配另一种语音服务。

配置本机模拟服务时，例如：

```powershell
python agent.py --env-file .env.development --ark-api-url http://127.0.0.1:3000/mock/llm
```

查看合并后的端点，不发起网络请求：

```python
from x2_agent.agent import parse_args

config = parse_args(["--env-file", ".env.development"])
print(config.ark_api_url)
print(config.asr_ws_url)
print(config.tts_ws_url)
print(config.tts_sentence_ws_url)
```

服务模块中的 URL 常量只表示默认值，实际连接使用加载后的配置。

## 技能

Agent 默认加载项目根目录的 `skills.yaml`，通过 `--skills-file` 可选用其他文件。
其中的技能名称、参数范围和口播用于生成模型工具定义，并在命令发送前校验。
添加 YAML 条目不会自动创建 Unity 执行器，两端需要支持同一技能。

`movement/walk` 的距离范围为 0.2–5 米，`movement/turn` 的角度范围为 −360–360 度。
表情的 `durationMs` 为正数时按指定时长显示，为 0 或负数时持续显示。
不合法的类型、技能组合和非有限数值会被拒绝。

## 日志与指标

```powershell
python agent.py --log-level INFO --log-file agent.log --metrics-file metrics.json
```

`--log-level` 和 `--log-file` 控制 Python Agent 调度日志。
云服务传输层与远端 Unity 日志仍保留原有终端输出。
`--metrics-file` 在正常退出或 Ctrl+C 时写入统计，强制结束进程可能来不及写入。

每项耗时保留最近 1000 个样本，计数器累计本次运行：

- ASR 耗时从录音提交开始计算，不包含录音本身。
- TTS 首包耗时从首段回复文字开始计算。
- `skill_executed_count` 统计已下发命令；是否执行成功以 `skill_response.state` 为准。

错误含义见[错误代码参考](error-codes.md)，测试命令见[开发指南](development.md)。

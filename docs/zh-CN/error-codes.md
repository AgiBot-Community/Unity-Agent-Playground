# 错误代码参考

先区分握手的 HTTP 状态码、连接后的 `agentsdk.error`，以及技能执行状态。
技能的 `failed` 不是数字错误码。

## WebSocket 握手错误

| HTTP 状态码 | 含义 | 解决方法 |
|---|---|---|
| 400 | 握手头不兼容或格式错误 | 检查客户端 WebSocket 握手实现 |
| 401 | 签名验证失败 | 检查 `app_id`、`app_key`、`app_secret` 和系统时间 |
| 404 | 网关路径不存在 | 确认路径为 `/api/V1/open-portal/app/wss/agent-sdk` |
| 503 | 连接数达到上限 | 关闭不用的客户端或增加网关连接限制 |

## Agent 错误代码 (3xxx)

### ASR 相关 (31xx)

| 错误代码 | 消息类型 | 含义 | 可能原因 |
|---|---|---|---|
| 3101 | `agentsdk.error` | ASR 识别失败 | 网络问题、API Key 无效、音频格式不支持 |
| 3102 | `agentsdk.error` | ASR 结果为空 | 音频无语音内容、环境噪音过大、VAD 未检测到语音 |

### TTS 相关 (33xx)

| 错误代码 | 消息类型 | 含义 | 可能原因 |
|---|---|---|---|
| 3301 | `agentsdk.error` | TTS 合成失败（含超时） | 网络问题、API Key 无效、音色不匹配 |

### LLM 相关 (32xx)

| 错误代码 | 消息类型 | 含义 | 可能原因 |
|---|---|---|---|
| 3201 | `agentsdk.error` | LLM 请求失败（含空回复） | 网络问题、API Key 无效、模型 ID 错误 |

代码中的 `3202`、`3302` 为保留的细分类别，当前主运行流程不单独发送。

## 网关错误代码 (4xxx)

鉴权失败使用握手 HTTP 401。`4001–4003`、`4101–4102` 仅为 Python 模块中的
保留常量，当前 Unity 网关不发送这些错误帧。

### 权限与参数

| 错误代码 | 消息类型 | 含义 | 解决方法 |
|---|---|---|---|
| 4091 | `agentsdk.error` | 无动作或语音权限，或回复事件已失效 | 检查会话优先级、控制权和 eventId |
| 4092 | `agentsdk.error` | Python Agent 拒绝无效技能参数 | 检查 YAML、类型/名称组合及有限数值 |
| 4093 | `agentsdk.error` | Unity 拒绝优先级或接管设置 | 检查管理权限、目标会话及参数 |

直接发给 Unity 的无效运动参数、未知技能使用 `agentsdk.skill_response.state`
的 `state="failed"` 回报，不附带数字错误码。

## Unity 运行时日志

| 错误级别 | 含义 | 日志类型 |
|---|---|---|
| `info` | 信息日志 | `agentsdk.runtime.log` |
| `warning` | 警告（不影响运行） | `agentsdk.runtime.log` |
| `error` | 错误（可能影响功能） | `agentsdk.runtime.log` |

## 错误帧

```json
{
  "type": "agentsdk.error",
  "agentId": "demo-app",
  "robotCid": "cid-xxx",
  "eventId": "evt-xxx",
  "errorCode": 3101,
  "errorMsg": "asr failed: connection timeout"
}
```

排查时用 `eventId` 对齐同一轮录音和回复。遇到 `stale voice response event`，
检查该轮完成前是否出现新的 `audio_request.start`。
`--save-input`、`--save-audio` 可保存输入与回复音频，`--log-file` 可保存 Agent 调度日志。

## 参见

- [网关协议](interface.md) - 完整的消息格式说明
- [Agent 指南](../../example/x2_agent/docs/zh-CN/README.md) - 常见问题排查
- [开发指南](development.md) - 测试和调试方法

# Unity 日志

`UnityLogForwarder` 捕获 Unity 日志，在主线程将它们作为 `agentsdk.runtime.log`
发给已连接客户端。Agent 在终端显示，控制台支持筛选和导出。

## 级别

| Unity 调用 | 外发级别 | 用途 |
|---|---|---|
| `Debug.Log` | `info` | 连接、状态变化、正常执行过程 |
| `Debug.LogWarning` | `warning` | 可处理的异常，例如非法技能参数或回复超时 |
| `Debug.LogError`、assert、exception | `error` | 失败及异常堆栈 |

网关自身发出字符串日志，经 `GatewayLogBridge` 转为 `Debug.Log`。
消息中包含 “error” 不会自动改变外发级别；判断问题时同时查看消息、堆栈和执行结果。
当前没有统一的 Unity 日志级别开关，部分组件用各自的 `Verbose` 控制详细输出。

## 缓存与交付

- 离线捕获缓存保留最近 256 条日志。
- 每会话发送缓存最多 128 条；慢客户端不会阻塞其他订阅者。
- 每帧最多转发 32 条；单条消息最多 8192 字符，堆栈最多 16384 字符。
- 队列溢出和内容截断通过 `droppedCount`、`sessionDroppedCount`、`truncated` 回报。

连接恢复后可收到仍在缓存中的日志，不保证完整历史。
编译失败、进入运行时前的编辑器消息，以及崩溃后未发送的日志，需要查看本机日志文件。
完整字段见[网关协议](interface.md)的运行时日志一节。

## 编写日志

记录足够定位问题的上下文，例如组件、eventId、技能名称和错误原因：

```csharp
Debug.LogWarning("[SkillRouter] 无效参数: " + error.Message);
```

在状态变化时记录，避免在每个 Update 中重复输出。不要写入 API Key、签名密钥
或完整音频内容。对话日志中的字幕和错误消息也可能包含用户内容。

`Capture` 回调只入队，不执行网络 I/O，也不再次记录日志。队列溢出只累计丢弃计数，
否则转发器自身的日志可能形成反馈循环。日志订阅者的异常会被隔离，不能依赖抛异常
来中止会话。

## 验证

`AuditRegressionVerification` 会故意产生 warning、error、assert 和 exception，
检查级别、堆栈、后台线程捕获、缓存边界、重连与反馈循环。
应以退出码和 `AUDIT_REGRESSION_PASSED` 判断结果，不能只搜索日志中的 “error”。
命令见[开发指南](development.md)，关闭流程见[线程说明](unity-threading-guide.md)。

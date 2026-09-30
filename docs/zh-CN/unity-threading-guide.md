# Unity 网关线程与关闭流程

网关使用独立线程处理握手和收发，机器人组件由 Unity 主线程调用。
排查卡住、断线或退出问题时，可按下面的职责和关闭顺序检查。

## 线程分工

| 线程 | 工作 | 主要入口 |
|---|---|---|
| Unity 主线程 | 执行入站回调、更新 VAD/TTS/技能和界面 | `MainThreadPump`、`VirtualRobotRuntime.Update` |
| TCP 监听线程 | 接收连接、验证握手、创建会话 | `LinkskyGatewayServer.AcceptLoop` |
| 每会话接收线程 | 读取 WebSocket、解析消息、将业务回调入队 | `GatewaySession.RecvLoop` |
| 每会话发送线程 | 顺序发送协议帧和日志 | `GatewaySession.SendLoop` |

接收线程不直接操作 Transform、AudioSource 或 HUD。`ExecutePendingActions` 由主线程
泵调用，再把消息交给 `IRobotRuntime`。`Runtime` 引用由 `_runtimeLock` 保护；
这把锁不允许后台线程直接调用 Unity 组件。

## 共享状态

| 状态 | 同步方式 |
|---|---|
| 会话列表、优先级、握手中连接 | 服务端 `_lock` |
| 会话启动、入队、关闭、信号释放 | 会话 `_lifecycleLock` |
| 入站回调与出站帧 | `ConcurrentQueue` |
| 会话日志计数 | `Interlocked` |
| Unity 日志捕获缓存 | `UnityLogForwarder.Gate` |
| 当前录音轮次、回复等待、播放状态 | Unity 主线程 |

`readonly` 只限制引用重新赋值，不保证对象内部线程安全。`volatile` 也不能让
“检查状态、入队、发送信号”成为原子操作；这三个步骤仍需处于同一生命周期锁内。

## 会话关闭

1. `Close` 在 `_lifecycleLock` 中取得关闭权，禁止新入队并唤醒发送线程。
2. 在锁外关闭 socket，使阻塞的读写返回。
3. 发送线程退出时释放 `_sendSignal`；尚未启动线程的会话由 `Close` 直接释放。
4. 关闭事件移除服务端会话，并通知主线程处理机器人状态。

`StartThreads` 不会启动已关闭的会话。重复 `Close` 或 `Dispose` 不重复关闭。
`Dispose` 不在事件回调中等待收发线程，以免等待自身或形成交叉等待。
网络 I/O、外部事件回调和线程等待都不能放在生命周期锁内。

`Stop` 同时关闭监听器、握手中的连接和已有会话。握手结束后的注册步骤再次检查
停止状态，避免停止后留下新会话。若要重启同一实例，先确认原监听线程已退出；
已 `Dispose` 的实例不能重启。

## 事件与日志

`SessionOpened` 在监听线程触发。`Closed` 在取得关闭权的线程触发；
`FrameLogged` 也使用调用者线程。订阅者涉及 Unity 对象时，应使用主线程泵。

日志观察者抛异常不会中断会话注册、阻止其他日志观察者或占住连接名额。
不要在日志回调中再次写入同一日志通道。
工程通过 `Application.logMessageReceivedThreaded` 捕获后台日志；
这与后台访问场景对象的限制是两回事。

## 回归验证

`AuditRegressionVerification` 覆盖并发发送与释放、关闭事件重入、收发回调内关闭、
握手期间停止、慢客户端、线程退出和信号句柄释放。
执行命令及日志成功标记见[开发指南](development.md)。

实现入口：

- [GatewaySession.cs](../../unity-agent-playground/Assets/X02Competition/Gateway/GatewaySession.cs)
- [LinkskyGatewayServer.cs](../../unity-agent-playground/Assets/X02Competition/Gateway/LinkskyGatewayServer.cs)
- [VirtualRobotRuntime.cs](../../unity-agent-playground/Assets/X02Competition/Robot/VirtualRobotRuntime.cs)
- [日志说明](unity-logging-standards.md)

# 机器人侧程序（exe/）

双击运行即启动机器人网关：机器人出现在窗口中，开始监听 `127.0.0.1:9002` 等 Agent 接入。

## 运行须知

| 项 | 值 |
|---|---|
| 系统 | Windows 10/11 x64 |
| 硬件 | 麦克风（说话）+ 扬声器（播放） |
| 监听 | `127.0.0.1:9002`（跨机访问见下方"修改配置"） |
| 路径 | `/api/V1/open-portal/app/wss/agent-sdk` |
| 鉴权 | 宽松模式（不校验签名，示例项目可直接连） |
| 会话 | 单 Agent 连接；Agent 退出后可立即重连 |

- 窗口可以失焦，但**不要最小化**（需保持后台运行，否则音频/网络停摆）
- **F1** 呼出/隐藏调试面板（默认隐藏，录屏画面干净）
- 调试面板显示连接状态、ASR/LLM 字幕、技能触发记录，并可手动触发全部技能（无 Agent/无 Key 时验证动作用）

Agent 接入方式见 [../python-agent-client/README.md](../python-agent-client/README.md)，协议见 [../docs/interface.md](../docs/interface.md)。

## 重新打包

本目录存放 Unity Build 产物（exe + Data 文件夹一起拷入）。重新打包步骤：

1. Unity 2022.3.62 打开 `../unity-agent-playground` 工程
2. 场景：`Assets/X02Competition/Scenes/scene.unity`（含网关装配 + 调试面板）
3. `File → Build Settings → Windows (x86_64)`
4. Player Settings 勾选 `Run In Background`（必须）
5. Build 输出整个文件夹放入本目录

## 修改配置

需要严格鉴权 / 跨机监听时，改 `Assets/X02Competition/Bootstrap/CompetitionLauncher.cs` 后重新 Build：

- 跨机监听：监听地址改 `IPAddress.Any`
- 严格鉴权：`StrictAuth = true`，凭证 appId=`demo-app` / appKey=`demo-key` / appSecret=`demo-secret`

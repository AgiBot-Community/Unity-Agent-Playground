# 机器人侧程序（exe）

双击运行即可启动机器人网关：机器人出现在窗口中（调试面板默认隐藏，按 **F1** 呼出），开始监听 `127.0.0.1:9002` 等 Agent 接入（接入方式见 `../sample-project/README.md`）。Agent 连上后机器人会播报开场白。

## 运行须知

| 项 | 值 |
|---|---|
| 系统 | Windows 10/11 x64 |
| 硬件 | 麦克风（说话）+ 扬声器（播放） |
| 监听 | `127.0.0.1:9002`（仅本机；跨机访问见下方"改配置"） |
| 路径 | `/api/V1/open-portal/app/wss/agent-sdk` |
| 鉴权 | 宽松模式（不校验签名，示例项目可直接连） |

- 窗口可以失焦，但**不要最小化**（需保持后台运行，否则音频/网络停摆）
- **F1** 呼出/隐藏调试面板（默认隐藏，录屏时画面干净）
- 调试面板按钮可手动触发挥手/表情/行走（无 Agent 时验证动作用）
- 同一时间只允许一个 Agent 连接；Agent 退出后可立即重连

## 目录里应有什么

本目录存放 Unity Build 产物（exe + Data 文件夹一起拷入）。

## 重新打包（可选）

1. Unity 打开 `../../Unity-RL-Playground` 工程（Unity 2021.3 LTS）
2. 场景：`Assets/X02Competition/Scenes/scene.unity`（含网关装配 + 调试面板）
3. `File → Build Settings → Windows (x86_64)`
4. Player Settings 勾选 `Run In Background`（必须）
5. Build 输出整个文件夹放入本目录

## 改配置（可选）

需要严格鉴权 / 跨机监听时，改 `Assets/X02Competition/Bootstrap/CompetitionLauncher.cs` 后重新 Build：

- 跨机监听：监听地址改 `IPAddress.Any`
- 严格鉴权：`StrictAuth = true`，凭证 appId=`demo-app` / appKey=`demo-key` / appSecret=`demo-secret`

# 机器人侧程序（exe/）

双击运行即启动机器人网关：机器人出现在窗口中，监听 `127.0.0.1:9002` 等 Agent 接入。

## 运行须知

- Windows 10/11 x64，麦克风 + 扬声器
- 路径 `/api/V1/open-portal/app/wss/agent-sdk`，鉴权为宽松模式（示例客户端可直接连）
- 单 Agent 连接；Agent 退出后可立即重连
- 窗口可以失焦，但**不要最小化**（需保持后台运行）
- **F1** 呼出/隐藏调试面板：连接状态、ASR/LLM 字幕、技能记录，可手动触发全部技能

## 重新打包

1. Unity 2022.3.62 打开 `../unity-agent-playground` 工程
2. 场景：`Assets/X02Competition/Scenes/scene.unity`
3. `File → Build Settings → Windows (x86_64)`
4. Player Settings 勾选 `Run In Background`（必须）
5. Build 输出整个文件夹放入本目录

## 修改配置

跨机监听 / 严格鉴权：改 `Assets/X02Competition/Bootstrap/CompetitionLauncher.cs` 后重新 Build，详见 [../unity-agent-playground/README.md](../unity-agent-playground/README.md)。

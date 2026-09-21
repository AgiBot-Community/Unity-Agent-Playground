# Unity 工程

**中文** | [English](unity.en.md) | [Français](unity.fr.md)

[unity-agent-playground/](../unity-agent-playground/) 提供 X2 机器人模型、Agent 网关、动作、表情和步态源码。工程记录的编辑器版本是 **2022.3.62f3c1**，URP、Sentis、ML-Agents、URDF Importer 等依赖以工程配置和随附本地包为准。

## 打开与运行

Unity Hub → Open → 选择 `unity-agent-playground/`，等待首次导入和编译完成，再打开 `Assets/X02Competition/Scenes/scene.unity` 并点击 Play。按 **F1** 显示调试面板。Play Mode 使用当前源码；仓库便携 EXE 来自先前构建，技能支持可能不同。

## 代码位置

| 路径（相对 Unity 工程） | 内容 |
|---|---|
| `Assets/X02Competition/Bootstrap/` | 启动器和调试面板 |
| `Assets/X02Competition/Gateway/` | WebSocket 服务与会话管理 |
| `Assets/X02Competition/Protocol/` | 消息协议 |
| `Assets/X02Competition/Robot/Skills/` | 手势、表情、步态、技能路由 |
| `Assets/X02Competition/Scenes/` | 场景和技能配置 |
| `Assets/RobotModel/` | URDF、网格和 ML 策略 |
| `Assets/SceneEnvironment/` | 场景环境 |

## 配置与构建

网关配置见 `Assets/X02Competition/Bootstrap/CompetitionLauncher.cs`。默认监听 `127.0.0.1:9002`；跨机监听与 `StrictAuth` 修改需重新构建。技能清单见场景的 `SkillCatalog.asset`，代码默认值见 `Robot/Skills/SkillCatalog.cs`。

在 `File → Build Settings` 选择 Windows x86_64，Player Settings 启用 `Run In Background`，将完整构建输出到仓库 `exe/`。完整 Unity Build 需要保留所有输出文件；单文件 EXE 的重新封装步骤见[模拟器说明](simulator.md)。

Agent 接入见[示例说明](../example/README.md)，自定义 Agent 见[网关协议](interface.md)。

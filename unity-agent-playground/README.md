# Unity Agent Playground

X02 人形机器人的 Unity 虚拟环境：机器人模型、Agent 网关、动作/表情/步态技能，全部源码。

## 环境要求

- Unity **2022.3.62f1**（含 URP、Sentis、ML-Agents、URDF Importer，依赖以本地包形式随工程提供）

## 打开工程

Unity Hub → Open → 选择本目录。首次打开等待编译完成，然后打开场景：

```
Assets/X02Competition/Scenes/scene.unity
```

点击 Play 即可运行（等价于 `exe/` 中的 Build 产物）。

## 目录结构

```
Assets/
├── X02Competition/            # 核心代码
│   ├── Bootstrap/             # 启动器、调试面板（F1）
│   ├── Gateway/               # Agent 网关：WebSocket 服务、会话管理
│   ├── Protocol/               # 消息协议定义
│   ├── Robot/                 # 机器人运行时
│   │   └── Skills/            # 技能：手势、表情、步态、技能路由
│   └── Scenes/                # 场景与技能配置（SkillCatalog.asset）
├── RobotModel/                # 机器人模型（URDF/网格/ML 策略）
└── SceneEnvironment/          # 场景环境
```

## 关键配置

网关行为在 `Assets/X02Competition/Bootstrap/CompetitionLauncher.cs` 中配置：

| 项 | 默认 | 说明 |
|---|---|---|
| 监听地址 | `127.0.0.1:9002` | 跨机访问改 `IPAddress.Any` |
| 鉴权 | 宽松模式 | 严格模式设 `StrictAuth = true`（凭证见代码） |

技能清单在场景的 `SkillCatalog.asset` 中配置，代码默认值见 `Skills/SkillCatalog.cs`。

## 打包

`File → Build Settings → Windows (x86_64)`，Player Settings 勾选 `Run In Background`，产物放入 `../exe/`。详见 [../exe/README.md](../exe/README.md)。

## 更多

- Agent 接入：[../python-agent-client/README.md](../python-agent-client/README.md)
- WebSocket 协议：[../docs/interface.md](../docs/interface.md)

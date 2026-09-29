# 文档 / Documentation

项目首页 / Project overview / Présentation : [中文](../README.md) · [English](en/README.md) · [Français](fr/README.md)

| 内容 / Topic / Sujet | 中文 | English | Français |
|---|---|---|---|
| 模拟器 / Simulator / Simulateur | [运行与快捷键](zh-CN/simulator.md) | [Running the simulator](en/simulator.md) | [Exécuter le simulateur](fr/simulator.md) |
| Unity 工程 / Unity project / Projet Unity | [安装与构建](zh-CN/unity.md) | [Installation and builds](en/unity.md) | [Installation et compilation](fr/unity.md) |
| Agent | [语音配置与排障](../example/x2_agent/docs/zh-CN/README.md) | [Voice setup and troubleshooting](../example/x2_agent/docs/en/README.md) | [Configuration vocale et dépannage](../example/x2_agent/docs/fr/README.md) |
| 控制台 / Console | [操作与会话管理](zh-CN/console.md) | [Controls and sessions](en/console.md) | [Commandes et sessions](fr/console.md) |
| 网关 / Gateway / Passerelle | [协议参考](zh-CN/interface.md) | [Protocol reference](en/interface.md) | [Référence du protocole](fr/interface.md) |
| 开发 / Development / Développement | [测试与发布](zh-CN/development.md) | [Tests and releases](en/development.md) | [Tests et publications](fr/development.md) |
| 贡献 / Contributing / Contribution | [贡献指南](../CONTRIBUTING.md) | [Contributing guide](en/CONTRIBUTING.md) | [Guide de contribution](fr/CONTRIBUTING.md) |

## 阅读顺序 / Reading order / Ordre de lecture

- **中文**：首次运行从项目首页开始，按用途选择模拟器或 Unity 工程指南；启动机器人后阅读 Agent 指南。自定义客户端参考网关协议，修改与测试参考开发和贡献指南。
- **English**: Start with the project overview, then choose the simulator or Unity guide. Once the robot is running, configure the agent. Use the protocol reference for custom clients and the development and contributing guides for changes and tests.
- **Français** : Commencez par la présentation, puis le guide du simulateur ou de Unity. Configurez l’agent une fois le robot lancé. Consultez le protocole pour un client personnalisé, et les guides de développement et de contribution pour les modifications et tests.

## 目录 / Layout / Organisation

```text
docs/
├── README.md           # 文档导航 / Documentation index
├── zh-CN/              # 中文指南
├── en/                 # English guides and project README
└── fr/                 # Guides français et README du projet
```

中文项目首页和贡献指南位于仓库根目录。英文、法文译本分别位于 `en/` 和 `fr/`。同一主题在各语言目录中使用相同文件名。Agent 的三语指南与示例一同维护在 `example/x2_agent/docs/`。

The Chinese project README and contributing guide live at the repository root. English and French translations live in `en/` and `fr/`. Topic filenames match across languages. Agent guides are maintained with the example in `example/x2_agent/docs/`.

Le README du projet et le guide de contribution chinois restent à la racine du dépôt. Les traductions sont dans `en/` et `fr/`, avec les mêmes noms de fichiers par sujet. Les guides de l’agent sont dans `example/x2_agent/docs/`.

## 组件说明 / Component READMEs / README des composants

- [Unity 工程 / Unity project / Projet Unity](../unity-agent-playground/README.md) — 中文 / English / Français
- [Agent](../example/x2_agent/README.md) — 三语入口 / Language links / Liens par langue
- [控制台 / Console](../example/x2_console/README.md) — 中文；三语使用指南见上表 / Translated guides above / Guides traduits ci-dessus
- [Windows 打包工具 / Windows packager](../tools/unity-packager/README.md) — English

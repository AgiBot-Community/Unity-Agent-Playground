# 贡献指南

**中文** | [English](docs/en/CONTRIBUTING.md) | [Français](docs/fr/CONTRIBUTING.md)

代码修复、功能改进、文档和翻译均可通过 Pull Request 提交。安装与首次运行见 [README](README.md)，模块说明和完整测试命令见[开发指南](docs/zh-CN/development.md)。

## 报告问题

提交 [Issue](https://github.com/AgiBot-Community/Unity-Agent-Playground/issues) 前，先搜索是否已有相同问题。报告中请包含：

- 使用的 Release 版本或提交、操作系统、Python / Unity 版本。
- 最少复现步骤，以及预期结果和实际结果。
- 相关日志；界面或动作问题可附截图或短视频。
- 涉及语音服务时，注明模型、音色和资源 ID，并删除日志中的 API Key、签名和私人对话。

功能建议请说明使用场景、现有行为的限制和期望结果。涉及协议、依赖或目录结构的大改动，建议先开 Issue 讨论范围。

## 提交修改

1. Fork 仓库，从目标分支创建工作分支。
2. 保持一次 PR 聚焦一个问题，并补充相应测试或文档。
3. 运行受影响组件的检查，在 PR 中列出结果；未运行的 Unity 或云端测试也请注明。
4. 提交 PR，说明解决的问题、修改后的行为和复现或验证方法；有关联 Issue 时附上链接。

Python 客户端的测试使用本机模拟服务，无需 Unity 或云端密钥。在已安装对应依赖的环境中，从仓库根目录执行：

```powershell
python -B -m unittest discover -s example/x2_agent/tests -v
python -B -m unittest discover -s example/x2_console/tests -v
```

修改网关协议时检查 Agent 和控制台两端；修改 Unity 技能、音频或会话行为时，运行[开发指南](docs/zh-CN/development.md)中对应的 Unity 验证。

## 文档与翻译

首页介绍项目并提供最短启动步骤，使用指南说明操作，协议文档定义消息与行为，开发指南记录测试和发布流程。新增内容放入对应页面，并在[文档索引](docs/README.md)添加入口。

通用指南按语言放在 `docs/zh-CN/`、`docs/en/`、`docs/fr/`，同一主题使用相同文件名。中文项目 README 和 CONTRIBUTING 保留在仓库根目录，译文位于对应语言目录。Agent 指南按同样的语言目录结构放在 `example/x2_agent/docs/`。修改功能说明或参数时同步更新对应译文，保留命令、协议字段、模型 ID 和文件名。

使用具体的步骤、默认值和结果描述功能。个人工作记录、临时验证过程和机器专用路径不属于使用文档。提交前检查相对链接、标题锚点和命令的工作目录。

## 文件与许可证

保留 Unity 资源的 `.meta` 文件。`.env`、密钥、日志、缓存和构建产物不提交；忽略规则见 [`.gitignore`](.gitignore)。便携 EXE 与校验文件通过 GitHub Releases 发布，流程见[开发指南](docs/zh-CN/development.md)。

项目原创代码采用 [Apache License 2.0](LICENSE)。引入第三方代码或资源时保留其许可证与来源声明。

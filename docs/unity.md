# Unity 工程

**中文** | [English](unity.en.md) | [Français](unity.fr.md)

本页是“从 Unity 项目开始”的完整路径。只想直接体验时，可改用 [从 EXE 开始](simulator.md)；两条路径随后连接相同的 [Agent 示例](../example/README.md)。

[unity-agent-playground/](../unity-agent-playground/) 提供 X2 机器人模型、Agent 网关、动作、表情和步态源码。工程记录的编辑器版本是 **2022.3.62f3c1**，URP、Sentis、ML-Agents、URDF Importer 等依赖以工程配置和随附本地包为准。

## 安装 Unity Hub 和编辑器

仅运行仓库提供的 `exe/x2模拟器.exe` 不需要安装 Unity Hub 或 Unity Editor。需要打开场景、修改源码或重新构建时，按以下步骤安装：

1. 从 [Unity 官方下载页](https://unity.com/download) 下载适合操作系统的 Unity Hub，运行安装程序并启动 Hub。按提示登录 Unity 账号，在 Settings → Licenses 中激活适用的许可证；符合条件的个人用户可选择 Unity Personal。
2. 本工程使用 **2022.3.62f3c1**，完整版本记录在 `ProjectSettings/ProjectVersion.txt`。在 Hub 的 Installs → Install Editor 中查找该版本；若列表中没有，从 [Unity 中国版本发布页](https://unity.cn/releases) 查找对应版本。带 `c1` 后缀的版本可能不在[全球编辑器归档](https://unity.com/releases/editor/archive)中，不要直接改用最新 Unity 6 或修改版本文件来跳过检查。
3. 如果通过独立安装程序安装了 Editor，在 Hub 的 Installs → Locate 中定位该编辑器（Windows 下为安装目录中的 `Editor/Unity.exe`）。Hub 和 Editor 是两个独立程序，安装 Hub 本身不会自动安装所需编辑器。
4. 在 Windows 上运行场景和构建 Windows Mono 程序可使用编辑器随附的支持；构建 IL2CPP 时另装对应 Windows Build Support 和 C++ 工具。需要构建 Linux 时，通过编辑器的 Add modules 安装与脚本后端对应的 **Linux Build Support**。仓库保留 Linux 工具链依赖，但它不能替代 Editor 的 Linux 构建模块。

## 添加独立工程

该目录可整体复制为独立工程，打开时不依赖外层仓库。ML-Agents 和 URDF Importer 两个随附源码包已作为嵌入式包放入 `Packages/`；首次导入仍需联网获取注册表依赖。详见工程内的[独立打开说明](../unity-agent-playground/README.md)。

下载仓库后先完整解压。目录结构应为：

```text
Unity-Agent-Playground/          仓库目录
└── unity-agent-playground/      在 Hub 中添加的独立 Unity 工程
    ├── Assets/
    ├── Packages/
    └── ProjectSettings/
        └── ProjectVersion.txt
```

1. 在 Hub 的 **Projects → Add → Add project from disk（添加磁盘上的现有项目）** 中，选择内层 `unity-agent-playground/`；部分 Hub 版本将该入口称为 Open。若已把工程复制到其他位置，选择同时包含上述三个目录的文件夹。
2. 确认项目使用 `2022.3.62f3c1`，点击项目打开。首次打开会解析依赖并导入资源，请等待编译完成。复制工程必须保留 `Assets/` 中的 `.meta` 文件以及完整的 `Packages/`、`ProjectSettings/`。

**遇到 “No projects found. Select a folder that contains Unity projects.”：** 这是 Hub 的批量 **Import projects** 扫描入口，它检查所选目录下的子目录。请改用上面的 Add project from disk；如果继续使用批量扫描，则选择 `unity-agent-playground/` 的父目录。扫描父目录并不意味着父目录是 Unity 工程。

## 运行场景与连接 Agent

1. 在 Editor 的 Project 面板打开 `Assets/X02Competition/Scenes/scene.unity`。如果有红色错误，先打开 Window → General → Console 查看并处理，再进入 Play。
2. 点击工具栏 **Play**，切换到 Game 窗口，按 **F1** 显示调试面板。网关默认监听 `127.0.0.1:9002`。运行 Editor 场景前关闭便携 EXE，避免两个模拟器争用端口。
3. 如需验证 Agent 连接，在仓库根目录打开另一个终端，运行：

```powershell
python -m pip install -r example/requirements.txt
python example/demo.py
```

demo 不需要云服务密钥。真实语音对话的配置见[示例说明](../example/README.md)；配置完成后，先按 Ctrl+C 退出 demo，再运行 `python example/agent.py`。Python 使用直接启动脚本的方式，无需安装本仓库为 Python 包。单独复制 Unity 工程的用户需另外准备兼容的 Agent 客户端。

看到 Agent 终端中的 `state=online` 表示连接成功。再次点击 Play 可停止场景，Ctrl+C 停止 Agent。源码修改不会自动更新仓库便携 EXE。

**C** 轮换观察视角；**F2** 全景、**F3** 正面跟随、**F4** 侧面跟随、**F5** 自由环绕。自由环绕支持在场景内按住鼠标右键旋转、滚轮调节距离。所有视角自动保留机器人全身；也可在 F1 面板点击视角按钮。

## 常见问题

| 现象 | 处理方式 |
|---|---|
| Hub 提示找不到项目 | 使用 Add project from disk 选择内层工程；批量 Import projects 则选择工程父目录。 |
| Hub 提示缺少编辑器 | 安装完整版本 `2022.3.62f3c1`；已有安装时使用 Installs → Locate。 |
| 首次导入卡在依赖解析或出现包错误 | 检查网络及 Console、Window → Package Manager 的具体错误；保留 `Packages/` 中的清单和嵌入式包。 |
| 打开后没有机器人 | 打开指定的 `scene.unity`，进入 Play 并查看 Game 窗口。 |
| 网关启动失败或端口 9002 被占用 | 关闭另一个 EXE 或 Editor 中正在运行的场景，再启动；同一时间只运行一个模拟器、连接一个 Agent。 |
| Agent 连接失败 | 确认场景正在 Play、网关已启动，再按示例说明检查连接地址；跨机连接不能使用对方的 `127.0.0.1`。 |

## 代码位置

| 路径（相对 Unity 工程） | 内容 |
|---|---|
| `Assets/X02Competition/Bootstrap/` | 启动器、调试面板和 `RobotCameraRig.cs` 多视角取景 |
| `Assets/X02Competition/Gateway/` | WebSocket 服务与会话管理 |
| `Assets/X02Competition/Protocol/` | 消息协议 |
| `Assets/X02Competition/Robot/Skills/` | 手势、表情、步态、技能路由 |
| `Assets/X02Competition/Scenes/` | 场景和技能配置 |
| `Assets/RobotModel/` | URDF、网格和 ML 策略 |
| `Assets/SceneEnvironment/` | 场景环境 |

## 配置与构建

端口与 `StrictAuth` 在场景的 `CompetitionLauncher` 组件中配置，实现位于 `Assets/X02Competition/Bootstrap/CompetitionLauncher.cs`。监听地址在 `Assets/X02Competition/Gateway/LinkskyGatewayServer.cs` 中，默认为本机回环地址。技能清单见场景的 `SkillCatalog.asset`，代码默认值见 `Assets/X02Competition/Robot/Skills/SkillCatalog.cs`。改动后需重新构建发布程序。

停止 Play 后，在 `File → Build Settings` 中用 Add Open Scenes 添加主场景并确保勾选，移除不需要随包启动的场景。选择 PC, Mac & Linux Standalone → Windows → x86_64，必要时点击 Switch Platform。在 Player Settings 中启用 `Run In Background`，点击 Build，选择 `Assets/` 之外的专用空输出目录。

构建完成后运行输出目录内的 EXE，使用上述 Agent 命令验证连接。运行和分发时保留全部输出文件；Unity 的普通 Build 不会自动生成或替换仓库的单文件 [EXE](../exe/x2模拟器.exe)。Linux 构建需先安装上述模块，再切换目标平台并单独输出。

Agent 接入见[示例说明](../example/README.md)，自定义 Agent 见[网关协议](interface.md)。

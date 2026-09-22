# x2模拟器（单文件便携版）

**中文** | [English](simulator.en.md) | [Français](simulator.fr.md)

本指南适用于直接运行 Windows 便携模拟器的用户。双击 [x2模拟器.exe](../exe/x2模拟器.exe) 后，窗口显示机器人，网关在 `127.0.0.1:9002` 等待连接。按 **F1** 打开调试面板即可手动测试技能；语音对话需要另行启动[示例 Agent](../example/README.md)，连接成功后由 Agent 发送开场白。

文件大小为 **58,287,104 字节（58.29 MB）**，可以只分发这一个 EXE，无需附带 Data 文件夹、Playground 或 Python。需要语音对话时，Python Agent 仍需单独启动；此 EXE 不包含其 API Key 或配置。校验文件见 [SHA-256](../exe/x2模拟器.sha256)。

## 运行须知

| 项 | 值 |
|---|---|
| 系统 | Windows 10/11 x64，系统提供 .NET Framework 4.x |
| 语音交互设备 | 麦克风（输入）和扬声器（播放）；仅测试技能按钮时无需麦克风 |
| 监听 | `127.0.0.1:9002`（仅本机；修改方式见 Unity 工程指南） |
| 路径 | `/api/V1/open-portal/app/wss/agent-sdk` |
| 鉴权 | 示例客户端发送 HMAC 签名；网关是否强制校验由其构建配置决定 |
| 会话 | 同一时间连接一个 Agent |

- 测试语音时保持窗口打开；若最小化后出现音频或网络异常，先恢复窗口再排查
- **F1** 呼出/隐藏调试面板（默认隐藏，录屏时画面干净）
- 调试面板按钮可手动触发挥手/表情/行走（无 Agent 时验证动作用）
- 同一时间只允许一个 Agent 连接；Agent 退出后可立即重连

## 从 EXE 开始

1. 获取仓库的 [EXE](../exe/x2模拟器.exe) 和 [校验文件](../exe/x2模拟器.sha256)。仅运行模拟器无需安装 Unity 或 Python。
2. 可在仓库根目录运行 `Get-FileHash -LiteralPath 'exe/x2模拟器.exe' -Algorithm SHA256`，与校验文件中的值比较。
3. 双击 EXE，等待机器人窗口出现；按 **F1** 查看状态或手动测试技能。
4. 如需连接示例 Agent，在仓库根目录执行：

```powershell
python -m pip install -r example/requirements.txt
python example/demo.py
```

出现 `state=online` 表示连接成功。demo 使用固定文字和随附音频，不调用云服务。真实对话请按 [Agent 指南](../example/README.md) 使用 `.env.example` 配置凭据，退出 demo 后运行 `python example/agent.py`。关闭窗口退出模拟器；Ctrl+C 停止 Agent。

## 多视角观察

按 **C** 轮换视角，也可使用调试面板中的视角按钮：

| 快捷键 | 视角 | 行为 |
|---|---|---|
| F2 | 全景观察 | 同时保留起点与当前位置，行走时自动拉远 |
| F3 | 正面跟随（默认） | 保留小范围自由移动后平滑跟随，固定世界方向以观察转身 |
| F4 | 侧面跟随 | 从侧面观察步态、前进距离与转向 |
| F5 | 自由环绕 | 在场景内按住鼠标右键旋转，滚轮调节观察距离 |

各视角按机器人全身边界自动取景，并为左侧 HUD 留出空间。自由环绕的最近距离也受全身取景限制。跟随视角保留地面参照和短距离移动，便于看出机器人正在走动；全景视角可直接对比起点和当前位置。小窗口下可按 F1 收起详细面板以扩大观察区域。

## 从 Unity 项目开始

需要修改场景、相机、技能或网关时，使用仓库的 [Unity 工程](../unity-agent-playground/)：

1. 按 [Unity 指南](unity.md) 下载并安装 Unity Hub 和旧版编辑器 **2022.3.62f3c1**，通过 **Add project from disk** 添加 `unity-agent-playground/`。
2. 导入完成后打开 `Assets/X02Competition/Scenes/scene.unity`，点击 **Play** 并聚焦 Game 窗口。
3. 使用上面相同的快捷键与 Agent 命令。进入 Play 前关闭 EXE，避免争用 9002 端口。
4. 修改后按 [Unity 构建步骤](unity.md) 使用 **File → Build Settings** 生成 Windows 程序，并保留完整输出目录。

仓库中的单文件 EXE 可直接运行和分发。自行使用 Unity Build 构建时，输出的是包含程序和资源的目录，分发时应保留整个目录；该操作不会自动替换仓库 EXE。

# X2 模拟器

**中文** | [English](../en/simulator.md) | [Français](../fr/simulator.md)

Windows 便携模拟器以单个 EXE 分发。启动后显示机器人，网关在 `127.0.0.1:9002` 等待连接。按 **F1** 打开调试面板可手动测试技能；语音对话需要单独运行[示例 Agent](../../example/x2_agent/docs/zh-CN/README.md)。

首次启动会将 Unity 资源解压到 `%LOCALAPPDATA%\UnityPortable`，后续启动复用缓存。运行模拟器无需安装 Unity 或 Python；Agent 的依赖与凭据单独配置。

## 运行须知

| 项 | 值 |
|---|---|
| 系统 | Windows 10/11 x64，系统提供 .NET Framework 4.x |
| 语音交互设备 | 麦克风（输入）和扬声器（播放）；仅测试技能按钮时无需麦克风 |
| 监听 | `127.0.0.1:9002`（仅本机；修改方式见 Unity 工程指南） |
| 路径 | `/api/V1/open-portal/app/wss/agent-sdk` |
| 鉴权 | 示例客户端发送 HMAC 签名；网关是否强制校验由其构建配置决定 |
| 会话 | 默认 8 路；控制台固定最高优先级，其它优先级可在控制台调节 |

- 测试语音时保持窗口打开；若最小化后出现音频或网络异常，先恢复窗口再排查
- **F1** 显示或隐藏调试面板，默认隐藏
- 调试面板按钮可手动触发挥手/表情/行走（无 Agent 时验证动作用）
- 支持多路连接；控制权与语音接收权分别选举，断线后自动交接

## 从 EXE 开始

1. 从 [GitHub Releases](https://github.com/AgiBot-Community/Unity-Agent-Playground/releases) 下载同版本的便携 EXE 和校验文件。源码仓库不包含二进制文件。仅运行模拟器无需安装 Unity 或 Python。
2. 若使用控制台启动按钮，将 EXE 保存到仓库本地 `exe/x2模拟器.exe`。在仓库根目录运行 `Get-FileHash -LiteralPath 'exe/x2模拟器.exe' -Algorithm SHA256`，与下载的校验文件比较；也可以在其他目录直接运行便携 EXE。
3. 双击 EXE，等待机器人窗口出现；按 **F1** 查看状态或手动测试技能。
4. 如需连接示例 Agent，在仓库根目录执行：

```powershell
python -m pip install -r example/x2_agent/requirements.txt
python example/x2_agent/demo.py
```

出现 `state=online` 表示连接成功。demo 使用固定文字和随附音频，不调用云服务。真实对话请按 [Agent 指南](../../example/x2_agent/docs/zh-CN/README.md) 使用 `.env.example` 配置凭据，退出 demo 后运行 `python example/x2_agent/agent.py`。关闭窗口退出模拟器；Ctrl+C 停止 Agent。

## 多视角观察

按 **C** 轮换视角，也可使用调试面板中的视角按钮：

| 快捷键 | 视角 | 行为 |
|---|---|---|
| F2 | 全景观察 | 同时保留起点与当前位置，行走时自动拉远 |
| F3 | 正面跟随（默认） | 保留小范围自由移动后平滑跟随，固定世界方向以观察转身 |
| F4 | 侧面跟随 | 从侧面观察步态、前进距离与转向 |
| F5 | 自由环绕 | 在场景内按住鼠标右键旋转，滚轮调节观察距离 |

各视角按机器人全身边界自动取景，并为左侧 HUD 留出空间。自由环绕的最近距离也受全身取景限制。跟随视角保留地面参照和短距离移动，便于看出机器人正在走动；全景视角可直接对比起点和当前位置。小窗口下可按 F1 收起详细面板以扩大观察区域。

## 修改与构建

修改场景、相机、技能或网关见 [Unity 工程指南](unity.md)。Editor Play 模式使用相同的快捷键和 Agent 命令，运行前应关闭便携 EXE，避免争用端口。

Unity Build 输出包含程序和资源的完整目录。使用[便携打包工具](../../tools/unity-packager/README.md)可将该目录打包成单个 EXE。

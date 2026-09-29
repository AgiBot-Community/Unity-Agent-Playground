# X2 图形控制台

独立的 Tkinter 管理控制台，提供连接管理、日志、状态、会话优先级和手动控制。
语音对话由 `example/x2_agent` 处理。控制台使用 Python 3.10+、Tkinter 和 `websockets`，网关凭据在界面中设置。

使用指南：[中文](../../docs/zh-CN/console.md) | [English](../../docs/en/console.md) | [Français](../../docs/fr/console.md)

## 启动

从仓库根目录启动：

```powershell
python -m pip install -r example/x2_console/requirements.txt
python example/x2_console/main.py
```

在本目录中可直接运行 `python main.py`，也支持 `python -m x2_console`。

连接后默认监控，不占用 Agent 的动作或麦克风。控制台保留 1000 优先级和管理权限；
需要手动操作时，在“会话 / 优先级”勾选“控制台接管动作”。“停止”会先收回控制再停止。

## 目录结构

```text
x2_console/
├── main.py                 # 启动入口
├── requirements.txt        # 本项目的依赖
├── x2_console/
│   ├── __init__.py         # 项目与可选仓库资源路径
│   ├── __main__.py         # python -m x2_console
│   ├── client.py           # 连接、管理指令与状态接收
│   ├── ui.py               # Tkinter 界面
│   ├── model.py            # 日志缓存、筛选与导出
│   ├── theme.py            # Catppuccin Mocha 语义颜色
│   └── gateway.py          # 管理与监控协议
└── tests/                  # 控制台回归测试
```

## 独立运行

可以单独复制本目录运行，无需相邻的 `x2_agent` 项目或云服务依赖。
网关地址和签名凭据在界面中设置，仅在本次运行中使用。
先从 [GitHub Releases](https://github.com/AgiBot-Community/Unity-Agent-Playground/releases)
下载便携 EXE，保存为仓库根目录下的 `exe/x2模拟器.exe`，再使用“启动模拟器”按钮。
`exe/` 是本地下载目录，不随源码仓库分发；
单独分发控制台时，请另行启动 Unity 模拟器，再填写网关地址连接。

## 测试

从仓库根目录运行：

```powershell
python -B -m unittest discover -s example/x2_console/tests -v
```

使用和优先级规则见[控制台指南](../../docs/zh-CN/console.md)。

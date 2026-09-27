# X2 图形控制台

这是独立的管理与监控控制台，提供连接管理、日志、状态、优先级和手动操作。
语音对话由独立的 `example/x2_agent` 负责，控制台只依赖 WebSocket，不读取豆包密钥或 `.env`。

从仓库根目录启动：

```powershell
python -m pip install -r example/x2_console/requirements.txt
python example/x2_console/main.py
```

在本目录中可直接运行 `python main.py`，也支持 `python -m x2_console`。

连接后默认监控，不占用 Agent 的动作或麦克风。控制台保留 1000 优先级和管理权限；
需要手动操作时，在“会话 / 优先级”勾选“控制台接管动作”。“停止”会先收回控制再停止。

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

可以单独复制本目录运行，无需相邻的 `x2_agent` 项目或云服务依赖。
网关地址和签名凭据在界面中设置，仅在本次运行中使用。
在仓库中，“启动模拟器”按钮定位仓库根目录的 `exe/`；
单独分发控制台时，请另行启动 Unity 模拟器，再填写网关地址连接。

控制台测试（从仓库根目录）：

```powershell
python -B -m unittest discover -s example/x2_console/tests -v
```

使用和优先级规则见[控制台指南](../../docs/console.md)。

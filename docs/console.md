# Python 图形控制台

**中文** | [English](console.en.md) | [Français](console.fr.md)

从仓库根目录运行：

```powershell
python -m pip install -r example/x2_console/requirements.txt
python example/x2_console/main.py
```

也可进入 `example/x2_console/` 运行 `python main.py` 或 `python -m x2_console`。
控制台的入口、实现和测试集中在该目录，目录说明见[控制台 README](../example/x2_console/README.md)。
该目录可独立复制运行，只包含管理与监控功能，不导入相邻的 Agent 项目。
唯一第三方依赖是 WebSocket；语音服务和密钥配置属于 `example/x2_agent`。
界面使用 Python 自带的 Tkinter，
不需要安装 Qt 或前端工具链。若 Python 发行版未包含 Tk，先安装对应的 Tk 支持。

1. 点击“启动 Unity 模拟器”，或手动运行 Unity 工程的主场景。
2. 保持默认 `127.0.0.1:9002`，点击“连接网关”。签名凭据和路径在“高级”中设置。
3. 连接后默认监控。需要手动操作时，在“会话 / 优先级”中勾选接管，再到“机器人控制”操作手势、行走、转向或表情。
4. “会话 / 优先级”页签显示所有连接、控制权和语音接收权。选择非控制台连接，
   输入 0–999 的整数并点击“应用”；确认服务器返回的新数值。控制台固定为 1000，不可修改。
5. 使用独立 `x2_agent` 做语音动作时，保持“控制台接管动作”未勾选。
   控制台保留日志查看和优先级管理，Agent 获得动作控制权。

默认支持 8 路客户端。控制台持有最高控制优先级时，低优先级 Agent 的动作请求会被拒绝，
但仍可进行语音对话和接收日志。多个控制台同级时，先连接者持有控制权。
会话优先级在重连后恢复默认值。完整规则见[协议说明](interface.md#25-多连接控制权和语音接收权)。

“接管动作”默认关闭，管理优先级仍固定为 1000；连接监控窗口不会抢占正在执行动作的 Agent。
需要重新手动操作时勾选接管；管理控制台的“停止动作与播报”按钮会先收回控制权再停止。
旧版模拟器不支持此开关时，界面会提示更新，请重启仓库中更新后的 EXE。
若出现“说了挥手但没有动作”，先检查会话列表中的动作归属和日志里的 `4091`，
而不要将正常的权限拒绝误判为手势缺失。

控制台只建立一条管理 WebSocket，握手明确关闭音频接收。
它不读取 `.env`，不包含 ASR、LLM、TTS、开场白或内置录音。
关闭窗口会断开连接并停止通信线程。键盘快捷键：`Ctrl+.` 停止，`Ctrl+L` 搜索日志。

日志支持 Info / Warning / Error、来源筛选、正文和堆栈搜索、暂停显示、清空和 JSONL 导出。
导出遵循当前筛选；本地历史只保留最近 1200 条。暂停显示不停止接收。
状态监控区显示网关上报的电源、网络和最近技能状态。
UI 与网络使用不同线程，日志过载时保持有界缓存并显示丢弃提示。

配色参考 **Catppuccin Mocha** 开源色板（`catppuccin/palette`，MIT）：
深灰紫底色、Teal 主操作、Yellow 警告、Red 错误。原始参考地址见
[`theme.py`](../example/x2_console/x2_console/theme.py)。Tk 控件直接使用语义颜色，
不加载 CSS 或远程字体。界面保留文字级别、键盘焦点与高 DPI 缩放。

本机回归：

```powershell
python -B -m unittest discover -s example/x2_console/tests -v
```

测试使用本机模拟 WebSocket，不调用云服务。Unity 多连接与优先级验证包含在
`AuditRegressionVerification.Run` 中，运行方法见[开发文档](development.md)。

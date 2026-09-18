# X02 机器人语音 Agent 交付包

对机器人说话，它会听懂、思考、用语音回答；让它挥手、做表情、走两步，它也会照做。

```
你说"挥挥手" ─► 机器人麦克风 ─► [机器人侧网关] ─► [Agent：识别→思考→合成]
                                              │
机器人挥手+表情 ◄─ [机器人侧网关] ◄─ 语音+动作指令 ┘
```

## 目录结构

| 目录 | 内容 |
|---|---|
| `unity-agent-playground/` | Unity 工程源码（[说明](unity-agent-playground/README.md)） |
| `exe/` | 机器人侧程序，Build 产物（[说明](exe/README.md)） |
| `python-agent-client/` | Agent 客户端，Python（[说明](python-agent-client/README.md)） |
| `docs/interface.md` | WebSocket 协议（自己写 Agent 时看） |

## 快速开始

环境：机器人侧 Windows 10/11 x64（麦克风+扬声器）；Agent 侧 Python 3.10+。

1. **启动机器人**：运行 `exe/` 中的程序（F1 呼出调试面板）
2. **启动 Agent**：

   ```bash
   cd python-agent-client
   python -m venv .venv && .venv/bin/pip install -r requirements.txt

   .venv/bin/python agent_client_demo.py     # 无 Key 验证链路
   cp .env.example .env                     # 填火山引擎 API Key 后跑完整对话
   .venv/bin/python agent_client_doubao.py
   ```

3. **开始对话**：

| 你说 | 机器人做 |
|---|---|
| "挥挥手" / "张开双臂" | 对应手势 |
| "你开心吗" / "给我比个爱心" | 表情屏切换 |
| "往前走一米" / "向左转" / "停" | 步态前进 / 转向 / 停止 |
| 播报中开口 | 打断当前播报，转入聆听 |

## 故障排查

| 现象 | 参见 |
|---|---|
| Agent 连不上机器人 | [exe/README.md](exe/README.md) |
| 语音/云端报错 | [python-agent-client/README.md](python-agent-client/README.md) |
| 协议对接问题 | [docs/interface.md](docs/interface.md) |

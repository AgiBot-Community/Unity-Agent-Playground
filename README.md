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
| `unity-agent-playground/` | Unity 工程（2022.3.62），机器人模型与网关、动作、表情全部源码 |
| `exe/` | 机器人侧程序（Unity Build 产物，[运行与打包说明](exe/README.md)） |
| `sample-project/` | Agent 示例（Python，[使用说明](sample-project/README.md)） |
| `docs/interface.md` | WebSocket 协议文档（自己写 Agent 时看） |

## 环境要求

| 项 | 要求 |
|---|---|
| 机器人侧 | Windows 10/11 x64，麦克风 + 扬声器 |
| Agent 侧 | Python 3.10+（Linux/macOS/Windows 均可） |
| 云服务 | 完整对话需火山引擎 API Key（语音技术 + 火山方舟各一）；连通性验证无需 Key |
| 网络 | Agent 与机器人同机（默认 `127.0.0.1:9002`）或网络可达 |

## 快速开始

1. **启动机器人**：运行 `exe/` 中的程序，窗口出现机器人（调试面板按 **F1** 呼出，默认隐藏）

2. **启动 Agent**：

   ```bash
   cd sample-project
   python -m venv .venv && .venv/bin/pip install -r requirements.txt

   # 第一步：无 Key 验证链路（机器人回放内置录音）
   .venv/bin/python agent_client_demo.py

   # 第二步：配置 Key 跑完整对话
   cp .env.example .env    # 填入两个 API Key
   .venv/bin/python agent_client_doubao.py
   ```

3. **开始对话**：机器人播报开场白后，用下表语音指令体验。

## 语音指令

| 你说 | 机器人做 |
|---|---|
| "挥挥手" / "张开双臂" | 对应手势 |
| "你开心吗" / "给我比个爱心" / "我有点难过" | 表情屏切换（开心/难过/惊讶/生气/爱心） |
| "往前走一米" / "向左转" | 步态前进 / 原地转向 |
| "停" | 立即停止 |
| 播报中开口 | 打断当前播报，转入聆听 |

回答问题时边说边做动作，表情屏的嘴巴随语音张合。

## 验收清单

| # | 操作 | 预期 |
|---|---|---|
| 1 | Agent 连上机器人 | 立即播报开场白 |
| 2 | 说"你好" | ~2.5s 内开始语音应答 |
| 3 | 问长一点的问题 | 边想边说，不等全文生成 |
| 4 | 说"挥挥手" | 边说话边挥手，面板显示 `技能: gesture/wave_hands` |
| 5 | 说"往前走一米" | 走完站稳，面板出现 `walk → done` |
| 6 | 播放中说话 | 先说完再听（半双工），不会自我打断 |
| 7 | Ctrl+C 后重连 Agent | 自动重连并重新播报开场白 |
| 8 | 第二个客户端接入 | 被拒（503，单会话） |
| 9 | 按 F1 | 调试面板呼出/隐藏 |

## 故障排查

| 现象 | 参见 |
|---|---|
| Agent 连不上机器人 | [exe/README.md](exe/README.md) 运行须知 |
| 语音/云端报错 | [sample-project/README.md](sample-project/README.md) 排障表 |
| 协议对接问题 | [docs/interface.md](docs/interface.md) |

# 示例 Agent：让机器人开口说话、会做动作

本目录是一个能直接跑的 Agent 参考实现。跑起来之后，你可以对着机器人说话，它会用语音回答你；说"挥挥手""做个开心的表情""往前走一米"，机器人真的会做。

两个客户端，用途不同：

| 文件 | 什么时候用 |
|---|---|
| `agent_client_doubao.py` | **正式体验/演示**。对接豆包云端三件套（ASR + LLM + TTS），全链路流式，实测首包语音 ~2.3s |
| `agent_client_demo.py` | **先跑通网关**。不依赖任何云服务和 API Key，收到语音后回固定台词，用于确认网络/协议没问题 |

建议路径：先用 demo 跑通 → 再配 Key 切 doubao 全链路。

## 第一步：安装（一次性）

- Python 3.10+
- 火山引擎账号（仅 doubao 客户端需要），开通：
  - 语音技术（ASR + TTS）→ `DOUBAO_SPEECH_API_KEY`
  - 火山方舟（LLM）→ `ARK_API_KEY`

```bash
python -m venv .venv
.venv/bin/pip install -r requirements.txt
```

Windows 下用 `.venv\Scripts\` 代替 `.venv/bin/`。

## 第二步：连上机器人

1. **先启动机器人侧**：运行交付的 Unity exe（或 Unity Play Mode）
2. **再启动 Agent**：

```bash
# 先验证连通（无需 Key，机器人会回固定台词）
.venv/bin/python agent_client_demo.py

# 确认没问题后，配置 Key 跑全链路
cp .env.example .env    # 编辑 .env，填入两个 API Key
.venv/bin/python agent_client_doubao.py
```

看到 `agent 会话就绪 state=online` 即连接成功。对机器人说话试试。

机器人不在本机时：`--host 192.168.x.x --port 9002`。

## 第三步：体验技能

连上后机器人先播开场白"你好，我是灵犀，有什么可以帮您？"（doubao 客户端为真人语音；demo 客户端是一声提示音），随后就可以对机器人说：

| 你说 | 机器人做 |
|---|---|
| "挥挥手" / "鞠个躬" / "张开双臂" | 对应的 3 个基本动作之一 |
| "你开心吗" / "给我比个爱心" / "我有点难过" / "你生气啦" | 头部表情屏切换（共 5 种经典表情：开心/难过/惊讶/生气/爱心） |
| "往前走一米" / "向左转" | 步态前进 / 原地转向 |
| "停" | 立即停止 |

机器人回答时嘴巴随语音张合（口型）。

没有 Key 也能验证动作：按 **F1** 呼出调试面板（默认隐藏），上面的按钮可以直接触发 3 个动作、5 个表情、行走。

## 常用参数

```bash
# 自定义机器人人设
.venv/bin/python agent_client_doubao.py --system-prompt "你是导览机器人小X"

# 保存一轮音频（排查"听不清/说不清"用）
.venv/bin/python agent_client_doubao.py --save-audio reply.wav --save-input input.wav

# 开场白自定义 / 禁用
.venv/bin/python agent_client_doubao.py --greeting "大家好，我是导览机器人"
.venv/bin/python agent_client_doubao.py --greeting ""

# demo 客户端：自定义回环台词 / 主动下发技能 / 测打断
.venv/bin/python agent_client_demo.py --reply "你好，我是灵犀"
.venv/bin/python agent_client_demo.py --skill gesture/wave_hands
.venv/bin/python agent_client_demo.py --interrupt chat
```

## .env 变量

| 变量 | 必填 | 说明 |
|---|---|---|
| `DOUBAO_SPEECH_API_KEY` | 是 | 语音控制台 API Key（ASR + TTS 共用） |
| `ARK_API_KEY` | 是 | 方舟 API Key（LLM） |
| `DOUBAO_LLM_MODEL` | 否 | 默认 `doubao-seed-2-1-turbo-260628` |
| `DOUBAO_TTS_SPEAKER` | 否 | 默认 `zh_female_wanqudashu_moon_bigtts`（1.0 音色，勿混用 2.0 音色） |
| `DOUBAO_ASR_RESOURCE_ID` | 否 | 默认 `volc.bigasr.sauc.duration` |

## 出问题了？

| 现象 | 怎么办 |
|---|---|
| 连不上（Connection refused） | 机器人侧没启动，或不在本机 → 加 `--host` |
| 握手 401 | 签名错（严格模式下）；核对 appSecret 与签名串格式 |
| 握手 503 | 已有一个会话没退出（单会话限制）；关掉旧的客户端再连 |
| 没有识别结果 | 音频太短/太轻；确认麦克风没被系统静音 |
| LLM 404 | 模型 ID 不完整；须用带日期后缀的完整 ID（如 `doubao-seed-2-1-turbo-260628`）或接入点 `ep-xxx` |
| TTS 403 | 音色与资源不匹配；`seed-tts-1.0` 只能用 1.0 音色 |
| 想看机器人到底听到什么 | `--save-input input.wav` 保存上行音频 |

协议细节见 `../docs/interface.md`。

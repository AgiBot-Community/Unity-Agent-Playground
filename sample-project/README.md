# Agent 示例（sample-project/）

对接机器人网关的 Agent 参考实现，两个客户端按需选用：

| 文件 | 用途 |
|---|---|
| `agent_client_doubao.py` | 完整对话：豆包云端三件套（ASR + LLM + TTS）全链路流式，首包语音实测 ~2.3s |
| `agent_client_demo.py` | 连通性验证：不依赖云服务和 API Key，收到语音后回放内置录音 |

建议先用 demo 确认链路，再配 Key 切 doubao。

## 环境配置

**Python 3.10+**。安装依赖：

```bash
python -m venv .venv
.venv/bin/pip install -r requirements.txt
```

Windows 下将 `.venv/bin/` 换成 `.venv\Scripts\`。

**API Key**（仅 doubao 客户端需要）：[火山引擎控制台](https://console.volcengine.com/) 开通"语音技术"（ASR+TTS）和"火山方舟"（LLM），各取一个 Key：

```bash
cp .env.example .env    # 编辑 .env 填入
```

| 变量 | 必填 | 说明 |
|---|---|---|
| `DOUBAO_SPEECH_API_KEY` | 是 | 语音控制台 API Key（ASR + TTS 共用） |
| `ARK_API_KEY` | 是 | 方舟 API Key（LLM） |
| `DOUBAO_LLM_MODEL` | 否 | 默认 `doubao-seed-2-1-turbo-260628` |
| `DOUBAO_TTS_SPEAKER` | 否 | 默认 `zh_female_wanqudashu_moon_bigtts`（1.0 音色，勿混用 2.0） |
| `DOUBAO_ASR_RESOURCE_ID` | 否 | 默认 `volc.bigasr.sauc.duration` |

## 运行

先启动机器人侧（见 [../exe/README.md](../exe/README.md)），再启动 Agent：

```bash
.venv/bin/python agent_client_demo.py        # 无 Key 验证
.venv/bin/python agent_client_doubao.py      # 完整对话
```

看到 `agent 会话就绪 state=online` 即连接成功，机器人播报开场白后即可对话。语音指令见[根目录 README](../README.md)。

机器人不在本机时：`--host 192.168.x.x --port 9002`。

## 常用参数

```bash
# 自定义机器人人设
.venv/bin/python agent_client_doubao.py --system-prompt "你是导览机器人小X"

# 开场白自定义 / 禁用
.venv/bin/python agent_client_doubao.py --greeting "大家好，我是导览机器人"
.venv/bin/python agent_client_doubao.py --greeting ""

# 保存一轮音频（排查"听不清/说不清"）
.venv/bin/python agent_client_doubao.py --save-audio reply.wav --save-input input.wav

# demo 客户端：自定义回环台词 / 主动下发技能 / 测打断
.venv/bin/python agent_client_demo.py --reply "你好，我是灵犀"
.venv/bin/python agent_client_demo.py --skill gesture/wave_hands
.venv/bin/python agent_client_demo.py --interrupt chat
```

技能由 LLM function calling 自动触发（已注册为 `robot_skill` 工具），无需手动调用。

## 排障

| 现象 | 处理 |
|---|---|
| 连不上（Connection refused） | 机器人侧未启动，或不在本机 → 加 `--host` |
| 握手 401 | 签名错（严格模式下）；核对 appSecret 与签名串格式 |
| 握手 503 | 已有会话未退出（单会话限制）；关掉旧客户端再连 |
| 没有识别结果 | 音频太短/太轻；确认麦克风未被系统静音 |
| LLM 404 | 模型 ID 不完整；须用带日期后缀的完整 ID 或接入点 `ep-xxx` |
| TTS 403 | 音色与资源不匹配；`seed-tts-1.0` 只能用 1.0 音色 |
| 想看机器人听到了什么 | `--save-input input.wav` 保存上行音频 |

协议细节见 [../docs/interface.md](../docs/interface.md)。

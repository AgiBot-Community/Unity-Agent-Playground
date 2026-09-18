# Agent 客户端（python-agent-client/）

对接机器人网关的 Agent 参考实现：

| 文件 | 用途 |
|---|---|
| `agent_client_demo.py` | 连通性验证，无需云服务和 Key（机器人回放内置录音） |
| `agent_client_doubao.py` | 完整对话：豆包 ASR + LLM + TTS 全链路流式 |

## 环境配置

Python 3.10+：

```bash
python -m venv .venv
.venv/bin/pip install -r requirements.txt    # Windows: .venv\Scripts\
```

API Key（仅 doubao 需要）：[火山引擎控制台](https://console.volcengine.com/) 开通"语音技术"和"火山方舟"，然后：

```bash
cp .env.example .env    # 编辑 .env 填入
```

| 变量 | 必填 | 说明 |
|---|---|---|
| `DOUBAO_SPEECH_API_KEY` | 是 | 语音（ASR + TTS 共用） |
| `ARK_API_KEY` | 是 | 方舟 LLM |
| `DOUBAO_LLM_MODEL` | 否 | 默认 `doubao-seed-2-1-turbo-260628` |
| `DOUBAO_TTS_SPEAKER` | 否 | 默认 `zh_female_wanqudashu_moon_bigtts`（1.0 音色） |
| `DOUBAO_ASR_RESOURCE_ID` | 否 | 默认 `volc.bigasr.sauc.duration` |

## 运行

先启动机器人侧（`../exe/`），再：

```bash
.venv/bin/python agent_client_demo.py        # 无 Key 验证
.venv/bin/python agent_client_doubao.py      # 完整对话
```

看到 `agent 会话就绪 state=online` 即成功。机器人不在本机时加 `--host 192.168.x.x --port 9002`。

## 常用参数

```bash
--system-prompt "你是导览机器人小X"     # 自定义人设
--greeting "大家好"                     # 自定义开场白（"" 禁用）
--save-audio r.wav --save-input i.wav   # 保存一轮音频，排查听不清
--skill gesture/wave_hands             # demo：主动下发技能
```

## 排障

| 现象 | 处理 |
|---|---|
| Connection refused | 机器人侧未启动，或加 `--host` |
| 握手 401 | 严格模式下签名错；核对 appSecret |
| 握手 503 | 已有会话未退出；关掉旧客户端 |
| 没有识别结果 | 音频太短/太轻；检查麦克风 |
| LLM 404 | 模型 ID 须带日期后缀或用接入点 `ep-xxx` |
| TTS 403 | 1.0 音色与 2.0 资源不匹配 |

协议细节见 [../docs/interface.md](../docs/interface.md)。

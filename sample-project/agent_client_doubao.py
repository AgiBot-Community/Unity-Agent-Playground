#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
X02 竞赛真实智能体客户端（豆包/火山引擎全家桶）。

链路：
  Unity 麦克风 16k PCM --audio_request--> 本客户端
    → ASR  豆包流式语音识别 2.0（bigmodel_nostream，整段识别）
    → LLM  豆包大模型（火山方舟 Ark，OpenAI 兼容）
    → TTS  豆包语音合成 2.0（单向流式，输出 16k/16bit/mono PCM）
    --asr/llm/tts 响应帧--> Unity 播放

鉴权（环境变量）：
  DOUBAO_SPEECH_API_KEY  语音控制台 API Key（ASR + TTS 共用，新版控制台 X-Api-Key）
  ARK_API_KEY            火山方舟 API Key（LLM）
可选：
  DOUBAO_LLM_MODEL       默认 doubao-seed-2-1-turbo-260628（或你的接入点 ep-xxx）
  DOUBAO_TTS_SPEAKER     默认 zh_female_wanqudashu_moon_bigtts（1.0 音色，
                         与 seed-tts-1.0 资源匹配；2.0 音色需 2.0 资源授权）
  DOUBAO_ASR_RESOURCE_ID 默认 volc.bigasr.sauc.duration

用法：
  .venv/bin/python agent_client_doubao.py                 # 默认 ws://localhost:9002
  .venv/bin/python agent_client_doubao.py --system-prompt "你是..."
  .venv/bin/python agent_client_doubao.py --save-audio reply.wav
"""

import argparse
import asyncio
import base64
import gzip
import hashlib
import hmac
import json
import os
import struct
import threading
import time
import urllib.error
import urllib.request
import uuid
import wave

import websockets


def load_env(path=".env"):
    """加载同目录 .env（KEY=VALUE，#注释行忽略），不覆盖已存在的环境变量。"""
    env_path = os.path.join(os.path.dirname(os.path.abspath(__file__)), path)
    if not os.path.isfile(env_path):
        return
    with open(env_path, "r", encoding="utf-8") as f:
        for line in f:
            line = line.strip()
            if not line or line.startswith("#") or "=" not in line:
                continue
            k, _, v = line.partition("=")
            k, v = k.strip(), v.strip().strip("'\"")
            if k and k not in os.environ:
                os.environ[k] = v


load_env()

# ----------------------------------------------------------------------------
# 协议常量（Linksky 侧，对齐 agent_client_demo.py）
# ----------------------------------------------------------------------------

T = {
    "sync": "agentsdk.robot_state.sync",
    "a_start": "agentsdk.audio_request.start",
    "a_append": "agentsdk.audio_request.append",
    "a_commit": "agentsdk.audio_request.commit",
    "asr_final": "agentsdk.asr_response.final",
    "llm_delta": "agentsdk.llm_response.item.delta",
    "llm_done_item": "agentsdk.llm_response.item.done",
    "llm_done": "agentsdk.llm_response.done",
    "tts_delta": "agentsdk.tts_response.item.delta",
    "tts_done_item": "agentsdk.tts_response.item.done",
    "tts_done": "agentsdk.tts_response.done",
    "skill": "agentsdk.xlm_response.skill",
    "error": "agentsdk.error",
}

# 机器人技能工具定义（function calling），与 Unity SkillCatalog 一一对应。
SKILL_TOOLS = [{
    "type": "function",
    "function": {
        "name": "robot_skill",
        "description": (
            "让机器人执行一个动作或表情技能。用户表达动作/表情/移动意图时调用；"
            "调用后正常用自然语言回应即可。"
        ),
        "parameters": {
            "type": "object",
            "properties": {
                "skillType": {"type": "string",
                             "enum": ["gesture", "movement", "emotion"]},
                "skillName": {"type": "string", "enum": [
                    # gesture（3 个基本动作）
                    "wave_hands", "bow", "open_arms",
                    # movement
                    "walk", "turn", "stop",
                    # emotion（5 个经典表情）
                    "happy", "sad", "surprised", "angry", "love",
                    "neutral"]},
                "skillParam": {
                    "type": "object",
                    "description": "walk: distanceM(米,默认1)；turn: angleDeg(度,右转为正)；emotion: durationMs(默认3000)",
                    "properties": {
                        "distanceM": {"type": "number"},
                        "angleDeg": {"type": "number"},
                        "durationMs": {"type": "number"},
                    },
                },
            },
            "required": ["skillType", "skillName"],
        },
    },
}]

# ----------------------------------------------------------------------------
# 豆包（火山引擎）端点与 V3 二进制协议
# ----------------------------------------------------------------------------

ASR_WS_URL = "wss://openspeech.bytedance.com/api/v3/sauc/bigmodel_nostream"
TTS_WS_URL = "wss://openspeech.bytedance.com/api/v3/tts/unidirectional/stream"
LLM_HTTP_URL = "https://ark.cn-beijing.volces.com/api/v3/chat/completions"

# V3 二进制协议：4B header + 4B 大端 payload size + payload
#   byte0: version(4b)=1 | header_size(4b)=1            → 0x11
#   byte1: message_type(4b) | flags(4b)
#   byte2: serialization(4b) | compression(4b)          1=JSON, 1=gzip
#   byte3: reserved
MSG_FULL_CLIENT = 0x1     # 请求参数（JSON）
MSG_AUDIO_ONLY = 0x2      # 音频数据
MSG_SERVER_FULL = 0x9     # 服务端 JSON 响应
MSG_SERVER_AUDIO = 0xB    # 服务端音频数据
MSG_ERROR = 0xF           # 错误
FLAG_LAST_PACKET = 0x2    # 最后一包（负包）


def _header(msg_type, flags, serialization, compression):
    return bytes([(1 << 4) | 1, (msg_type << 4) | flags,
                  (serialization << 4) | compression, 0x00])


def build_full_request(obj, use_gzip=True):
    payload = json.dumps(obj, ensure_ascii=False).encode("utf-8")
    if use_gzip:
        payload = gzip.compress(payload)
        head = _header(MSG_FULL_CLIENT, 0, 1, 1)
    else:
        head = _header(MSG_FULL_CLIENT, 0, 1, 0)
    return head + struct.pack(">I", len(payload)) + payload


def build_audio_packet(pcm: bytes, last=False):
    # 音频不压缩；last=True 为负包（size 可为 0）
    head = _header(MSG_AUDIO_ONLY, FLAG_LAST_PACKET if last else 0, 0, 0)
    return head + struct.pack(">I", len(pcm)) + pcm


def parse_server_message(data: bytes):
    """返回 (msg_type, payload)；gzip 自动解压。

    布局（实测确认，2026-09-16 asr_probe 探测）：
      byte0 = version(高4位)=1 | header_size(低4位)=1
      byte1 = message_type(高4位) | flags(低4位)
      byte2 = serialization(高4位) | compression(低4位)
      byte3 = reserved
      flags bit0=1（带 sequence）：data[4:8]=sequence, data[8:12]=payload size,
                                  data[12:12+size]=payload
      flags bit0=0：data[4:8]=payload size, data[8:8+size]=payload
    """
    if len(data) < 8:
        raise ValueError("server message too short: %d" % len(data))
    msg_type = (data[1] >> 4) & 0xF
    flags = data[1] & 0xF
    serialization = (data[2] >> 4) & 0xF
    compression = data[2] & 0xF
    if flags & 0x1:
        if len(data) < 12:
            raise ValueError("server message too short: %d" % len(data))
        (size,) = struct.unpack(">I", data[8:12])
        payload = data[12:12 + size]
    else:
        (size,) = struct.unpack(">I", data[4:8])
        payload = data[8:8 + size]
    if compression == 1 and payload:
        payload = gzip.decompress(payload)
    if serialization == 1 and msg_type in (MSG_SERVER_FULL, MSG_ERROR):
        return msg_type, json.loads(payload.decode("utf-8")) if payload else {}
    return msg_type, payload


# ----------------------------------------------------------------------------
# ASR：豆包流式语音识别 2.0（bigmodel_nostream，整段识别后一次返回）
# ----------------------------------------------------------------------------

async def asr_transcribe(pcm16k: bytes, key: str, resource_id: str) -> str:
    """16k/16bit/mono PCM → 文本。空音频/空结果返回 ''。"""
    if not pcm16k:
        return ""
    headers = {
        "X-Api-Key": key,
        "X-Api-Resource-Id": resource_id,
        "X-Api-Request-Id": str(uuid.uuid4()),
        "X-Api-Sequence": "-1",
    }
    req_json = {
        "user": {"uid": "x02-agent-demo"},
        "audio": {"format": "pcm", "rate": 16000, "bits": 16, "channel": 1},
        "request": {"model_name": "bigmodel", "enable_punc": True,
                    "result_type": "full", "enable_itn": True},
    }
    async with websockets.connect(ASR_WS_URL, additional_headers=headers,
                                  compression=None, open_timeout=10) as ws:
        await ws.send(build_full_request(req_json))

        chunk = 6400  # 200ms @16k16bit（官方建议 100~200ms/包）
        for i in range(0, len(pcm16k), chunk):
            await ws.send(build_audio_packet(pcm16k[i:i + chunk]))
            await asyncio.sleep(0.02)   # 发包节奏，避免过快
        await ws.send(build_audio_packet(b"", last=True))  # 负包收尾

        final_text = ""
        last_response = {}
        try:
            while True:
                raw = await asyncio.wait_for(ws.recv(), timeout=30)
                msg_type, payload = parse_server_message(raw)
                if msg_type == MSG_ERROR:
                    raise RuntimeError("ASR 错误: %s" % payload)
                if msg_type == MSG_SERVER_FULL:
                    last_response = payload
                    print("agent     [ASR resp] %s" % trunc(json.dumps(
                        payload, ensure_ascii=False), 300))
                    result = payload.get("result") or {}
                    text = result.get("text", "")
                    if text:
                        final_text = text   # full 结果覆盖
                    if payload.get("is_final"):
                        return final_text
        except websockets.exceptions.ConnectionClosed:
            pass              # 服务端发完最终包后主动断开，属正常收尾
        if not final_text:
            print("agent     [ASR] 空结果，最后响应: %s" % json.dumps(
                last_response, ensure_ascii=False)[:500])
        return final_text


# ----------------------------------------------------------------------------
# LLM：火山方舟 Ark（OpenAI 兼容 chat completions，SSE 流式）
# ----------------------------------------------------------------------------

def _llm_stream_iter(url, api_key, model, messages, disable_thinking=True,
                     tools=None):
    """同步生成器：产出 ("text", delta) 或 ("tool_calls", [调用...])。

    - 文本 delta 照常流式产出；
    - tool_calls 增量拼接，流结束时产出一次完整列表（OpenAI 兼容格式）；
    - thinking=disabled：doubao-seed 系列默认开思考模式，语音场景关掉；
      模型不支持该参数（HTTP 400）时自动去掉重试。
    """
    payload = {"model": model, "messages": messages,
               "temperature": 0.7, "stream": True}
    if tools:
        payload["tools"] = tools
    if disable_thinking:
        payload["thinking"] = {"type": "disabled"}
    req = urllib.request.Request(url, data=json.dumps(payload).encode("utf-8"),
                                 method="POST", headers={
        "Authorization": "Bearer " + api_key,
        "Content-Type": "application/json",
    })
    try:
        resp = urllib.request.urlopen(req, timeout=60)
    except urllib.error.HTTPError as e:
        if disable_thinking and e.code == 400:
            yield from _llm_stream_iter(url, api_key, model, messages,
                                        disable_thinking=False, tools=tools)
            return
        raise
    tc_acc = {}   # index → {"id","name","args"}
    with resp:
        for raw in resp:
            line = raw.decode("utf-8").strip()
            if not line.startswith("data:"):
                continue
            data = line[5:].strip()
            if data == "[DONE]":
                break
            try:
                chunk = json.loads(data)
            except ValueError:
                continue
            choices = chunk.get("choices") or []
            if not choices:
                continue
            delta = choices[0].get("delta") or {}
            text = delta.get("content")
            if text:
                yield ("text", text)
            for c in delta.get("tool_calls") or []:
                i = c.get("index", 0)
                slot = tc_acc.setdefault(
                    i, {"id": "", "name": "", "arguments": ""})
                if c.get("id"):
                    slot["id"] = c["id"]
                fn = c.get("function") or {}
                if fn.get("name"):
                    slot["name"] += fn["name"]
                if fn.get("arguments"):
                    slot["arguments"] += fn["arguments"]
    if tc_acc:
        yield ("tool_calls", [tc_acc[i] for i in sorted(tc_acc)])


async def llm_chat_stream(system_prompt: str, history: list, model: str,
                          api_key: str, tools=None):
    """async generator：流式产出 ("text", delta) / ("tool_calls", [...])
    （urllib 线程 → asyncio 队列桥接）。"""
    messages = [{"role": "system", "content": system_prompt}] + list(history)
    loop = asyncio.get_running_loop()
    q = asyncio.Queue()

    def producer():
        try:
            for item in _llm_stream_iter(LLM_HTTP_URL, api_key, model,
                                         messages, tools=tools):
                loop.call_soon_threadsafe(q.put_nowait, item)
            loop.call_soon_threadsafe(q.put_nowait, None)
        except Exception as e:
            loop.call_soon_threadsafe(q.put_nowait, e)

    threading.Thread(target=producer, daemon=True).start()
    while True:
        item = await q.get()
        if item is None:
            return
        if isinstance(item, Exception):
            raise item
        yield item


def slice_history(history: list, turns: int) -> list:
    """取最近 turns 轮对话；截断点对齐到 user 消息边界，
    避免把 assistant.tool_calls / tool 结果对从中间切开（API 会 400）。"""
    if turns <= 0 or len(history) <= turns * 2:
        return list(history)
    cut = len(history) - turns * 2
    while cut < len(history) and history[cut].get("role") != "user":
        cut += 1
    if cut >= len(history):        # 异常兜底：退回粗截
        cut = len(history) - turns * 2
    return list(history[cut:])


# 断句规则：强标点立即成句；弱标点需凑够 min_weak 字；超长无标点强制切
_SENT_STRONG = "。！？!?…\n"
_SENT_WEAK = "，、；;:,"


def cut_sentences(pending: str, min_weak: int = 8, max_buf: int = 40):
    """把待合成文本切成完整句子。返回 (句子列表, 剩余待拼文本)。"""
    out = []
    rest = pending
    while rest:
        cut = -1
        for i, ch in enumerate(rest):
            if ch in _SENT_STRONG or (ch in _SENT_WEAK and i + 1 >= min_weak):
                cut = i
                break
        if cut < 0:
            if len(rest) >= max_buf:
                out.append(rest)
                rest = ""
            break
        end = cut + 1
        while end < len(rest) and rest[end] in _SENT_STRONG + _SENT_WEAK:
            end += 1          # 连续标点（如"？！"）并入同句
        out.append(rest[:end])
        rest = rest[end:]
    return out, rest


# 技能即时口播表：纯工具调用时立刻回话（与动作并行），不等二轮 LLM
SKILL_ACKS = {
    ("gesture", "wave_hands"): "好呀，我这就挥挥手～",
    ("gesture", "bow"): "给您鞠个躬～",
    ("gesture", "open_arms"): "欢迎欢迎！",
    ("movement", "stop"): "好的，我停下了。",
    ("emotion", "happy"): "我现在好开心呀！",
    ("emotion", "sad"): "呜，有点难过……",
    ("emotion", "surprised"): "哇！真的吗？",
    ("emotion", "angry"): "哼，我生气啦！",
    ("emotion", "love"): "爱心送给您～",
    ("emotion", "neutral"): "好的。",
}
SKILL_ACK_GENERIC = "好的，我这就来！"


def pick_ack(skill_type, skill_name, param):
    """按技能挑即时口播；walk/turn 带上参数。"""
    if skill_name == "walk":
        try:
            d = float((param or {}).get("distanceM", 1))
            return "好，我往前走%s米。" % ("%g" % d)
        except (TypeError, ValueError):
            pass
    elif skill_name == "turn":
        try:
            deg = float((param or {}).get("angleDeg", 90))
            return "好，我向%s转。" % ("右" if deg >= 0 else "左")
        except (TypeError, ValueError):
            pass
    return SKILL_ACKS.get((skill_type, skill_name), SKILL_ACK_GENERIC)


# ----------------------------------------------------------------------------
# TTS：豆包语音合成 1.0（v3 单向流式，16k/16bit/mono PCM 分段产出）
# ----------------------------------------------------------------------------

TTS_EVENT_SESSION_FINISH = 152   # 官方文档：SessionFinish 事件号


def parse_tts_frame(msg: bytes):
    """解析 v3 TTS 服务端帧（2026-09-16 实测布局）。

    header(4) + event(4) + session_id_len(4) + session_id + payload_size(4) + payload
    header: version|hdr_size, type|flags, serialization|compression, reserved
    数据帧 flags=4；错误帧布局不同，payload 尽力解析。
    返回 (msg_type, event, payload)；payload 已解压，JSON 帧返回 dict。
    """
    msg_type = (msg[1] >> 4) & 0xF
    flags = msg[1] & 0xF
    serialization = (msg[2] >> 4) & 0xF
    compression = msg[2] & 0xF
    body = msg[4:]
    event = struct.unpack(">i", body[:4])[0] if len(body) >= 4 else 0
    body = body[4:]
    if msg_type == 0xF:
        # 错误帧：code(4)+size(4)+payload，无 session_id
        payload = body[8:] if len(body) > 8 else b""
        if compression == 1 and payload:
            payload = gzip.decompress(payload)
        if serialization == 1 and payload:
            try:
                payload = json.loads(payload.decode("utf-8"))
            except ValueError:
                pass
        return msg_type, event, payload
    (sid_len,) = struct.unpack(">I", body[:4])
    body = body[4:]
    body = body[sid_len:]
    (size,) = struct.unpack(">I", body[:4])
    payload = body[4:4 + size]
    if compression == 1 and payload:
        payload = gzip.decompress(payload)
    if serialization == 1 and payload:
        payload = json.loads(payload.decode("utf-8"))
    return msg_type, event, payload


async def tts_stream(text: str, speaker: str, key: str):
    """async generator：逐段产出 16k PCM bytes。"""
    headers = {
        "X-Api-Key": key,
        "X-Api-Resource-Id": "seed-tts-1.0",
        "X-Api-Request-Id": str(uuid.uuid4()),
    }
    body = {"req_params": {"text": text, "speaker": speaker,
                           "audio_params": {"format": "pcm",
                                            "sample_rate": 16000}}}
    async with websockets.connect(TTS_WS_URL, additional_headers=headers,
                                  compression=None, open_timeout=10,
                                  max_size=10 * 1024 * 1024) as ws:
        await ws.send(build_full_request(body))
        try:
            while True:
                raw = await asyncio.wait_for(ws.recv(), timeout=60)
                msg_type, event, payload = parse_tts_frame(raw)
                if msg_type == MSG_ERROR:
                    raise RuntimeError("TTS 错误: %s" % payload)
                if msg_type == MSG_SERVER_AUDIO:
                    if payload:
                        yield payload
                elif msg_type == MSG_SERVER_FULL:
                    if event == TTS_EVENT_SESSION_FINISH:
                        return
        except websockets.exceptions.ConnectionClosed:
            return          # 音频已收齐，服务端直接断开也视为完成


# ----------------------------------------------------------------------------
# Linksky 握手与帧构造（对齐 agent_client_demo.py）
# ----------------------------------------------------------------------------

def build_headers(app_id, app_key, app_secret, path):
    ts = str(int(time.time() * 1000))
    nonce = "nonce-" + uuid.uuid4().hex[:8]
    payload = "GET\n%s\n%s\n%s" % (path, ts, nonce)
    sig = hmac.new(app_secret.encode("utf-8"), payload.encode("utf-8"),
                   hashlib.sha256).hexdigest()
    return {
        "X-App-Id": app_id,
        "X-App-Key": app_key,
        "X-Timestamp": ts,
        "X-Nonce": nonce,
        "X-Signature": sig,
        "X-Callback-Types": '["audio2tts"]',
    }


def envelope(ftype, agent_id, robot_cid, event_id, item_id=None, **extra):
    d = {
        "type": ftype,
        "agentId": agent_id,
        "agentMode": "passive",
        "robotCid": robot_cid,
        "cid": robot_cid,
        "eventId": event_id,
    }
    if item_id is not None:
        d["itemId"] = item_id
    d.update(extra)
    return d


def trunc(s, n=160):
    return s if len(s) <= n else s[:n] + "...(%d)" % len(s)


def save_wav(path, pcm, rate=16000):
    with wave.open(path, "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(rate)
        w.writeframes(bytes(pcm))


# ----------------------------------------------------------------------------
# Agent 逻辑
# ----------------------------------------------------------------------------

class DoubaoAgent:
    def __init__(self, args):
        self.args = args
        self.robot_cid = "cid-agent-demo"
        self.history = []          # [{role, content}, ...]
        self._tts_seq = 0

    async def send(self, ws, obj):
        text = json.dumps(obj, ensure_ascii=False, separators=(",", ":"))
        print("agent -> gw  %s" % trunc(text))
        await ws.send(text)

    async def send_error(self, ws, event_id, code, msg):
        await self.send(ws, envelope(T["error"], self.args.app_id,
                                     self.robot_cid, event_id,
                                     errorCode=code, errorMsg=msg))

    async def agent_round(self, ws, req, audio_buf):
        """收到 commit 后跑真实链路：ASR → LLM → TTS（流式下发）。"""
        a = self.args
        event_id = req.get("eventId", "")
        item_id = req.get("itemId") or ("item-" + uuid.uuid4().hex[:8])
        t0 = time.time()

        # 诊断：保存上行音频（验证 Unity→agent 音频链路内容）
        if a.save_input:
            save_wav(a.save_input, bytes(audio_buf))
            print("agent     已保存上行音频 → %s (%d bytes)"
                  % (a.save_input, len(audio_buf)))

        # ---- 1) ASR ----
        try:
            asr_text = await asr_transcribe(bytes(audio_buf),
                                           a.speech_key, a.asr_resource_id)
        except Exception as e:
            print("agent     ASR 失败: %s" % e)
            await self.send_error(ws, event_id, 3101, "asr failed: %s" % e)
            return
        print("agent     [ASR %.0fms] %r" % ((time.time() - t0) * 1000, asr_text))
        if not asr_text:
            await self.send_error(ws, event_id, 3102, "empty asr result")
            return
        await self.send(ws, envelope(T["asr_final"], a.app_id, self.robot_cid,
                                    event_id, text=asr_text))

        # ---- 2) LLM 流式（含 function calling）+ 3) TTS 按句流水线 ----
        t1 = time.time()
        self.history.append({"role": "user", "content": asr_text})
        reply = ""
        pending = ""              # 尚未成句的 LLM 输出
        total = bytearray()
        llm_first = tts_first = None
        tool_calls = []

        async def speak(text):
            """一句话 → TTS 流式 PCM → 逐段下发网关。"""
            nonlocal total, tts_first
            async for pcm in tts_stream(text, a.tts_speaker, a.speech_key):
                if tts_first is None:
                    tts_first = time.time()
                    print("agent     [TTS 首包 %.0fms（自轮次开始）]"
                          % ((tts_first - t0) * 1000))
                total += pcm
                self._tts_seq += 1
                await self.send(ws, envelope(
                    T["tts_delta"], a.app_id, self.robot_cid, event_id,
                    item_id=item_id,
                    audio=base64.b64encode(pcm).decode("ascii"),
                    audioLen=len(pcm)))

        try:
            async for kind, payload in llm_chat_stream(
                    a.system_prompt,
                    slice_history(self.history, a.history_turns),
                    a.llm_model, a.ark_key, tools=SKILL_TOOLS):
                if llm_first is None:
                    llm_first = time.time()
                    print("agent     [LLM 首 token %.0fms]"
                          % ((llm_first - t1) * 1000))
                if kind == "tool_calls":
                    tool_calls = payload
                    continue
                delta = payload
                reply += delta
                pending += delta
                await self.send(ws, envelope(
                    T["llm_delta"], a.app_id, self.robot_cid, event_id,
                    item_id=item_id, text=delta))
                sents, pending = cut_sentences(pending)
                for s in sents:
                    await speak(s)          # 顺序朗读，与播放顺序一致
        except Exception as e:
            self.history.pop()
            print("agent     LLM 失败: %s" % e)
            await self.send_error(ws, event_id, 3201, "llm failed: %s" % e)
            return

        # ---- 3.5) 技能下发：LLM function calling → xlm_response.skill ----
        dispatched = False
        ack = ""
        for tc in tool_calls:
            if tc.get("name") != "robot_skill":
                continue
            try:
                args = json.loads(tc.get("arguments") or "{}")
            except ValueError:
                print("agent     工具参数非法: %r" % tc.get("arguments"))
                continue
            await self.send(ws, envelope(
                T["skill"], a.app_id, self.robot_cid, event_id,
                itemId=item_id,
                skillType=args.get("skillType", ""),
                skillName=args.get("skillName", ""),
                skillParam=args.get("skillParam") or {}))
            dispatched = True
            ack = pick_ack(args.get("skillType", ""), args.get("skillName", ""),
                           args.get("skillParam"))
            print("agent     [技能] %s/%s %s"
                  % (args.get("skillType"), args.get("skillName"),
                     args.get("skillParam")))

        if not reply and not tool_calls:
            self.history.pop()
            await self.send_error(ws, event_id, 3202, "empty llm reply")
            return

        # ---- 4) 纯工具调用：技能已下发，立即口播回应（与动作并行，不等二轮 LLM） ----
        if dispatched and not reply:
            reply = ack
            await self.send(ws, envelope(T["llm_delta"], a.app_id,
                                         self.robot_cid, event_id,
                                         item_id=item_id, text=reply))
            await speak(reply)

        if not reply:
            reply = "好的。"          # 兜底：纯工具调用也必须有口头回应
            await self.send(ws, envelope(T["llm_delta"], a.app_id,
                                         self.robot_cid, event_id,
                                         item_id=item_id, text=reply))
            await speak(reply)
        self.history.append({"role": "assistant", "content": reply})
        print("agent     [LLM 完 %.0fms] %s"
              % ((time.time() - t1) * 1000, trunc(reply, 80)))

        await self.send(ws, envelope(T["llm_done_item"], a.app_id, self.robot_cid,
                                     event_id, item_id=item_id))
        await self.send(ws, envelope(T["llm_done"], a.app_id, self.robot_cid,
                                     event_id))

        try:
            if pending:
                await speak(pending)        # 收尾：剩余未成句文本
        except Exception as e:
            print("agent     TTS 失败: %s" % e)
            await self.send_error(ws, event_id, 3301, "tts failed: %s" % e)
            return

        await self.send(ws, envelope(T["tts_done_item"], a.app_id,
                                     self.robot_cid, event_id, item_id=item_id))
        await self.send(ws, envelope(T["tts_done"], a.app_id, self.robot_cid,
                                     event_id))
        if a.save_audio and total:
            save_wav(a.save_audio, total)
            print("agent     已保存 TTS 音频 → %s" % a.save_audio)
        print("agent     本轮完成: 总耗时 %.0fms（音频 %d bytes PCM）"
              % ((time.time() - t0) * 1000, len(total)))

    async def greet(self, ws):
        """连接就绪后的开场播报（独立于对话轮次，网关直接播）。"""
        a = self.args
        if not a.greeting:
            return
        event_id = "evt-greet-" + uuid.uuid4().hex[:8]
        item_id = "item-greet-" + uuid.uuid4().hex[:8]
        try:
            await self.send(ws, envelope(T["llm_delta"], a.app_id,
                                         self.robot_cid, event_id,
                                         item_id=item_id, text=a.greeting))
            await self.send(ws, envelope(T["llm_done_item"], a.app_id,
                                         self.robot_cid, event_id,
                                         item_id=item_id))
            await self.send(ws, envelope(T["llm_done"], a.app_id,
                                         self.robot_cid, event_id))
            async for pcm in tts_stream(a.greeting, a.tts_speaker, a.speech_key):
                self._tts_seq += 1
                await self.send(ws, envelope(
                    T["tts_delta"], a.app_id, self.robot_cid, event_id,
                    item_id=item_id,
                    audio=base64.b64encode(pcm).decode("ascii"),
                    audioLen=len(pcm)))
            await self.send(ws, envelope(T["tts_done_item"], a.app_id,
                                         self.robot_cid, event_id,
                                         item_id=item_id))
            await self.send(ws, envelope(T["tts_done"], a.app_id,
                                         self.robot_cid, event_id))
            print("agent     开场播报已下发: %r" % a.greeting)
        except Exception as e:
            print("agent     开场播报失败（不影响对话）: %s" % e)

    async def session(self, ws):
        a = self.args
        audio_buf = bytearray()

        async for raw in ws:
            try:
                f = json.loads(raw)
            except ValueError:
                print("agent <- gw  非法 JSON: %s" % trunc(raw))
                continue
            ftype = f.get("type", "")
            if ftype != T["a_append"]:        # append 太吵，不打
                print("agent <- gw  %s" % trunc(raw))

            if ftype == T["sync"]:
                print("agent     会话就绪 state=%s callbackType=%s"
                      % (f.get("state"), f.get("callbackType")))
                await self.greet(ws)
            elif ftype == T["a_start"]:
                audio_buf = bytearray()
            elif ftype == T["a_append"]:
                b64 = f.get("audio", "")
                audio_buf += base64.b64decode(b64) if b64 else b""
            elif ftype == T["a_commit"]:
                ms = len(audio_buf) / 32.0
                print("agent     收到 commit：%d bytes（约 %.0f ms 语音）"
                      % (len(audio_buf), ms))
                await self.agent_round(ws, f, audio_buf)
            elif ftype == T["error"]:
                print("agent     网关错误: code=%s msg=%s"
                      % (f.get("errorCode"), f.get("errorMsg")))
            elif ftype == "agentsdk.skill_response.state":
                print("agent     技能状态: %s → %s %s"
                      % (f.get("skillName"), f.get("state"),
                         f.get("detail", "")))
            elif ftype == "agentsdk.state_request.meta":
                pass
            else:
                print("agent     （忽略 %s）" % ftype)

    async def run(self):
        a = self.args
        uri = "ws://%s:%d%s" % (a.host, a.port, a.path)
        while True:
            headers = build_headers(a.app_id, a.app_key, a.app_secret, a.path)
            try:
                print("agent     连接 %s ..." % uri)
                async with websockets.connect(
                        uri,
                        additional_headers=headers,
                        compression=None,       # Unity 手写 WS 服务端不支持压缩扩展
                        user_agent_header=None,
                        open_timeout=5) as ws:
                    print("agent     握手成功（101 Switching Protocols）")
                    await self.session(ws)
            except websockets.exceptions.InvalidStatus as e:
                print("agent     握手被拒绝: HTTP %s" % e.response.status_code)
                print("agent     （400=握手头不兼容 401=签名错 404=路径错 503=已有会话）")
            except (websockets.exceptions.ConnectionClosed, OSError,
                    asyncio.TimeoutError) as e:
                print("agent     连接断开：%s" % e)
            print("agent     3 秒后重连 ...")
            await asyncio.sleep(3)


def main():
    import os
    p = argparse.ArgumentParser(description="X02 豆包真实智能体客户端")
    p.add_argument("--host", default="localhost")
    p.add_argument("--port", type=int, default=9002)
    p.add_argument("--path", default="/api/V1/open-portal/app/wss/agent-sdk")
    p.add_argument("--app-id", default="demo-app")
    p.add_argument("--app-key", default="demo-key")
    p.add_argument("--app-secret", default="demo-secret")
    p.add_argument("--speech-key", default=os.environ.get("DOUBAO_SPEECH_API_KEY", ""),
                   help="语音控制台 API Key（ASR+TTS），或环境变量 DOUBAO_SPEECH_API_KEY")
    p.add_argument("--ark-key", default=os.environ.get("ARK_API_KEY", ""),
                   help="火山方舟 API Key（LLM），或环境变量 ARK_API_KEY")
    p.add_argument("--llm-model", default=os.environ.get("DOUBAO_LLM_MODEL",
                   "doubao-seed-2-1-turbo-260628"))
    p.add_argument("--tts-speaker", default=os.environ.get("DOUBAO_TTS_SPEAKER",
                   "zh_female_wanqudashu_moon_bigtts"))
    p.add_argument("--asr-resource-id", default=os.environ.get(
        "DOUBAO_ASR_RESOURCE_ID", "volc.bigasr.sauc.duration"))
    p.add_argument("--system-prompt",
                   default="你是人形机器人X2的语音助手，名叫灵犀。"
                           "回答口语化、简洁（一般不超过两句话），不要用列表和markdown。"
                           "你可以通过 robot_skill 工具做动作和表情：动作有挥手"
                           "（wave_hands）、鞠躬（bow）、张开双臂（open_arms）；"
                           "表情有开心（happy）、"
                           "难过（sad）、惊讶（surprised）、生气（angry）、爱心"
                           "（love）。用户表达这类意图时调用工具，同时必须给一句"
                           "简短的口头回应（如'好呀，我这就挥手'），不能只调用工具"
                           "不说话。还可以走（walk）、转（turn）、停（stop）。")
    p.add_argument("--history-turns", type=int, default=5,
                   help="LLM 记忆的对话轮数")
    p.add_argument("--greeting",
                   default="你好，我是灵犀，有什么可以帮您？",
                   help="连接就绪后的开场播报（置空 '' 禁用）")
    p.add_argument("--save-audio", metavar="PATH", default=None,
                   help="保存最后一轮 TTS 音频到 wav")
    p.add_argument("--save-input", metavar="PATH", default=None,
                   help="保存最近一轮上行语音到 wav（诊断用）")
    args = p.parse_args()

    if not args.speech_key:
        raise SystemExit("缺少语音 API Key：填 .env 的 DOUBAO_SPEECH_API_KEY 或 --speech-key")
    if not args.ark_key:
        raise SystemExit("缺少方舟 API Key：填 .env 的 ARK_API_KEY 或 --ark-key")

    asyncio.run(DoubaoAgent(args).run())


if __name__ == "__main__":
    main()

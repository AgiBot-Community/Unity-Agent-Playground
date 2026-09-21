"""Unidirectional sentence synthesis compatibility transport."""
import asyncio
import gzip
import json
import struct
import uuid
import websockets
from .speech_protocol import build_full_request, MSG_ERROR, MSG_SERVER_AUDIO, MSG_SERVER_FULL

TTS_WS_URL = "wss://openspeech.bytedance.com/api/v3/tts/unidirectional/stream"

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

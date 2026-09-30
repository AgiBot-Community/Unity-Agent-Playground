"""Doubao bidirectional TTS: incremental text in, PCM audio out."""
import asyncio
import gzip
import json
import os
import struct
import uuid
from typing import AsyncIterator, Optional, Tuple, Dict, Any

import websockets
from websockets.protocol import State

# TTS 服务端点 - 可通过环境变量覆盖
TTS_WS_URL = 'wss://openspeech.bytedance.com/api/v3/tts/bidirection'

# TTS 连接和超时参数
TTS_CONNECT_TIMEOUT_SECONDS = 10
TTS_CLOSE_TIMEOUT_SECONDS = 1
TTS_HANDSHAKE_TIMEOUT_SECONDS = 10
TTS_AUDIO_TIMEOUT_SECONDS = 60
TTS_MAX_MESSAGE_SIZE = 10 * 1024 * 1024  # 10MB


def event_packet(event: int, payload: Dict[str, Any],
                 session_id: Optional[str] = None) -> bytes:
    body = struct.pack('>i', event)
    if session_id is not None:
        sid = session_id.encode()
        body += struct.pack('>I', len(sid)) + sid
    data = json.dumps(payload, ensure_ascii=False).encode()
    return b'\x11\x14\x10\x00' + body + struct.pack('>I', len(data)) + data


def parse_event(raw: bytes) -> Tuple[int, int, str, Any]:
    if not isinstance(raw, bytes) or len(raw) < 8:
        raise ValueError('Invalid TTS frame')
    kind, flags = raw[1] >> 4, raw[1] & 15
    serialization, compression = raw[2] >> 4, raw[2] & 15
    offset = (raw[0] & 15) * 4

    def integer():
        nonlocal offset
        value = struct.unpack_from('>I', raw, offset)[0]
        offset += 4
        return value

    def blob():
        nonlocal offset
        size = integer()
        if offset + size > len(raw):
            raise ValueError('Truncated TTS frame')
        value = raw[offset:offset + size]
        offset += size
        return value

    if kind == 15:
        code, data = integer(), blob()
        if compression == 1:
            data = gzip.decompress(data)
        raise RuntimeError('TTS error %s: %s' % (code, data.decode('utf-8', errors='replace')))
    if not flags & 4:
        raise ValueError('TTS event flag missing')
    event = integer()
    # Server connection events carry a connection ID; session events carry a session ID.
    identifier = blob().decode('utf-8')
    data = blob()
    if compression == 1:
        data = gzip.decompress(data)
    if serialization == 1:
        data = json.loads(data) if data else {}
    return kind, event, identifier, data


class BidiTtsClient:
    def __init__(self, key: str, speaker: str, endpoint: Optional[str] = None):
        self.key, self.speaker = key, speaker
        self.endpoint = endpoint or os.environ.get("TTS_WS_URL", TTS_WS_URL)
        self.ws: Optional[websockets.WebSocketClientProtocol] = None
        self._connect_lock = asyncio.Lock()

    async def connect(self) -> None:
        async with self._connect_lock:
            await self._connect()

    async def _connect(self) -> None:
        if self.ws is not None and self.ws.state == State.OPEN:
            return
        await self.close()
        ws = await websockets.connect(
            self.endpoint, additional_headers={'X-Api-Key': self.key,
                                     'X-Api-Resource-Id': 'seed-tts-1.0',
                                     'X-Api-Connect-Id': str(uuid.uuid4())},
            compression=None, open_timeout=TTS_CONNECT_TIMEOUT_SECONDS,
            close_timeout=TTS_CLOSE_TIMEOUT_SECONDS,
            max_size=TTS_MAX_MESSAGE_SIZE)
        try:
            await ws.send(event_packet(1, {}))
            _, event, _, payload = parse_event(
                await asyncio.wait_for(ws.recv(), TTS_HANDSHAKE_TIMEOUT_SECONDS))
            if event != 50:
                raise RuntimeError('TTS connection rejected: %s %s' % (event, payload))
        except BaseException:
            await ws.close()
            raise
        self.ws = ws

    async def close(self) -> None:
        ws, self.ws = self.ws, None
        if ws is not None:
            await ws.close()

    def request(self, event: int, text: Optional[str] = None) -> Dict[str, Any]:
        params = {'speaker': self.speaker,
                  'audio_params': {'format': 'pcm', 'sample_rate': 16000}}
        if text is not None:
            params['text'] = text
        return {'user': {'uid': 'x02-agent-demo'}, 'event': event,
                'namespace': 'BidirectionalTTS', 'req_params': params}

    async def stream(self, texts: AsyncIterator[str]) -> AsyncIterator[bytes]:
        await self.connect()
        sid = str(uuid.uuid4())
        sender: Optional[asyncio.Task] = None
        completed = False
        try:
            await self.ws.send(event_packet(100, self.request(100), sid))
            _, event, response_sid, payload = parse_event(
                await asyncio.wait_for(self.ws.recv(), TTS_HANDSHAKE_TIMEOUT_SECONDS))
            if event != 150 or response_sid != sid:
                raise RuntimeError('TTS session rejected: %s %s' % (event, payload))

            async def send_text() -> None:
                async for text in texts:
                    if text:
                        await self.ws.send(event_packet(200, self.request(200, text), sid))
                await self.ws.send(event_packet(102, {}, sid))

            sender = asyncio.create_task(send_text())
            while True:
                receive = asyncio.create_task(self.ws.recv())
                try:
                    # Surface sender errors immediately instead of waiting for recv timeout.
                    waiting = {receive} if sender.done() else {receive, sender}
                    done, _ = await asyncio.wait(waiting, timeout=TTS_AUDIO_TIMEOUT_SECONDS,
                                                 return_when=asyncio.FIRST_COMPLETED)
                    if not done:
                        raise TimeoutError('TTS audio timeout')
                    if sender.done():
                        sender.result()
                    raw = await asyncio.wait_for(receive, TTS_AUDIO_TIMEOUT_SECONDS)
                finally:
                    if not receive.done():
                        receive.cancel()
                    await asyncio.gather(receive, return_exceptions=True)
                kind, event, response_sid, payload = parse_event(raw)
                if response_sid != sid:
                    raise RuntimeError('TTS session ID mismatch')
                if kind == 11 and payload:
                    yield payload
                elif event == 152:
                    await sender
                    completed = True
                    return
                elif event in (151, 153):
                    raise RuntimeError('TTS session failed: %s' % payload)
        finally:
            if sender is not None:
                if not sender.done():
                    sender.cancel()
                await asyncio.gather(sender, return_exceptions=True)
            if not completed:
                await self.close()

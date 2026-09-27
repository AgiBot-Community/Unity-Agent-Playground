"""Doubao bidirectional TTS: incremental text in, PCM audio out."""
import asyncio
import gzip
import json
import struct
import uuid

import websockets
from websockets.protocol import State


URL = 'wss://openspeech.bytedance.com/api/v3/tts/bidirection'


def event_packet(event, payload, session_id=None):
    body = struct.pack('>i', event)
    if session_id is not None:
        sid = session_id.encode()
        body += struct.pack('>I', len(sid)) + sid
    data = json.dumps(payload, ensure_ascii=False).encode()
    return b'\x11\x14\x10\x00' + body + struct.pack('>I', len(data)) + data


def parse_event(raw):
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
    def __init__(self, key, speaker):
        self.key, self.speaker = key, speaker
        self.ws = None
        self._connect_lock = asyncio.Lock()

    async def connect(self):
        async with self._connect_lock:
            await self._connect()

    async def _connect(self):
        if self.ws is not None and self.ws.state == State.OPEN:
            return
        await self.close()
        ws = await websockets.connect(
            URL, additional_headers={'X-Api-Key': self.key,
                                     'X-Api-Resource-Id': 'seed-tts-1.0',
                                     'X-Api-Connect-Id': str(uuid.uuid4())},
            compression=None, open_timeout=10, close_timeout=1,
            max_size=10 * 1024 * 1024)
        try:
            await ws.send(event_packet(1, {}))
            _, event, _, payload = parse_event(await asyncio.wait_for(ws.recv(), 10))
            if event != 50:
                raise RuntimeError('TTS connection rejected: %s %s' % (event, payload))
        except BaseException:
            await ws.close()
            raise
        self.ws = ws

    async def close(self):
        ws, self.ws = self.ws, None
        if ws is not None:
            await ws.close()

    def request(self, event, text=None):
        params = {'speaker': self.speaker,
                  'audio_params': {'format': 'pcm', 'sample_rate': 16000}}
        if text is not None:
            params['text'] = text
        return {'user': {'uid': 'x02-agent-demo'}, 'event': event,
                'namespace': 'BidirectionalTTS', 'req_params': params}

    async def stream(self, texts):
        await self.connect()
        sid = str(uuid.uuid4())
        sender = None
        completed = False
        try:
            await self.ws.send(event_packet(100, self.request(100), sid))
            _, event, response_sid, payload = parse_event(
                await asyncio.wait_for(self.ws.recv(), 10))
            if event != 150 or response_sid != sid:
                raise RuntimeError('TTS session rejected: %s %s' % (event, payload))

            async def send_text():
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
                    done, _ = await asyncio.wait(waiting, timeout=60,
                                                 return_when=asyncio.FIRST_COMPLETED)
                    if not done:
                        raise TimeoutError('TTS audio timeout')
                    if sender.done():
                        sender.result()
                    raw = await asyncio.wait_for(receive, 60)
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

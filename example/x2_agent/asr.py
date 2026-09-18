"""Streaming audio upload and recognition lifecycle."""
import asyncio
import time
import uuid
import websockets
from .speech_protocol import (
    build_full_request, build_audio_packet, parse_server_message,
    MSG_ERROR, MSG_SERVER_FULL, FLAG_LAST_PACKET,
)

ASR_WS_URL = "wss://openspeech.bytedance.com/api/v3/sauc/bigmodel_nostream"

async def asr_transcribe(pcm16k: bytes, key: str, resource_id: str) -> str:
    """16k/16bit/mono PCM → 文本。空音频/空结果返回 ''。"""
    if not pcm16k:
        return ""

    async def chunks():
        yield pcm16k

    return await asr_stream_transcribe(chunks(), key, resource_id)


async def asr_stream_transcribe(chunks, key: str, resource_id: str) -> str:
    """录音期间上传；nostream 表示整段返回结果，不要求整段上传。"""
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
    started = time.perf_counter()
    async with websockets.connect(ASR_WS_URL, additional_headers=headers,
                                  compression=None, open_timeout=10,
                                  close_timeout=1) as ws:
        print("agent     [ASR 连接 %.0fms] 开始上传"
              % ((time.perf_counter() - started) * 1000))
        await ws.send(build_full_request(req_json))
        async def receive_result():
            final_text = ""
            while True:
                try:
                    raw = await ws.recv()
                except websockets.exceptions.ConnectionClosedOK:
                    return final_text
                msg_type, payload = parse_server_message(raw)
                if msg_type == MSG_ERROR:
                    raise RuntimeError("ASR 错误: %s" % payload)
                if msg_type == MSG_SERVER_FULL:
                    result = payload.get("result") or {}
                    text = result.get("text", "")
                    if text:
                        final_text = text   # full 结果覆盖
                    # V3 最终包由 flags bit1 标识，通常没有 JSON is_final。
                    if raw[1] & FLAG_LAST_PACKET or payload.get("is_final"):
                        return final_text

        receiver = asyncio.create_task(receive_result())
        pending = bytearray()
        try:
            async for pcm in chunks:
                if receiver.done():
                    return receiver.result()
                pending.extend(pcm)
                while len(pending) >= 6400:  # 200ms @16k16bit
                    await ws.send(build_audio_packet(bytes(pending[:6400])))
                    del pending[:6400]
                    await asyncio.sleep(0)  # 接收任务也需及时处理响应
            await ws.send(build_audio_packet(bytes(pending), last=True))
            return await asyncio.wait_for(receiver, timeout=30)
        finally:
            if not receiver.done():
                receiver.cancel()
            await asyncio.gather(receiver, return_exceptions=True)


class StreamingAsr:
    """录音帧进入独立上传任务，commit 时只等待最终识别。"""

    def __init__(self, key, resource_id):
        # Unity 每帧约 100ms；允许开场白期间缓存的录音批量到达，最多积压一分钟。
        self.queue = asyncio.Queue(maxsize=600)
        self.error = None
        self.task = asyncio.create_task(asr_stream_transcribe(
            self._chunks(), key, resource_id))

    async def _chunks(self):
        while True:
            pcm = await self.queue.get()
            if pcm is None:
                return
            yield pcm

    def feed(self, pcm):
        if self.task.done() or self.error is not None:
            return
        try:
            self.queue.put_nowait(pcm)
        except asyncio.QueueFull:
            self.error = RuntimeError("ASR 上传积压，检查网络后重试")
            self.task.cancel()

    async def finish(self):
        if self.error:
            raise self.error
        if not self.task.done():
            try:
                self.queue.put_nowait(None)
            except asyncio.QueueFull:
                await self.close()
                raise RuntimeError("ASR 上传积压，无法提交录音")
        return await self.task

    async def close(self):
        if not self.task.done():
            self.task.cancel()
        await asyncio.gather(self.task, return_exceptions=True)

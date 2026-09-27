"""Shared Unity gateway event names, authentication and envelopes."""
import hashlib
import hmac
import asyncio
import json
import time
import uuid
from contextlib import aclosing, asynccontextmanager

T = {
    "sync": "agentsdk.robot_state.sync",
    "a_start": "agentsdk.audio_request.start",
    "a_append": "agentsdk.audio_request.append",
    "a_commit": "agentsdk.audio_request.commit",
    "state": "agentsdk.state_request.meta",
    "asr_final": "agentsdk.asr_response.final",
    "llm_delta": "agentsdk.llm_response.item.delta",
    "llm_done_item": "agentsdk.llm_response.item.done",
    "llm_done": "agentsdk.llm_response.done",
    "tts_delta": "agentsdk.tts_response.item.delta",
    "tts_done_item": "agentsdk.tts_response.item.done",
    "tts_done": "agentsdk.tts_response.done",
    "skill": "agentsdk.xlm_response.skill",
    "interrupt": "agentsdk.xlm_response.interrupt",
    "error": "agentsdk.error",
    "runtime_log": "agentsdk.runtime.log",
    "session_state": "agentsdk.session.state",
    "session_priority": "agentsdk.session.priority.set",
    "session_control": "agentsdk.session.control.set",
}

def print_runtime_log(frame):
    """Display Unity diagnostics without sending anything back to the gateway."""
    if frame.get("type") != T["runtime_log"]:
        return False
    print("unity [%s] %s #%s %s" % (
        str(frame.get("level", "info")).upper(),
        frame.get("timestampMs", ""), frame.get("sequence", ""),
        frame.get("message", "")))
    if frame.get("stackTrace"):
        print(frame["stackTrace"])
    if frame.get("truncated"):
        print("unity     日志内容已截断")
    if frame.get("droppedCount"):
        print("unity     日志缓冲区累计丢弃 %s 条" % frame["droppedCount"])
    if frame.get("sessionDroppedCount"):
        print("unity     本连接因发送积压累计丢弃 %s 条日志" % frame["sessionDroppedCount"])
    return True


@asynccontextmanager
async def gateway_messages(ws, on_session_state=None):
    """Keep draining Unity diagnostics while a greeting or voice round is busy."""
    pending = asyncio.Queue(maxsize=256)

    async def receive():
        async for raw in ws:
            try:
                frame = json.loads(raw)
                if not isinstance(frame, dict):
                    raise ValueError("expected JSON object")
            except ValueError:
                print("agent <- gw  非法 JSON: %s" % str(raw)[:160])
                continue
            if on_session_state is not None and frame.get("type") in (T["sync"], T["session_state"]):
                on_session_state(frame)
            if not print_runtime_log(frame):
                await pending.put((raw, frame))

    reader = asyncio.create_task(receive())

    async def frames():
        while True:
            if not pending.empty():
                yield pending.get_nowait()
                continue
            if reader.done():
                reader.result()
                return
            next_frame = asyncio.create_task(pending.get())
            try:
                await asyncio.wait({reader, next_frame}, return_when=asyncio.FIRST_COMPLETED)
                if next_frame.done():
                    yield next_frame.result()
                else:
                    reader.result()
                    return
            finally:
                if not next_frame.done():
                    next_frame.cancel()
                await asyncio.gather(next_frame, return_exceptions=True)

    try:
        async with aclosing(frames()) as messages:
            yield messages
    finally:
        if not reader.done():
            reader.cancel()
        await asyncio.gather(reader, return_exceptions=True)


def build_headers(app_id, app_key, app_secret, path, bad_sig=False,
                  role=None, audio_enabled=None, client_name=None):
    """对齐 AuthVerifier.cs：payload = "GET\\n<path>\\n<ts>\\n<nonce>"。"""
    ts = str(int(time.time() * 1000))
    nonce = "nonce-" + uuid.uuid4().hex[:8]
    payload = "GET\n%s\n%s\n%s" % (path, ts, nonce)
    sig = hmac.new(app_secret.encode("utf-8"), payload.encode("utf-8"),
                   hashlib.sha256).hexdigest()
    headers = {
        "X-App-Id": app_id,
        "X-App-Key": app_key,
        "X-Timestamp": ts,
        "X-Nonce": nonce,
        "X-Signature": "deadbeef" if bad_sig else sig,
        "X-Callback-Types": '["audio2tts"]',
    }
    if role is not None:
        headers["X-Client-Role"] = role
    if audio_enabled is not None:
        headers["X-Audio-Enabled"] = "true" if audio_enabled else "false"
    if client_name is not None:
        headers["X-Client-Name"] = client_name
    return headers


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

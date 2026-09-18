"""Shared Unity gateway event names, authentication and envelopes."""
import hashlib
import hmac
import time
import uuid

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
}


def build_headers(app_id, app_key, app_secret, path, bad_sig=False):
    """对齐 AuthVerifier.cs：payload = "GET\\n<path>\\n<ts>\\n<nonce>"。"""
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
        "X-Signature": "deadbeef" if bad_sig else sig,
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

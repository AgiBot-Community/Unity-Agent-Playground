"""Management protocol: authentication, diagnostics, sessions and manual controls."""
import hashlib
import hmac
import time
import uuid

T = {
    "sync": "agentsdk.robot_state.sync",
    "state": "agentsdk.state_request.meta",
    "skill": "agentsdk.xlm_response.skill",
    "skill_state": "agentsdk.skill_response.state",
    "interrupt": "agentsdk.xlm_response.interrupt",
    "error": "agentsdk.error",
    "runtime_log": "agentsdk.runtime.log",
    "session_state": "agentsdk.session.state",
    "session_priority": "agentsdk.session.priority.set",
    "session_control": "agentsdk.session.control.set",
}

def build_headers(app_id, app_key, app_secret, path, bad_sig=False,
                  role=None, audio_enabled=None, control_enabled=None, client_name=None):
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
    if control_enabled is not None:
        headers["X-Control-Enabled"] = "true" if control_enabled else "false"
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

# X2 agent gateway protocol

[中文](interface.md) | **English** | [Français](interface.fr.md)

Protocol reference v1.0, aligned with LinkSoul AgentSDK v1.4.0. Use this page to implement a client for the repository's Unity gateway. Unity is the WebSocket server and the agent is the client. The gateway captures microphone audio, plays returned PCM, displays text and executes skills. Confirm device capabilities separately when integrating real hardware.

Before integration, start the robot using the [EXE guide](simulator.en.md) or [Unity project guide](unity.en.md), then connect using the [Agent guide](../example/x2_agent/docs/README.en.md). Both routes use the same gateway protocol.

## Connection and authentication

Endpoint: `ws://<robot-host>:9002/api/V1/open-portal/app/wss/agent-sdk`. Messages are JSON text frames; audio is base64 PCM. Up to eight clients may connect by default (`CompetitionLauncher.MaxConnections`). Disable WebSocket compression (`compression=None` in the Python client).

Use `127.0.0.1` for local connections. Remote access requires changing the gateway's listen address and making its port reachable; see the [Unity configuration guide](unity.en.md).

| Header | Value |
|---|---|
| `X-App-Id`, `X-App-Key` | Application credentials |
| `X-Timestamp` | Unix milliseconds; strict validation expects a ±5-minute window |
| `X-Nonce` | Unique random value for each connection |
| `X-Signature` | Lowercase HMAC-SHA256 hex digest |
| `X-Callback-Types` | JSON array `["audio2tts"]` |

```python
import hashlib
import hmac

payload = "GET\n" + path + "\n" + ts + "\n" + nonce
signature = hmac.new(app_secret.encode(), payload.encode(), hashlib.sha256).hexdigest()
```

The path, including case, participates in the signature. Demo credentials are `demo-app` / `demo-key` / `demo-secret`. Clients always sign; enforcement depends on the gateway build. Do not assume permissive authentication. Changing it requires rebuilding the gateway from [Unity sources](../unity-agent-playground/).

| HTTP status | Meaning |
|---|---|
| 101 | WebSocket upgrade accepted |
| 400 | Invalid/non-WebSocket upgrade |
| 401 | Credential, signature or timestamp failure |
| 404 | Wrong path |
| 503 | Connection limit reached |

Wait for `agentsdk.robot_state.sync` with `state=online`, but do not assume it is the first frame: audio events may arrive earlier. Reconnect after a disconnect; the example uses a three-second retry interval. No application heartbeat is required by this gateway.

## Envelope

Multi-client headers: `X-Client-Role` (`agent` by default, or `controller` / `observer`),
`X-Client-Name` (ASCII display name), `X-Audio-Enabled` (`true` for agents by default,
otherwise `false`; observers never receive audio). `X-Control-Enabled` declares initial action
eligibility (defaults to true when omitted, except observers). The management console always sends
both flags as `false` to avoid preempting the Agent during connection. Controllers have fixed priority **1000**;
agents start at **50**, observers at **0**. The active controller can set other non-controller
connections to an integer from **0 to 999**. Priorities reset on reconnect. Earlier connections win ties.

Only the highest-priority non-observer with action takeover enabled controls skills and interrupts. Microphone frames go to
the highest-priority audio-enabled connection. A manual console therefore coexists with a voice
agent. The console has no built-in voice modes. Runtime logs and state updates are broadcast.
Lower-priority commands are rejected with error `4091`, not replayed. Ownership is reassigned on disconnect.
Roles are declared by trusted clients and use the existing handshake authentication.

`agentsdk.session.state` carries `revision`, `controlOwnerCid`, `audioOwnerCid`, and `sessions`.
Each session entry includes `robotCid`, `name`, `role`, `priority`, `audioEnabled`,
`controlActive`, `audioActive`. Online sync also includes these own-session capability flags;
standby agents should skip greetings and cloud warm-up.
Use the newest revision. To edit a priority, the active controller sends
`{"type":"agentsdk.session.priority.set","eventId":"...","targetRobotCid":"...","priority":800}`.
Success broadcasts a new state; unauthorized edits, invalid targets/ranges or controller edits return `4093`.

A controller can release/reclaim its own action eligibility with
`{"type":"agentsdk.session.control.set","eventId":"...","enabled":false}`.
`enabled` must be a JSON boolean. Priority stays at 1000 and audio settings are unchanged.
The first connected controller retains administration via `managementOwnerCid`, even after releasing
actions. Session entries expose `controlEnabled`; online sync also exposes `controlEnabled` and `canManage`.
Success broadcasts state; invalid requests return `4093`. Agents should apply authority changes on receipt,
including during an active voice round, and recheck authority before dispatching a skill.

```json
{
  "type": "agentsdk.asr_response.final",
  "agentId": "demo-app",
  "agentMode": "passive",
  "robotCid": "cid-example",
  "cid": "cid-example",
  "eventId": "evt-example",
  "itemId": "item-example",
  "text": "Hello"
}
```

Preserve incoming `agentId`, `robotCid`/`cid`, `eventId` and, when supplied, `itemId` when responding to a recording. These correlate the robot, turn and item. `agentMode` is `passive` in outgoing example messages. The greeting is a separate agent-initiated turn after synchronization.

## Gateway → agent

| Type | Fields / behavior |
|---|---|
| `agentsdk.robot_state.sync` | `agentId`, `state`, `callbackType="audio2tts"`, `agentMeta` |
| `agentsdk.audio_request.start` | Envelope and `itemId`; recording begins |
| `agentsdk.audio_request.append` | `audio` (base64 PCM), `audioLen` (decoded bytes) |
| `agentsdk.audio_request.commit` | Recording ends after silence or the duration limit |
| `agentsdk.state_request.meta` | `stateName`, `stateValue`, e.g. `power`, `ok` |
| `agentsdk.skill_response.state` | Simulator extension: `skillName`, `state`, `detail` |

The default agent starts speech recognition (ASR) upload at `start`, streams `append` data during recording and waits for the final result after `commit`. `audioLen` counts decoded PCM bytes, not base64 characters.

### Unity runtime logs: `agentsdk.runtime.log`

This simulator extension forwards Unity engine and script logs over the same WebSocket,
without a subscription request. Both Python clients display them while greetings and voice rounds run.
The normal envelope is followed by `source: "unity"`, `level` (`info`, `warning`, `error`),
`logType` (`Log`, `Warning`, `Error`, `Assert`, `Exception`), `message`, `stackTrace`,
`timestampMs` (UTC Unix milliseconds), `sequence`, `threadId`, `droppedCount` and `truncated`.
Assertions and exceptions use level `error`; stack content depends on Unity's stack-trace settings.

The capture buffer retains the most recent 256 records while disconnected or backpressured.
`droppedCount` counts capture-buffer evictions since runtime startup. At most 32 logs are handed
off per frame and 128 wait in a session's diagnostic queue; voice traffic has priority.
Messages and stacks are limited to 8192 and 16384 UTF-16 code units respectively.
Long entries set `truncated: true`. Log sends do not log themselves.
Delivery is best effort, without ACKs; entries already handed to a disconnected session are not replayed.
Compilation errors, editor messages before runtime startup, and unsent crash logs are outside this channel.
Clients may ignore this extension. The gateway limits each complete message, including all fragments,
to 16 MiB and closes oversized messages with code `1009`.

## Agent → gateway

All messages use the correlation envelope above.

| Type | Payload |
|---|---|
| `agentsdk.asr_response.middle` | `text`, optional intermediate result |
| `agentsdk.asr_response.final` | `text`, final result |
| `agentsdk.llm_response.item.delta` | `itemId`, `text`; incremental text, not the entire reply |
| `agentsdk.llm_response.item.done` | `itemId`; text item complete |
| `agentsdk.llm_response.done` | LLM turn complete |
| `agentsdk.tts_response.item.delta` | `itemId`, `audio`, `audioLen`; PCM chunk |
| `agentsdk.tts_response.item.done` | Audio item complete |
| `agentsdk.tts_response.done` | Audio turn complete; reset playback state |
| `agentsdk.xlm_response.skill` | `skillType`, `skillName`, `skillParam` |
| `agentsdk.xlm_response.interrupt` | `interruptType`, e.g. `chat`; optional `interruptTips` |
| `agentsdk.error` | `errorCode`, `errorMsg` |

PCM playback starts as chunks arrive. LLM text and TTS audio may interleave: do not wait for the entire LLM reply to synthesize audio. The default uses bidirectional TTS; sentence synthesis is a fallback. Finish each stream with its item/turn completion messages.

## Skills and interrupts

This table describes the default Unity skill catalog. After changing skills, rebuild and validate the target application; see the [Unity guide](unity.en.md) for configuration locations.

| `skillType` | `skillName` | `skillParam` |
|---|---|---|
| `gesture` | `wave_hands`, `open_arms` | `{}` |
| `movement` | `walk` | `{"distanceM": 1.0}`; reference range 0.2–5 m |
| `movement` | `turn` | `{"angleDeg": 90}`; positive turns right |
| `movement` | `stop` | `{}` |
| `emotion` | `happy`, `sad`, `surprised`, `angry`, `love`, `neutral` | `{"durationMs": 3000}` |

The voice client `agent.py` exposes these skills through the `robot_skill` LLM tool; the offline demo does not recognize spoken requests. Gestures use joint trajectories, expressions drive the face, and TTS energy animates the mouth. Movement completion is reported after the robot settles. Unknown skills report failure without terminating speech.

The simulator reports `running`, `done` or `failed` through `agentsdk.skill_response.state`. Dispatch is asynchronous: sending a command does not mean its action has finished. Wait for `done` or `failed` before a dependent action. Confirm support for this simulation extension when integrating hardware.

An explicit interrupt stops TTS and current movement/gestures. Speaking during playback does not trigger an interrupt in the current half-duplex build.

| Example error code | Meaning |
|---|---|
| 3101 / 3102 | ASR failure / empty result |
| 3201 / 3202 | LLM failure / empty reply |
| 3301 | TTS failure |

Replacing, stopping or interrupting a running movement or gesture also reports `failed`
for the old command, so clients can finish waiting for its terminal state.

## Audio and timing

Default gesture timing is about 6 seconds for `wave_hands` and 6.5 seconds for `open_arms`.
Waving raises a bent right arm and uses a small wrist-led swing; opening the arms uses a rounded,
slightly staggered reach and a short hold. Shoulder, elbow, wrist and head-yaw targets ease into
and out of the pose, with joint-limit and target-speed protection. Replacement blends from the
current targets. Interruption reports `failed` immediately and returns smoothly to rest.
The final settling can slightly extend nominal duration; wait for `done` when chaining actions.
Gestures do not command the waist, root or legs, and an episode reset cancels the old trajectory.

Audio is 16,000 Hz, signed 16-bit mono PCM, base64-encoded in JSON. Typical upstream chunks are 100 ms = 1,600 samples = 3,200 bytes. Voice activity detection (VAD) defaults in `Assets/X02Competition/Robot/Audio/VadGate.cs` are RMS start 0.02, stop 0.008, silence 600 ms and maximum turn 15,000 ms. The portable application does not expose every setting; use runtime logs for actual timing. VAD pauses during TTS to avoid recording the robot's own voice.

A normal turn is `start → append × N → commit → ASR final`, followed by interleaved LLM deltas and TTS chunks, completion messages and optional skill status. Distinguish recording/silence time, post-commit ASR wait, first LLM token and first audio latency. There is no fixed end-to-end latency guarantee.

## Compatibility and debugging

Unknown message types are ignored. Some types, including `llm_response.item.done`, `vlm_*`, `greet_*` and `xlm_response.control`, may be logged but not acted upon in this gateway version. Keep completion messages for protocol compatibility.

**F1** toggles the Unity panel showing connection state, ASR/LLM text and skill events. Buttons execute skills locally. After synchronization, the agent sends a greeting as LLM text and TTS audio. In `agent.py`, `--greeting` changes text and synthesized speech; in the demo it changes captions only and retains the recording. An empty value disables the greeting. See the [example guide](../example/x2_agent/docs/README.en.md).

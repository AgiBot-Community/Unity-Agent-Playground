# Example voice agent

[中文](../zh-CN/README.md) | **English** | [Français](../fr/README.md)

These Python clients connect to the Unity robot gateway over WebSocket. `agent.py` uses Doubao speech services and Volcengine Ark for conversations and skill calls. `demo.py` checks connections and playback using fixed text and bundled recordings, with a sine-wave fallback for missing or invalid audio.

Commands below run in the repository's `example/x2_agent/` directory using Windows PowerShell. On Linux/macOS, use `python3` if required by your environment; the supplied portable simulator runs on Windows.

## Project layout

This is a self-contained Agent project. It can run without the console and reads its own `.env`.
The GUI is a separate project; see the [console guide](../../../../docs/en/console.md).

| Path | Responsibility |
|---|---|
| `agent.py` / `demo.py` | Voice agent and offline demo entry scripts |
| `requirements.txt` | Third-party dependencies |
| `x2_agent/agent.py` | Session orchestration, history, skills and CLI |
| `x2_agent/asr.py` | Audio upload during recording, recognition and cancellation |
| `x2_agent/llm.py` | Streaming LLM requests and connection reuse |
| `x2_agent/tts.py` | Bidirectional streaming synthesis |
| `x2_agent/sentence_tts.py` | Sentence-based synthesis fallback |
| `x2_agent/speech_protocol.py` | Doubao V3 binary frames |
| `x2_agent/gateway.py` | Unity events, HMAC authentication and envelopes |
| `x2_agent/demo.py` | Offline gateway demo |
| `x2_agent/config.py`, `audio.py` | Configuration, WAV output and log truncation |
| `tests/` | Local ASR, pipeline and configuration regression tests |
| `.env.example` | Public template; copy to a private `.env` |

## Install and run

Python 3.10+ is required. For real conversations, enable Volcengine Speech (ASR/TTS) and Ark (LLM). In PowerShell, from the repository’s `example/x2_agent/` directory:

```powershell
python -m pip install -r requirements.txt
```

Start the robot using one of these routes:

- **Portable simulator:** download and run the Windows EXE from GitHub Releases; follow the [simulator guide](../../../../docs/en/simulator.md).
- **From the Unity project:** open `unity-agent-playground/` with Unity **2022.3.62f3c1**, open `Assets/X02Competition/Scenes/scene.unity` and press Play; follow the [Unity guide](../../../../docs/en/unity.md).

Both use local port `9002`; run only one robot instance. Then start one Agent from `example/x2_agent/`:

```powershell
python demo.py
```

Wait for `state=online` and the end of the greeting, then speak to check the fixed reply and audio playback. Stop the demo with **Ctrl+C** before configuring real conversations:

```powershell
# Preserve an existing .env
if (-not (Test-Path .env)) { Copy-Item .env.example .env }
# Set DOUBAO_SPEECH_API_KEY and ARK_API_KEY in .env
python agent.py
```

Keep `x2_agent/` beside the entry scripts and run them with the same Python environment used to install dependencies. From the repository root, use `python example/x2_agent/agent.py`; the default configuration remains `example/x2_agent/.env`.

For a remote connection, set `--host <address> --port 9002` and ensure the gateway listens on a reachable network address. Client options alone do not change Unity's default loopback listener.

The log `agent 会话就绪 state=online` confirms the connection. Wait for the greeting to finish before speaking. After commit, listening pauses through ASR, LLM processing and TTS playback. It resumes when the reply ends, on an explicit interrupt, after empty recognition or failure, or after 90 seconds without response progress. The default prompt and voice use Chinese.

## Skills

The phrases below require `agent.py`; the model selects skills from the request. The demo does not recognize speech commands. To test skills without cloud services, use F1 buttons or the demo's `--skill` option.

- “挥挥手”, “张开双臂”: wave and open arms (`wave_hands`, `open_arms`).
- “往前走一米”, “向左转”, “停”: walk, turn and stop after recognition and skill dispatch. During playback, use the panel's stop button or an explicit interrupt.
- Requests for happy, sad, surprised or other expressions change the robot face. The protocol also includes a neutral expression.
- **F1** opens the Unity debug panel; its skill buttons work without a cloud key.

The mouth follows speech playback. See the [protocol skill table](../../../../docs/en/interface.md) for skill names and parameters.

The demo’s `--reply` and `--greeting` change captions, not the bundled voice recordings. Audio comes from `x2_agent/greeting.wav` and `x2_agent/tts.wav`, sent in 200 ms chunks.

## Configuration

Configuration loads at CLI startup, not on import. Precedence: CLI arguments > existing environment variables > `.env` > defaults. Scripts load `.env` from the `example/x2_agent/` project root by default. Use `--env-file` with your configuration file path to override it; a missing file is an error.

| Variable | Required for Doubao | Default / meaning |
|---|---|---|
| `DOUBAO_SPEECH_API_KEY` | Yes | Speech key shared by ASR and TTS |
| `ARK_API_KEY` | Yes | Ark LLM key |
| `DOUBAO_LLM_MODEL` | No | `doubao-seed-2-0-mini-260428` |
| `DOUBAO_TTS_SPEAKER` | No | `zh_female_wanqudashu_moon_bigtts`; compatible with `seed-tts-1.0` |
| `DOUBAO_ASR_RESOURCE_ID` | No | `volc.bigasr.sauc.duration` |

Copy [`.env.example`](../../.env.example) for your local configuration. Keep keys out of Git; the repository [`.gitignore`](../../../../.gitignore) excludes `.env` files.

## Useful options

Override service endpoints with `ARK_API_URL`, `ASR_WS_URL`, `TTS_WS_URL` and
`TTS_SENTENCE_WS_URL`, or the higher-priority `--ark-api-url`, `--asr-ws-url`,
`--tts-ws-url` and `--tts-sentence-ws-url` options. Sentence and bidirectional TTS
use separate endpoints. The project's `skills.yaml` supplies tool definitions,
parameter validation and acknowledgments; select another file with `--skills-file`.
Use `--log-level` and `--log-file` for Python orchestration logs, and `--metrics-file`
to export counters and statistics over the most recent 1000 duration samples per
metric on normal exit or Ctrl+C. Timing excludes human recording time; a dispatched
skill counter does not confirm execution. Unity logs still appear in the terminal.

Run these from the repository’s `example/x2_agent/` directory with your virtual environment's Python:

```powershell
python agent.py --help
python agent.py --system-prompt "You are a concise robot guide."
python agent.py --save-audio reply.wav --save-input input.wav
python agent.py --asr-after-commit
python agent.py --llm-model doubao-seed-2-1-turbo-260628
python agent.py --tts-mode sentence
python agent.py --greeting "你好"
python agent.py --greeting=
python demo.py --reply "Hello"
python demo.py --skill gesture/wave_hands
python demo.py --interrupt chat
```

The default uploads ASR audio while recording, reuses LLM connections and passes text deltas directly into a bidirectional TTS session. `--asr-after-commit` delays upload for comparison; `--tts-mode sentence` selects the fallback. Set the model with `--llm-model` or `DOUBAO_LLM_MODEL` in `.env`.

## Troubleshooting

| Symptom | Check |
|---|---|
| Connection refused | Start the EXE or enter Editor Play mode; verify host and port. Remote access also requires a reachable network listener on the gateway |
| HTTP 401 | Credentials, signature and timestamp when strict authentication is enabled |
| HTTP 503 | The gateway connection limit has been reached (eight by default) |
| Speech promises a gesture but no action / `4091` | If a manual console is connected, release its action takeover in the sessions tab. Its priority stays 1000. Confirm the Agent owns actions and the skill reports `running` / `done` |
| No transcription | Microphone mute state, input level and recording length |
| Long wait after speech | Separate recording start→commit, post-commit ASR, first LLM token and first TTS audio timings; silence detection is not ASR processing time |
| LLM 404 | Full dated model ID or your `ep-...` endpoint ID |
| TTS 403 | Voice/resource authorization; a 2.0 voice does not match `seed-tts-1.0` |
| No audible reply | Check the Windows output device and volume; compare TTS logs and `--save-audio` output |

## Tests and development

Run from `example/x2_agent/` with the same Python environment used to install dependencies:

```powershell
python -B -m unittest discover -s tests -v
```

Tests use local mock services without Unity or cloud APIs.

See the [protocol](../../../../docs/en/interface.md), [development guide](../../../../docs/en/development.md) and [contributing guide](../../../../docs/en/CONTRIBUTING.md).

## Runtime logs

Unity runtime logs arrive as `agentsdk.runtime.log` on the same connection.
Both clients display Info, Warning, Error and exception stacks, including during greetings
and voice rounds. Buffer drops and truncation are reported. See the
[protocol reference](../../../../docs/en/interface.md) for fields and delivery limits.

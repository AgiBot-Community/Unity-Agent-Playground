# Example voice agent

[中文](../README.md) | **English** | [Français](README.fr.md)

Use `python -m x2_agent` for real Doubao conversations and skill calls. Use `python -m x2_agent.demo` first to validate the gateway without cloud credentials: it returns fixed text and a sine-wave tone, not recognized speech or a synthesized voice.

## Project layout

| Path | Responsibility |
|---|---|
| `pyproject.toml` | Metadata, Python requirement and dependencies |
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

Python 3.10+ is required. For real conversations, enable Volcengine Speech (ASR/TTS) and Ark (LLM). In PowerShell, from the repository’s `example/` directory:

```powershell
python -m venv .venv
.\.venv\Scripts\python.exe -m pip install -e .
```

Start [the simulator](../../docs/simulator.en.md) first, then:

```powershell
.\.venv\Scripts\python.exe -m x2_agent.demo
# Stop the demo with Ctrl+C before starting another client
if (-not (Test-Path .env)) { Copy-Item .env.example .env }
# Set both API keys in .env
.\.venv\Scripts\python.exe -m x2_agent
```

The clients use Python modules; no client EXE entry points are generated. On Linux/macOS, the Python path is `.venv/bin/python`; the supplied Unity simulator is for Windows. Remote connections require a reachable gateway that listens externally, in addition to `--host <address> --port 9002`.

Wait for the greeting to finish before speaking. The current flow is half-duplex. The default prompt and voice target Chinese; translating these documents does not change language support in the speech services or Unity UI.

## Skills

- “挥挥手”: wave; `gesture/wave_hands` is the only current gesture.
- “往前走一米”, “向左转”, “停”: walk, turn and stop.
- Requests for happy, sad, surprised or other expressions change the robot face. The protocol also includes a neutral expression.
- **F1** opens the Unity debug panel; its skill buttons work without a cloud key.

## Configuration

Configuration loads at CLI startup, not on import. Precedence: CLI arguments > existing environment variables > `.env` > defaults. Local editable installs load `.env` from the `example/` project root. `--env-file D:\config\x2.env` selects an explicit file and reports a missing file as an error.

| Variable | Required for Doubao | Default / meaning |
|---|---|---|
| `DOUBAO_SPEECH_API_KEY` | Yes | Speech key shared by ASR and TTS |
| `ARK_API_KEY` | Yes | Ark LLM key |
| `DOUBAO_LLM_MODEL` | No | `doubao-seed-2-0-mini-260428` |
| `DOUBAO_TTS_SPEAKER` | No | `zh_female_wanqudashu_moon_bigtts`; compatible with `seed-tts-1.0` |
| `DOUBAO_ASR_RESOURCE_ID` | No | `volc.bigasr.sauc.duration` |

Keep `.env`, recordings, virtual environments and diagnostics out of Git.

## Useful options

Run these from the repository’s `example/` directory with your virtual environment's Python:

```powershell
.\.venv\Scripts\python.exe -m x2_agent --help
.\.venv\Scripts\python.exe -m x2_agent --system-prompt "You are a concise robot guide."
.\.venv\Scripts\python.exe -m x2_agent --save-audio reply.wav --save-input input.wav
.\.venv\Scripts\python.exe -m x2_agent --asr-after-commit
.\.venv\Scripts\python.exe -m x2_agent --llm-model doubao-seed-2-1-turbo-260628
.\.venv\Scripts\python.exe -m x2_agent --tts-mode sentence
.\.venv\Scripts\python.exe -m x2_agent --greeting "你好"
.\.venv\Scripts\python.exe -m x2_agent --greeting=
.\.venv\Scripts\python.exe -m x2_agent.demo --reply "Hello"
.\.venv\Scripts\python.exe -m x2_agent.demo --skill gesture/wave_hands
.\.venv\Scripts\python.exe -m x2_agent.demo --interrupt chat
```

The default uploads ASR audio while recording, reuses LLM connections and passes text deltas directly into a bidirectional TTS session. `--asr-after-commit` delays upload for comparison; `--tts-mode sentence` selects the fallback. Mini prioritizes speed; its reasoning behavior can differ from Turbo.

## Troubleshooting

| Symptom | Check |
|---|---|
| Connection refused | Start Unity; verify host, port and listen address |
| HTTP 401 | Credentials, signature and timestamp when strict authentication is enabled |
| HTTP 503 | Another agent occupies the only session |
| No transcription | Microphone mute state, input level and recording length |
| Long wait after speech | Separate recording start→commit, post-commit ASR, first LLM token and first TTS audio timings; silence detection is not ASR processing time |
| LLM 404 | Full dated model ID or your `ep-...` endpoint ID |
| TTS 403 | Voice/resource authorization; a 2.0 voice does not match `seed-tts-1.0` |
| No audible reply | Check the Windows output device and volume; compare TTS logs and `--save-audio` output |

## Tests and development

```powershell
.\.venv\Scripts\python.exe -B -m unittest discover -s tests -v
```

Tests use local mock services, not Unity or cloud APIs. Dependencies belong in `pyproject.toml`; reinstall with `pip install -e .` after changing them. Run the agent locally with `python -m x2_agent` or `python -m x2_agent.demo`. Generated installation metadata and caches are ignored.

See the [protocol](../../docs/interface.en.md) and [development guide](../../docs/development.en.md).

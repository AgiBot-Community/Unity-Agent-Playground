# Example voice agent

[中文](../README.md) | **English** | [Français](README.fr.md)

This directory provides Python clients for the Unity robot gateway. Start with `python demo.py` to check the connection and audio without cloud credentials. It returns fixed text and bundled recordings, with a sine-wave fallback if a recording is missing or invalid. Then configure `python agent.py` for Doubao voice conversations and skill calls.

Commands below run in the repository's `example/` directory using Windows PowerShell. On Linux/macOS, use `python3` if required by your environment; the supplied portable simulator runs on Windows.

## Project layout

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

Python 3.10+ is required. For real conversations, enable Volcengine Speech (ASR/TTS) and Ark (LLM). In PowerShell, from the repository’s `example/` directory:

```powershell
python -m pip install -r requirements.txt
```

Start the robot using one of these routes:

- **From the EXE:** on Windows, launch `exe/x2模拟器.exe` from the repository; follow the [EXE guide](../../docs/simulator.en.md).
- **From the Unity project:** open `unity-agent-playground/` with Unity **2022.3.62f3c1**, open `Assets/X02Competition/Scenes/scene.unity` and press Play; follow the [Unity guide](../../docs/unity.en.md).

Both use local port `9002`; run only one robot instance. Then start one Agent from `example/`:

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

Keep `x2_agent/` beside the entry scripts and run them with the same Python environment used to install dependencies. From the repository root, use `python example/agent.py`; the default configuration remains `example/.env`.

For a remote connection, set `--host <address> --port 9002` and ensure the gateway listens on a reachable network address. Client options alone do not change Unity's default loopback listener.

The log `agent 会话就绪 state=online` confirms the connection. Wait for the greeting to finish before speaking. The current flow is half-duplex. The default prompt and voice target Chinese; translating these documents does not change language support in the speech services or Unity UI.

## Skills

The phrases below require `agent.py`; the model selects skills from the request. The demo does not recognize speech commands. To test skills without cloud services, use F1 buttons or the demo's `--skill` option.

- “挥挥手”, “张开双臂”: wave and open arms (`wave_hands`, `open_arms`).
- “往前走一米”, “向左转”, “停”: walk, turn and stop after recognition and skill dispatch. During playback, use the panel's stop button or an explicit interrupt.
- Requests for happy, sad, surprised or other expressions change the robot face. The protocol also includes a neutral expression.
- **F1** opens the Unity debug panel; its skill buttons work without a cloud key.

The mouth follows speech playback. See the [protocol skill table](../../docs/interface.en.md) for skill names and parameters.

The demo’s `--reply` and `--greeting` change captions, not the bundled voice recordings. Audio comes from `x2_agent/greeting.wav` and `x2_agent/tts.wav`, sent in 200 ms chunks.

## Configuration

Configuration loads at CLI startup, not on import. Precedence: CLI arguments > existing environment variables > `.env` > defaults. Scripts load `.env` from the `example/` project root by default. Use `--env-file` with your configuration file path to override it; a missing file is an error.

| Variable | Required for Doubao | Default / meaning |
|---|---|---|
| `DOUBAO_SPEECH_API_KEY` | Yes | Speech key shared by ASR and TTS |
| `ARK_API_KEY` | Yes | Ark LLM key |
| `DOUBAO_LLM_MODEL` | No | `doubao-seed-2-0-mini-260428` |
| `DOUBAO_TTS_SPEAKER` | No | `zh_female_wanqudashu_moon_bigtts`; compatible with `seed-tts-1.0` |
| `DOUBAO_ASR_RESOURCE_ID` | No | `volc.bigasr.sauc.duration` |

The versioned configuration template is [`.env.example`](../.env.example). Keep the `.env` containing your keys private; follow the repository [`.gitignore`](../../.gitignore) when adding files.

## Useful options

Run these from the repository’s `example/` directory with your virtual environment's Python:

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

The default uploads ASR audio while recording, reuses LLM connections and passes text deltas directly into a bidirectional TTS session. `--asr-after-commit` delays upload for comparison; `--tts-mode sentence` selects the fallback. Mini prioritizes speed; its reasoning behavior can differ from Turbo.

## Troubleshooting

| Symptom | Check |
|---|---|
| Connection refused | Start the EXE or enter Editor Play mode; verify host and port. Remote access also requires a reachable network listener on the gateway |
| HTTP 401 | Credentials, signature and timestamp when strict authentication is enabled |
| HTTP 503 | Another agent occupies the only session |
| No transcription | Microphone mute state, input level and recording length |
| Long wait after speech | Separate recording start→commit, post-commit ASR, first LLM token and first TTS audio timings; silence detection is not ASR processing time |
| LLM 404 | Full dated model ID or your `ep-...` endpoint ID |
| TTS 403 | Voice/resource authorization; a 2.0 voice does not match `seed-tts-1.0` |
| No audible reply | Check the Windows output device and volume; compare TTS logs and `--save-audio` output |

## Tests and development

Run from `example/` with the same Python environment used to install dependencies:

```powershell
python -B -m unittest discover -s tests -v
```

Tests use local mock services, not Unity or cloud APIs. Dependencies belong in `requirements.txt`; update them with `python -m pip install -r requirements.txt`. Run the scripts with `python agent.py` or `python demo.py`.

See the [protocol](../../docs/interface.en.md) and [development guide](../../docs/development.en.md).

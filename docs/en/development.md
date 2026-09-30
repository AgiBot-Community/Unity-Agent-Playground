# Development and maintenance

[中文](../zh-CN/development.md) | **English** | [Français](../fr/development.md)

This page covers module responsibilities, tests and releases. See the [quick start](README.md) for a first run and the [contributing guide](CONTRIBUTING.md) for issues and pull requests.

## Environment and entry points

For integration tests, start the [portable simulator](simulator.md) or the main scene in the [Unity project](unity.md) in Play mode. Both use the same agent interface; run one instance at a time.

Use Python 3.10+. In `example/x2_agent/`, install third-party dependencies with `python -m pip install -r requirements.txt`, then run `python agent.py` or `python demo.py`. See the [agent guide](../../example/x2_agent/docs/en/README.md).

Each project owns its `requirements.txt`; `.env.example` and `.env` belong only to the voice Agent.
Agent configuration precedence is CLI arguments > existing environment variables > `example/x2_agent/.env` > defaults.
The console does not load `.env`; gateway signing parameters are entered in its UI. Imports do not load credentials.
The Agent accepts `--env-file <path>` for another configuration file.

## Module boundaries

`example/x2_agent/x2_agent/agent.py` owns sessions and concurrency. Its neighboring `asr.py`, `llm.py`, `tts.py` and `sentence_tts.py` own the transports. Unity messages belong in `gateway.py`; Doubao framing belongs in `speech_protocol.py`; audio and configuration belong in `audio.py` and `config.py`. `example/x2_agent/agent.py` and `example/x2_agent/demo.py` are entry scripts.

The separate console starts at `example/x2_console/main.py`. Its `x2_console/` package includes
only management and monitoring code, without importing the Agent project or cloud libraries.
Voice code belongs to the Agent. Update and test both distributions when changing their common gateway protocol.

Preserve cancellation, error propagation and connection cleanup when changing transports. Do not block the voice pipeline with synchronous network calls. Check both demo and production clients when changing gateway fields.

## Checks and tests

From the repository root, using a Python environment with project dependencies installed:

```powershell
cd example/x2_agent
python -B -m unittest discover -s tests -v
cd ../x2_console
python -B -m unittest discover -s tests -v
cd ../..
```

Tests use local mock services, with no Unity or API keys.

The Unity regression suite enters Play Mode and checks short audio, bounded input,
skill cancellation, multi-turn response gating, error/timeout recovery, concurrent shutdown,
WebSocket fragmentation/limits/slow-peer shutdown and log forwarding:

```powershell
# $unityEditor is the local 2022.3.62f3c1 Editor/Unity.exe
& $unityEditor -batchmode -projectPath "$PWD/unity-agent-playground" `
  -executeMethod AuditRegressionVerification.Run -logFile "$PWD/unity-regressions.log"
```

Close any editor using this project first. The check exits itself; do not add `-quit`.
Look for `AUDIT_REGRESSION_PASSED` and exit code zero. It intentionally emits warning,
error, assert and exception logs to verify forwarding, without cloud calls or a microphone.

It also reports `MULTI_SESSION_VERIFICATION_PASSED` for concurrent clients, fixed console priority,
editable other priorities, rejected commands, single-recipient audio, log broadcasting and handoff.

Validate manually in two stages:

1. **Local integration:** start one simulator, connect `demo.py`, confirm `state=online`, captions and audio playback, then test skills through the F1 panel.
2. **Cloud voice:** stop the demo, configure credentials and start `agent.py`. After the greeting, run several conversation turns. Check ASR text, the LLM reply, TTS playback and resumed listening on each turn, then test skills, explicit interruption and reconnection. A pending cloud reply must not be superseded by another recording.

Record the model, speech resources, results and per-stage timings. Separate recording and silence detection from cloud processing; a single measurement is not a performance guarantee.

## Unity component tests

Verify expression rendering with graphics enabled:

```powershell
& $unityEditor -batchmode -quit -projectPath "$PWD/unity-agent-playground" `
  -executeMethod EmotionVerification.Run -logFile "$PWD/emotion-verification.log"
```

Do not add `-nographics`. The success marker is `EMOTION_VERIFICATION_PASSED`.
Inspect `.diagnostics/emotions/atlas.png`: neutral, happy, sad, surprised,
angry, love from left to right; idle above, speaking below. Checks cover board
placement, rotation, dimensions, resolution, distinct silhouettes, preserved
eyes/brows during speech, and timed neutral reset.

For gesture changes, run the real-scene check:

```powershell
& $unityEditor -batchmode -projectPath "$PWD/unity-agent-playground" `
  -executeMethod GestureVerification.Run -logFile "$PWD/unity-gestures.log"
```

It checks wave/open playback, joint limits, target speed, standing stability, interruption,
replacement callbacks and episode reset. It exits itself; do not add `-quit`.
Look for `GESTURE_VERIFICATION_PASSED`. Front/oblique screenshots, sampled joint CSV and metrics
are written to `.diagnostics/gesture-polish/`. No microphone or cloud service is used.
Curves belong to `GestureMotion.cs`; smoothing, limits and lifecycle belong to `GesturePlayer.cs`.

## Building and releasing

The [Unity project](../../unity-agent-playground/) uses `2022.3.62f3c1`. The launcher, HUD and cameras are under `Assets/X02Competition/Bootstrap/`; regression checks are under `Assets/X02Competition/Tests/Editor/`. Build changes through Build Settings following the [Unity guide](unity.md), and keep the full output.

A Unity build produces the Player directory; packaging and release uploads are separate steps.

For a batch Windows x64 Player build:

```powershell
& $unityEditor -batchmode -quit -projectPath "$PWD/unity-agent-playground" `
  -executeMethod X2PlayerBuild.Windows -x2Output "$PWD/build/UnityEnvironment.exe" `
  -logFile "$PWD/unity-build.log"
```

`-x2Output` must be an absolute EXE path. Look for `X2_WINDOWS_BUILD_PASSED`,
then package the complete Player directory with `tools/unity-packager/pack-x2.ps1`.

For a release:

1. Name the asset `x2-simulator-windows-x64.exe` and ensure the `.sha256` file names that exact EXE.
2. Check that the EXE is below 100,000,000 bytes. Verify extraction into a fresh cache, startup, agent connections, skills and camera switching. Record the version and SHA-256.
3. Upload the EXE and checksum to the same GitHub Release.

`exe/`, `build/` and `release/` are ignored local directories. Keep release assets out of the source repository.

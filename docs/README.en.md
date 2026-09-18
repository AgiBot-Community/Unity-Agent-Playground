# Unity Agent Playground · X2

[中文](../README.md) | **English** | [Français](README.fr.md)

Talk to an X2 robot in Unity through a Python voice agent and request gestures, movement and facial expressions. This repository includes a portable Windows simulator, an installable Python example and gateway protocol documentation.

## Repository contents

| Path | Purpose |
|---|---|
| [exe/](simulator.en.md) | `x2模拟器.exe`, a 66.88 MB single-file Unity distribution |
| [example/](../example/docs/README.en.md) | The `x2_agent` Python package, configuration template and local tests |
| [docs/](index.en.md) | Documentation index, protocol and development guide in three languages |
| `scripts/check_docs.py` | Language coverage and local link checks |
| `.github/workflows/ci.yml` | Windows tests and documentation checks |

Unity is the WebSocket server. The Python agent receives microphone audio, calls ASR, LLM and TTS services, then sends text, audio and skill commands back. Start the simulator and agent separately.

The full Unity project is not included. The maintainer keeps packaging tools and frozen resources in a separate sibling directory, `../x2-simulator/`; it is not distributed with this repository or required to run the simulator.

## Quick start

Requirements: Windows 10/11 x64, Python 3.10+, a microphone and speakers. Real conversations also require Volcengine Speech and Ark API keys. The offline demo does not use cloud services.

1. Open [exe/x2模拟器.exe](../exe/x2模拟器.exe). Press **F1** to show the debug panel.
2. Open PowerShell at the repository root:

```powershell
cd example
python -m venv .venv
.\.venv\Scripts\python.exe -m pip install -e .
.\.venv\Scripts\python.exe -m x2_agent.demo
```

The demo returns fixed text and a sine-wave tone to check the gateway and audio path. It does not recognize speech or synthesize spoken replies.

3. Stop the demo with Ctrl+C, configure credentials and start Doubao:

```powershell
if (-not (Test-Path .env)) { Copy-Item .env.example .env }
# Edit .env: set DOUBAO_SPEECH_API_KEY and ARK_API_KEY
.\.venv\Scripts\python.exe -m x2_agent
```

Wait for the greeting to finish before speaking. Chinese examples include “你好” (hello), “挥挥手” (wave) and “往前走一米” (walk forward one metre). Only one agent can connect at a time. The current flow is half-duplex: speech detection pauses during playback, so speaking does not interrupt it; an explicit protocol command can interrupt playback.

Three documentation languages do not imply that speech services, the default Chinese voice or the Unity UI are localized into all three languages.

## Single-file distribution

Distribute only `exe/x2模拟器.exe` to run the simulator. It contains neither Python nor API keys. On first launch, resources are silently extracted to `%LOCALAPPDATA%\x2sim\` and reused later. There is no manual extraction or setup wizard. Allow at least 500 MB of free disk space.

One local measurement was about 12.6 seconds to open the window on first launch and 1.8 seconds later. Results vary by machine. Voice latency depends on silence detection, network conditions, the model and speech resources; no fixed latency is guaranteed.

## Development and validation

```powershell
# From the repository root
python -B scripts/check_docs.py
cd example
# Local tests; no cloud requests
.\.venv\Scripts\python.exe -B -m unittest discover -s tests -v
```

See the [development guide](development.en.md). Credentials, recordings, caches and local diagnostics are excluded from Git. The simulator EXE is an intentional distribution artifact.

## Documentation

| Need | Guide |
|---|---|
| Models, voices, greeting, options and troubleshooting | [Example agent](../example/docs/README.en.md) |
| Simulator startup, cache and repackaging | [Simulator](simulator.en.md) |
| Custom agents, authentication and messages | [Gateway protocol](interface.en.md) |
| All documentation languages | [Documentation index](index.en.md) |

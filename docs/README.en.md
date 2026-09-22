# X2 Agent Playground

[![AgiBot Community](https://img.shields.io/badge/Community-AgiBot-181717?style=flat-square&logo=github&logoColor=white)](https://github.com/AgiBot-Community)
[![GitHub Issues](https://img.shields.io/badge/Feedback-GitHub_Issues-238636?style=flat-square&logo=github&logoColor=white)](https://github.com/AgiBot-Community/Unity-Agent-Playground/issues)

![Unity 2022.3](https://img.shields.io/badge/Unity-2022.3-222222?style=flat-square&logo=unity&logoColor=white)
![C#](https://img.shields.io/badge/C%23-512BD4?style=flat-square&logo=dotnet&logoColor=white)
![Python 3.10+](https://img.shields.io/badge/Python-3.10%2B-3776AB?style=flat-square&logo=python&logoColor=white)

🌐 [中文 ↗](../README.md) | **English** | [Français ↗](README.fr.md)

Talk to an X2 robot in Unity through a Python voice agent and request gestures, movement and facial expressions. This repository includes a portable Windows simulator, Python example scripts and gateway protocol documentation.

## Repository contents

| Path | Purpose |
|---|---|
| [exe/](simulator.en.md) | `x2模拟器.exe` portable executable and its SHA-256 checksum |
| [unity-agent-playground/](unity.en.md) | Unity sources, robot models, skills and gateway implementation |
| [example/](../example/docs/README.en.md) | Python Agent scripts, configuration template and local tests |
| [docs/](index.en.md) | Documentation index, protocol and development guide in three languages |

Unity is the WebSocket server. The Python agent receives microphone audio, calls ASR, LLM and TTS services, then sends text, audio and skill commands back. Start the simulator and agent separately.

These guides use the executables, sources, configuration templates and documents maintained in Git. The Unity Editor version is recorded in [ProjectVersion.txt](../unity-agent-playground/ProjectSettings/ProjectVersion.txt), currently `2022.3.62f3c1`.

## Quick start

Choose one way to start the robot. The EXE needs neither Unity nor Python; the example Agent requires Python 3.10+. Voice interaction needs a microphone and speakers. Real conversations also require Volcengine Speech and Ark API keys.

### Path A: start from the EXE

1. On Windows 10/11 x64, open [exe/x2模拟器.exe](../exe/x2模拟器.exe) and wait for the robot window. Use the supplied [SHA-256 checksum](../exe/x2模拟器.sha256) to verify the download.
2. Press **F1** to show the debug panel. Skill buttons work without an Agent.
3. For voice interaction, continue to “Start the Agent” below. See the [EXE guide](simulator.en.md) for details.

### Path B: start from the Unity project

1. Install Unity Hub and Editor **2022.3.62f3c1**, following the [Unity guide](unity.en.md).
2. In Hub, choose **Add project from disk** and select [unity-agent-playground/](../unity-agent-playground/), which contains `Assets/`, `Packages/` and `ProjectSettings/`.
3. Wait for import and compilation, open `Assets/X02Competition/Scenes/scene.unity`, then press **Play**.
4. Focus the Game window and press **F1** to inspect the state. Continue below to start the Agent. Press Play again to stop the scene.

Both paths listen on `127.0.0.1:9002`; run only one simulator or Editor Play scene at a time. **C** cycles camera views; **F2–F5** select overview, front follow, side follow and free orbit. Free orbit supports right-button dragging and the mouse wheel.

### Start the Agent (both paths)

Keep the robot running and open PowerShell at the repository root:

```powershell
cd example
python -m pip install -r requirements.txt
python demo.py
```

The demo returns fixed text and plays bundled recordings to check the gateway and audio path. It does not recognize speech or synthesize replies in real time. Missing or invalid recordings fall back to a sine-wave tone.

`state=online` confirms the connection. Stop the demo with Ctrl+C, then use the repository's `.env.example` template in the same `example/` terminal to configure and start Doubao:

```powershell
if (-not (Test-Path .env)) { Copy-Item .env.example .env }
# Edit .env: set DOUBAO_SPEECH_API_KEY and ARK_API_KEY
python agent.py
```

Wait for the greeting to finish before speaking. Chinese examples include “你好” (hello), “挥挥手” (wave) and “往前走一米” (walk forward one metre). Only one agent can connect at a time. The current flow is half-duplex: speech detection pauses during playback, so speaking does not interrupt it; an explicit protocol command can interrupt playback.

Three documentation languages do not imply that speech services, the default Chinese voice or the Unity UI are localized into all three languages.

## Distribution and source builds

The repository's `exe/x2模拟器.exe` can be distributed alone; its checksum is in the adjacent `.sha256` file. The Python Agent is supplied separately in `example/`.

After changing Unity sources, use Build Settings as described in the [Unity guide](unity.en.md) and keep the complete output directory. Source edits do not automatically update the repository's portable EXE.

## Development and validation

```powershell
cd example
# Local tests; no cloud requests
python -B -m unittest discover -s tests -v
```

See the [development guide](development.en.md). [Git ignore rules](../.gitignore) and project ignore rules define the file scope. Configuration templates and the simulator EXE are repository deliverables; personal credentials must not be committed.

## Documentation

| Need | Guide |
|---|---|
| Install Unity Hub, add the standalone project, run scenes and build | [Unity getting started](unity.en.md) |
| Models, voices, greeting, options and troubleshooting | [Example agent](../example/docs/README.en.md) |
| Starting the EXE, verifying the download and switching views | [Simulator](simulator.en.md) |
| Custom agents, authentication and messages | [Gateway protocol](interface.en.md) |
| All documentation languages | [Documentation index](index.en.md) |

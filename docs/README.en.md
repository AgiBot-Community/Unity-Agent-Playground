<p align="center">
  <a href="https://github.com/AgiBot-Community">
    <img src="https://github.com/AgiBot-Community.png?size=304" alt="AgiBot Community logo" width="152">
  </a>
</p>

<h1 align="center">X2 Agent Playground</h1>

<p align="center">
  <a href="../README.md"><img src="https://img.shields.io/badge/语言-简体中文-22314E?style=for-the-badge" alt="简体中文"></a>
  <a href="README.en.md"><img src="https://img.shields.io/badge/Language-English-3776AB?style=for-the-badge" alt="English documentation"></a>
  <a href="README.fr.md"><img src="https://img.shields.io/badge/Langue-Français-0055A4?style=for-the-badge" alt="Documentation française"></a>
</p>

<p align="center">
  Talk to an X2 robot in Unity through a Python voice agent and request gestures, movement and facial expressions. This repository includes a portable Windows simulator, Python example scripts and gateway protocol documentation.
</p>

<p align="center">
  <a href="https://unity.com/releases/editor/archive"><img src="https://img.shields.io/badge/Unity-2022.3-222222?style=flat-square&amp;logo=unity&amp;logoColor=white" alt="Unity 2022.3"></a>
  <a href="https://learn.microsoft.com/dotnet/csharp/"><img src="https://img.shields.io/badge/C%23-512BD4?style=flat-square&amp;logo=dotnet&amp;logoColor=white" alt="C#"></a>
  <a href="https://www.python.org/"><img src="https://img.shields.io/badge/Python-3.10%2B-3776AB?style=flat-square&amp;logo=python&amp;logoColor=white" alt="Python 3.10+"></a>
</p>

<p align="center">
  <a href="https://github.com/AgiBot-Community"><img src="https://img.shields.io/badge/Community-AgiBot-181717?style=flat-square&amp;logo=github&amp;logoColor=white" alt="AgiBot Community"></a>
  <a href="https://github.com/AgiBot-Community/Unity-Agent-Playground/issues"><img src="https://img.shields.io/badge/Feedback-GitHub_Issues-238636?style=flat-square&amp;logo=github&amp;logoColor=white" alt="GitHub Issues"></a>
</p>

## Repository contents

| Path | Purpose |
|---|---|
| [exe/](simulator.en.md) | `x2模拟器.exe` portable executable and its SHA-256 checksum |
| [unity-agent-playground/](unity.en.md) | Unity sources, robot models, skills and gateway implementation |
| [example/](../example/docs/README.en.md) | Python Agent scripts, configuration template and local tests |
| [docs/](index.en.md) | Documentation index, protocol and development guide in three languages |

Unity captures microphone audio, displays the robot and executes skills as the WebSocket server. The Python agent receives that audio, calls speech recognition (ASR), a large language model (LLM) and speech synthesis (TTS), then returns text, audio and skill commands. Start the simulator first, then connect one agent.

Development uses Unity **2022.3.62f3c1**, recorded in [ProjectVersion.txt](../unity-agent-playground/ProjectSettings/ProjectVersion.txt). If Hub does not list this older release, follow the [Unity guide](unity.en.md) to download it from the official releases page and add it to Hub.

## Quick start

Choose a startup route for your task, then connect an agent:

| Task | Route | Requirements |
|---|---|---|
| Try the robot and test skills | Route A: portable EXE | Windows 10/11 x64 |
| Edit scenes, skills or the gateway and rebuild | Route B: Unity project | Unity Hub and the specified Editor |
| Check the Agent connection and audio playback | Run `demo.py` after starting the robot | Python 3.10+, microphone and speakers |
| Have voice conversations and request skills | Stop the demo, then run `agent.py` | Volcengine Speech and Ark API keys |

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

Tests use local mock services and require neither Unity nor API keys. See the [development guide](development.en.md) for module responsibilities, manual validation and releases.

## Documentation

| Need | Guide |
|---|---|
| Install Unity Hub, add the standalone project, run scenes and build | [Unity getting started](unity.en.md) |
| Models, voices, greeting, options and troubleshooting | [Example agent](../example/docs/README.en.md) |
| Starting the EXE, verifying the download and switching views | [Simulator](simulator.en.md) |
| Custom agents, authentication and messages | [Gateway protocol](interface.en.md) |
| All documentation languages | [Documentation index](index.en.md) |

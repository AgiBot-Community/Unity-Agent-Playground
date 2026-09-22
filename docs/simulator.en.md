# X2 simulator · portable executable

[中文](simulator.md) | **English** | [Français](simulator.fr.md)

Use this guide to run the portable Windows simulator. Open [x2模拟器.exe](../exe/x2模拟器.exe): the robot appears and its gateway waits on `127.0.0.1:9002`. Press **F1** to test skills with the debug buttons. Voice conversations require the separate [Python agent](../example/docs/README.en.md), which sends the greeting after connecting.

The file is **58,287,104 bytes (58.29 MB)**. Distribute this EXE alone: no Data folder, Python, credentials or Playground checkout is needed to run the simulator. Real voice conversations still require the separate agent. See the [SHA-256 checksum](../exe/x2模拟器.sha256).

## Runtime

| Item | Value |
|---|---|
| OS | Windows 10/11 x64, system .NET Framework 4.x |
| Voice devices | Microphone and speakers; skill buttons do not require a microphone |
| Local endpoint | `ws://127.0.0.1:9002/api/V1/open-portal/app/wss/agent-sdk` |
| Authentication | Clients send HMAC signatures; enforcement depends on the Unity build configuration |
| Sessions | One agent at a time |

Keep the window open during voice tests. If minimizing it causes audio or network problems, restore it before troubleshooting. Debug buttons can trigger skills without an agent. The current voice flow is half-duplex.

## Start from the EXE

1. Download the repository's [EXE](../exe/x2模拟器.exe) and [checksum](../exe/x2模拟器.sha256). Unity and Python are not required just to run the simulator.
2. Optionally run `Get-FileHash -LiteralPath 'exe/x2模拟器.exe' -Algorithm SHA256` at the repository root and compare the value with the checksum file.
3. Open the EXE, wait for the robot window, then press **F1** to view status or test skills.
4. To connect the example Agent, run from the repository root:

```powershell
python -m pip install -r example/requirements.txt
python example/demo.py
```

`state=online` confirms the connection. The demo uses fixed text and supplied audio without cloud services. For real conversations, configure credentials using `.env.example` as described in the [Agent guide](../example/docs/README.en.md), stop the demo, and run `python example/agent.py`. Close the window to stop the simulator; Ctrl+C stops the Agent.

## Camera views

Press **C** to cycle views, or use the camera buttons in the debug panel:

| Key | View | Behavior |
|---|---|---|
| F2 | Overview | Keeps the start and current position in view, zooming out as needed |
| F3 | Front follow (default) | Allows some visible movement before following; preserves a fixed world heading |
| F4 | Side follow | Shows gait, travel and turns from the side |
| F5 | Free orbit | Hold the right mouse button over the scene to orbit; use the wheel to change distance |

Every view frames the robot's full bounds and reserves space for the HUD. Orbit zoom cannot crop the robot. Follow views retain ground references and a small movement dead zone. Collapse the detailed panel with F1 for more viewing space in small windows.

## Start from the Unity project

Use the repository's [Unity project](../unity-agent-playground/) to change scenes, cameras, skills or the gateway:

1. Follow the [Unity guide](unity.en.md) to download Unity Hub and the older **2022.3.62f3c1** Editor, then add `unity-agent-playground/` with **Add project from disk**.
2. After import, open `Assets/X02Competition/Scenes/scene.unity`, press **Play** and focus the Game window.
3. Use the same shortcuts and Agent commands above. Close the EXE before Play to avoid a port 9002 conflict.
4. Follow the [Unity build guide](unity.en.md) using **File → Build Settings** to generate a Windows application; keep its complete output directory.

The supplied single-file EXE is ready to run and distribute. A normal Unity Build creates a directory containing the application and its resources; keep that entire directory when distributing your build. Building does not automatically replace the repository EXE.

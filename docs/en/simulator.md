# X2 simulator · portable executable

[中文](../zh-CN/simulator.md) | **English** | [Français](../fr/simulator.md)

The Windows simulator is distributed as a single EXE. It displays the robot and listens on `127.0.0.1:9002`. Press **F1** to test skills with the debug buttons. Voice conversations require the separate [Python agent](../../example/x2_agent/docs/en/README.md).

On first launch, Unity resources are extracted under `%LOCALAPPDATA%\UnityPortable`; subsequent launches reuse the cache. The simulator runs without Unity or Python installed. Configure the agent's dependencies and credentials separately.

## Runtime

| Item | Value |
|---|---|
| OS | Windows 10/11 x64, system .NET Framework 4.x |
| Voice devices | Microphone and speakers; skill buttons do not require a microphone |
| Local endpoint | `ws://127.0.0.1:9002/api/V1/open-portal/app/wss/agent-sdk` |
| Authentication | Clients send HMAC signatures; enforcement depends on the Unity build configuration |
| Sessions | Eight by default; console priority is fixed highest, other priorities are adjustable |

Keep the window open during voice tests. If minimizing it causes audio or network problems, restore it before troubleshooting. Debug buttons can trigger skills without an agent. The current voice flow is half-duplex.

## Start from the EXE

1. Download the portable EXE and matching checksum from [GitHub Releases](https://github.com/AgiBot-Community/Unity-Agent-Playground/releases). Binaries are not included in the source repository. Unity and Python are not required just to run the simulator.
2. For the console launcher, save the EXE locally as `exe/x2模拟器.exe`. Run `Get-FileHash -LiteralPath 'exe/x2模拟器.exe' -Algorithm SHA256` at the repository root and compare with the downloaded checksum. You can also launch the portable EXE from another directory.
3. Open the EXE, wait for the robot window, then press **F1** to view status or test skills.
4. To connect the example Agent, run from the repository root:

```powershell
python -m pip install -r example/x2_agent/requirements.txt
python example/x2_agent/demo.py
```

`state=online` confirms the connection. The demo uses fixed text and supplied audio without cloud services. For real conversations, configure credentials using `.env.example` as described in the [Agent guide](../../example/x2_agent/docs/en/README.md), stop the demo, and run `python example/x2_agent/agent.py`. Close the window to stop the simulator; Ctrl+C stops the Agent.

## Camera views

Press **C** to cycle views, or use the camera buttons in the debug panel:

| Key | View | Behavior |
|---|---|---|
| F2 | Overview | Keeps the start and current position in view, zooming out as needed |
| F3 | Front follow (default) | Allows some visible movement before following; preserves a fixed world heading |
| F4 | Side follow | Shows gait, travel and turns from the side |
| F5 | Free orbit | Hold the right mouse button over the scene to orbit; use the wheel to change distance |

Every view frames the robot's full bounds and reserves space for the HUD. Orbit zoom cannot crop the robot. Follow views retain ground references and a small movement dead zone. Collapse the detailed panel with F1 for more viewing space in small windows.

## Editing and building

See the [Unity guide](unity.md) to change scenes, cameras, skills or the gateway. Editor Play mode uses the same shortcuts and agent commands. Close the portable EXE before entering Play to avoid a port conflict.

Unity Build produces a complete directory containing the application and its resources. Use the [portable packager](../../tools/unity-packager/README.md) to package that directory as a single EXE.

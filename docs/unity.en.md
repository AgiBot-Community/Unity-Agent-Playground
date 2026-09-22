# Unity project

[中文](unity.md) | **English** | [Français](unity.fr.md)

This is the complete path for starting from the Unity project. To try the ready-made application, use [Start from the EXE](simulator.en.md). Both paths connect to the same [Agent example](../example/docs/README.en.md).

[unity-agent-playground/](../unity-agent-playground/) contains the X2 robot models, Agent gateway, gestures, expressions and locomotion sources. The recorded editor version is **2022.3.62f3c1**. Use the project configuration and bundled local packages for URP, Sentis, ML-Agents and URDF Importer dependencies.

## Install Unity Hub and the Editor

The supplied `exe/x2模拟器.exe` runs without Unity Hub or the Editor. Install them to edit scenes, change sources or rebuild:

1. Download Unity Hub for your operating system from the [official download page](https://unity.com/download), install it and sign in. Activate an appropriate license under Settings → Licenses; eligible individuals can use Unity Personal.
2. Install **2022.3.62f3c1**, the exact version in `ProjectSettings/ProjectVersion.txt`. Check Installs → Install Editor, or find this release on the [Unity China releases page](https://unity.cn/releases). The `c1` release may not appear in the [global Editor archive](https://unity.com/releases/editor/archive). Do not substitute the latest Unity 6 or edit the version file to bypass the check.
3. For an Editor installed separately, use Installs → Locate and select its executable (`Editor/Unity.exe` on Windows). Installing Hub alone does not install the required Editor.
4. On Windows, the Editor includes support for running scenes and Windows Mono builds. IL2CPP builds need the corresponding Windows Build Support and C++ tools. For Linux builds, add **Linux Build Support** for the chosen scripting backend through Add modules. The repository retains Linux toolchain dependencies; these do not replace the Editor build module.

## Add the standalone project

Copy the entire directory to use it as a standalone project without the parent repository. The two bundled source packages, ML-Agents and URDF Importer, are embedded under `Packages/`; the first import still needs network access for registry dependencies. See the project's [standalone instructions](../unity-agent-playground/README.md).

Fully extract the repository before opening it:

```text
Unity-Agent-Playground/          Repository
└── unity-agent-playground/      Standalone Unity project to add in Hub
    ├── Assets/
    ├── Packages/
    └── ProjectSettings/
        └── ProjectVersion.txt
```

1. Choose **Projects → Add → Add project from disk** in Hub (called Open in some versions). Select the inner `unity-agent-playground/`, or the copied directory containing all three folders above.
2. Select Editor `2022.3.62f3c1` and open the project. Wait for dependency resolution, asset import and compilation. Keep all `.meta` files in `Assets/`, plus the complete `Packages/` and `ProjectSettings/` folders when copying.

**“No projects found. Select a folder that contains Unity projects.”** comes from the bulk **Import projects** entry, which scans child directories. Use Add project from disk as above, or select the project's parent directory for a bulk scan. Scanning the parent does not make it the Unity project.

## Run the scene and connect an Agent

1. Open `Assets/X02Competition/Scenes/scene.unity` in the Editor's Project panel. Resolve red errors in Window → General → Console before entering Play.
2. Press **Play**, focus the Game window, and press **F1** to toggle the debug panel. The gateway listens on `127.0.0.1:9002`. Close the portable EXE before running the Editor scene to avoid a port conflict.
3. To test the Agent connection, open a separate terminal at the repository root:

```powershell
python -m pip install -r example/requirements.txt
python example/demo.py
```

The demo needs no cloud credentials. For voice conversations, follow the [Agent setup guide](../example/docs/README.en.md), stop the demo with Ctrl+C, then run `python example/agent.py`. These are direct Python scripts; installing this repository as a Python package is unnecessary. If you copied only the Unity project, supply a separate compatible Agent client.

`state=online` in the Agent terminal confirms the connection. Press Play again to stop the scene and Ctrl+C to stop the Agent. Source changes do not automatically update the portable EXE.

**C** cycles views; **F2** selects overview, **F3** front follow, **F4** side follow and **F5** free orbit. In orbit mode, hold the right mouse button over the scene to rotate and use the wheel to change distance. All views frame the full robot. Camera buttons are also available in the F1 panel.

## Troubleshooting

| Symptom | Action |
|---|---|
| Hub finds no projects | Select the inner project with Add project from disk, or its parent for bulk Import projects. |
| Missing Editor | Install the full version `2022.3.62f3c1`, or register an existing installation with Installs → Locate. |
| Dependency resolution stalls or packages fail | Check network access and errors in Console / Window → Package Manager. Keep the manifests and embedded packages in `Packages/`. |
| No robot visible | Open the specified `scene.unity`, enter Play and view the Game window. |
| Gateway fails or port 9002 is busy | Stop another running EXE or Editor scene. Run one simulator and connect one Agent at a time. |
| Agent cannot connect | Confirm Play mode and gateway startup, then check the endpoint using the Agent guide. A remote client cannot reach another machine through `127.0.0.1`. |

## Source layout

| Path within the Unity project | Purpose |
|---|---|
| `Assets/X02Competition/Bootstrap/` | Launcher, debug panel and `RobotCameraRig.cs` camera framing |
| `Assets/X02Competition/Gateway/` | WebSocket service and sessions |
| `Assets/X02Competition/Protocol/` | Message protocol |
| `Assets/X02Competition/Robot/Skills/` | Gestures, expressions, locomotion and routing |
| `Assets/X02Competition/Scenes/` | Scene and skill configuration |
| `Assets/RobotModel/` | URDF, meshes and ML policies |
| `Assets/SceneEnvironment/` | Scene environment |

## Configure and build

Configure the port and `StrictAuth` on the scene's `CompetitionLauncher` component, implemented in `Assets/X02Competition/Bootstrap/CompetitionLauncher.cs`. The listen address is defined in `Assets/X02Competition/Gateway/LinkskyGatewayServer.cs` and defaults to loopback. Skills use the scene's `SkillCatalog.asset`; defaults are in `Assets/X02Competition/Robot/Skills/SkillCatalog.cs`. Rebuild the application after changes.

Stop Play, open `File → Build Settings`, use Add Open Scenes to add and enable the main scene, and remove scenes that should not ship. Select PC, Mac & Linux Standalone → Windows → x86_64 and use Switch Platform if necessary. Enable `Run In Background` in Player Settings, click Build and choose a dedicated empty output directory outside `Assets/`.

Run the EXE in the completed output directory and verify connections using the Agent commands above. Keep all output files for running and distribution. A normal Unity Build does not automatically create or replace the repository's single-file [EXE](../exe/x2模拟器.exe). For Linux, install the module above, switch the target and use a separate output directory.

See the [example](../example/docs/README.en.md) for Agent setup and the [protocol](interface.en.md) for custom clients.

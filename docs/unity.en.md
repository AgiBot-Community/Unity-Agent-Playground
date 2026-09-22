# Unity project

[中文](unity.md) | **English** | [Français](unity.fr.md)

Follow this guide to install the required Editor, import the project, run the robot scene and build a Windows application. To try the robot immediately, use the [portable simulator](simulator.en.md). Both routes support the same [example agent](../example/docs/README.en.md).

[unity-agent-playground/](../unity-agent-playground/) contains the X2 robot models, Agent gateway, gestures, expressions and locomotion sources. The recorded editor version is **2022.3.62f3c1**. Use the project configuration and bundled local packages for URP, Sentis, ML-Agents and URDF Importer dependencies.

## Install Unity Hub and the Editor

The supplied `exe/x2模拟器.exe` runs without Unity Hub or the Editor. Install them to edit scenes, change sources or rebuild:

1. Download Unity Hub for your operating system from the [official download page](https://unity.com/download), install it and sign in. Activate an appropriate license under Settings → Licenses; eligible individuals can use Unity Personal.
2. Install **2022.3.62f3c1**, matching `ProjectSettings/ProjectVersion.txt`. Hub's recommended list does not include every historical release; use “Download an older Editor” below if the version is missing.
3. For an Editor installed separately, use Installs → Locate and select its executable (`Editor/Unity.exe` on Windows). Installing Hub alone does not install the required Editor.
4. Choose modules for your build target as described below. Running this project's scene on Windows does not require Android, iOS or WebGL modules.

### Download an older Editor

**Use the China release from the Unity China website for this project.** The `c1` suffix is part of `2022.3.62f3c1`; the global `2022.3.62f3` release is a different installer.

1. Open the [Unity China releases page](https://unity.cn/releases), select the **2022** series and find `2022.3.62f3`. The listing title may omit `c1`, so also check its China download option. The required revision is `1623fc0bbb97`.
2. If the entry offers installation through Hub, select it, allow the browser to open Unity Hub, and confirm the version and modules. If that route is unavailable, Windows users can use the official [2022.3.62f3c1 Editor installer](https://download.unitychina.cn/download_unity/1623fc0bbb97/Windows64EditorInstaller/UnitySetup64.exe). On macOS or Linux, select the appropriate OS and architecture on the same releases page.
3. Run the standalone installer and note the installation directory. In Hub, choose **Installs → Locate** and select `Editor/Unity.exe` in that directory on Windows. Locate registers an existing Editor without downloading it again.
4. Confirm the full version **2022.3.62f3c1** in Hub before adding the project. Other Editor versions can remain installed alongside it.

**For other older global releases:** use **Installs → Install Editor → Archive → Download archive**, or open the [global Editor archive](https://unity.com/releases/editor/archive) directly. Filter for the release, select its **Install / Unity Hub** link and allow the browser to open Hub. Alternatively, choose a standalone installer for your OS from the download menu and register it with Locate. The global archive does not make the `c1` suffix optional for this project. Treat an Editor upgrade as a separate migration to validate; do not edit the version file to bypass version selection.

### Choose build modules

| Task | Required support |
|---|---|
| Run scenes on Windows or build Windows Mono applications | Included with the Windows Editor |
| Build Windows IL2CPP applications | Windows Build Support (IL2CPP) and the required C++ toolchain |
| Build for Linux | Linux Build Support matching the Mono / IL2CPP backend |

For Editors installed through Hub, use **Installs → Manage → Add modules**. Editors installed separately and registered with Locate usually lack this option; Locate does not convert them into Hub-managed installations. To manage modules through Hub, follow Unity's instructions to reinstall the required Editor through Hub. The project's Linux toolchain packages do not replace platform build modules.

Official sources checked online on 2026-09-23: [China releases](https://unity.cn/releases), [global archive](https://unity.com/releases/editor/archive), [archived installation and Locate](https://docs.unity.com/en-us/hub/add-editor), and [module management](https://docs.unity.com/en-us/hub/add-modules). The Windows installer URL was reachable; the installer was not downloaded or run for this documentation update.

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

1. Stop Play and open **File → Build Settings**. Use **Add Open Scenes** to add and enable the main scene, and remove scenes that should not start with the application.
2. Select **PC, Mac & Linux Standalone → Windows → x86_64**, then **Switch Platform** if necessary.
3. Enable `Run In Background` in Player Settings so the application can process Agent messages while unfocused.
4. Select **Build**, choose a dedicated empty output directory outside `Assets/`, and wait for completion.

Run the EXE in the completed output directory and verify connections using the Agent commands above. Keep all output files for running and distribution. A normal Unity Build does not automatically create or replace the repository's single-file [EXE](../exe/x2模拟器.exe). For Linux, install the module above, switch the target and use a separate output directory.

See the [example](../example/docs/README.en.md) for Agent setup and the [protocol](interface.en.md) for custom clients.

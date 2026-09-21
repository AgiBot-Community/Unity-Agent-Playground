# Unity project

[中文](unity.md) | **English** | [Français](unity.fr.md)

[unity-agent-playground/](../unity-agent-playground/) contains the X2 robot models, Agent gateway, gestures, expressions and locomotion sources. The recorded editor version is **2022.3.62f3c1**. Use the project configuration and bundled local packages for URP, Sentis, ML-Agents and URDF Importer dependencies.

## Open and run

In Unity Hub, choose Open and select `unity-agent-playground/`. Wait for import and compilation, open `Assets/X02Competition/Scenes/scene.unity`, then press Play. **F1** toggles the debug panel. Play Mode uses current sources; the portable EXE comes from an earlier build and may support different skills.

## Source layout

| Path within the Unity project | Purpose |
|---|---|
| `Assets/X02Competition/Bootstrap/` | Launcher and debug panel |
| `Assets/X02Competition/Gateway/` | WebSocket service and sessions |
| `Assets/X02Competition/Protocol/` | Message protocol |
| `Assets/X02Competition/Robot/Skills/` | Gestures, expressions, locomotion and routing |
| `Assets/X02Competition/Scenes/` | Scene and skill configuration |
| `Assets/RobotModel/` | URDF, meshes and ML policies |
| `Assets/SceneEnvironment/` | Scene environment |

## Configure and build

Gateway settings are in `Assets/X02Competition/Bootstrap/CompetitionLauncher.cs`. The default listener is `127.0.0.1:9002`. Rebuild after changing external listening or `StrictAuth`. Skills are configured in the scene’s `SkillCatalog.asset`; code defaults are in `Robot/Skills/SkillCatalog.cs`.

Select Windows x86_64 in `File → Build Settings`, enable `Run In Background` in Player Settings, and place the complete build in the repository’s `exe/` directory. Keep all Unity build files. See the [simulator guide](simulator.en.md) for single-file repackaging.

See the [example](../example/docs/README.en.md) for Agent setup and the [protocol](interface.en.md) for custom clients.

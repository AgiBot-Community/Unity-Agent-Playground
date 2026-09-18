# X2 simulator · portable executable

[中文](simulator.md) | **English** | [Français](simulator.fr.md)

Open [x2模拟器.exe](../exe/x2模拟器.exe) to start Unity and its robot gateway. **F1** toggles the debug panel. Start the [Python agent](../example/docs/README.en.md) separately; the agent supplies the greeting.

The file is **66,880,000 bytes (66.88 MB)**. Distribute this EXE alone: no Data folder, Python, credentials or Playground checkout is needed to run the simulator. Real voice conversations still require the separate agent.

## Runtime

| Item | Value |
|---|---|
| OS | Windows 10/11 x64, system .NET Framework 4.x |
| Audio | Microphone and speakers |
| Local endpoint | `ws://127.0.0.1:9002/api/V1/open-portal/app/wss/agent-sdk` |
| Authentication | Clients send HMAC signatures; enforcement depends on the Unity build configuration |
| Sessions | One agent at a time |

Keep the window open during voice tests. If minimizing it causes audio or network problems, restore it before troubleshooting. Debug buttons can trigger skills without an agent. The current voice flow is half-duplex.

## First launch and cache

No Unity installation, manual extraction or configuration wizard is required. The launcher silently extracts resources to `%LOCALAPPDATA%\x2sim\<version>` and reuses them. This is single-file distribution, not execution entirely from memory.

The cache occupies about 328 MiB; allow at least 500 MB for initial preparation. A local test took about 12.6 seconds to open the window initially and 1.8 seconds later. You can delete the cache after closing Unity; it will be rebuilt next time. Unity still writes its usual logs and preferences.

All 207 original build files are preserved, including the repaired debug-panel DLL.

## Repackaging

The maintainer's sibling directory `../x2-simulator/` contains the launcher source, 7-Zip tools, `unity-payload.7z`, checksums and build script. It is not part of this repository. From that directory:

```powershell
.\build-portable.ps1
```

Output: `dist/x2模拟器.exe`. Frozen resources are sufficient; the original Unity project is not required for this packaging step. To replace resources, pass `-Source 'path-to-complete-Unity-build'`. The script verifies the archive and enforces a size below 100,000,000 bytes.

Repackaging does not change Unity's listen address or authentication settings. To change them, obtain an updated Unity build and package that build. Sources are in [unity-project/](../unity-project/), using editor `2022.3.62f3c1`. This portable EXE has not been rebuilt from the current sources; its supported skills may differ.

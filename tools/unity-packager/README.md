# Unity Windows portable packager

Turn a **complete, already-built Windows x64 Unity player directory** into a
single distributable EXE. Python 3.10+ and the Windows .NET Framework x64 C#
compiler (`%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe`) are required
on the build machine. The end user needs Windows x64 and .NET Framework 4.5+.
This tool does not build the Unity project or bundle a separate Python agent.

```powershell
python tools/unity-packager/pack.py `
  --source "D:\build\Windows" `
  --player "UnityEnvironment.exe" `
  --output "D:\release\MySimulator.exe" `
  --icon "D:\assets\simulator.ico"
```

`--icon` is optional and changes the **outer EXE's** Explorer icon, not the
Unity player's window/taskbar icon. Set the latter in Unity Player Settings
and rebuild the Unity player. The output folder also receives a
`MySimulator.sha256` checksum file. There is no package size limit check.

For the X2 project, `assets/agibot-x2.ico` is the matching icon and
`pack-x2.ps1` passes it automatically:

```powershell
.\tools\unity-packager\pack-x2.ps1 -Source "D:\build\Windows" -Output "D:\release\x2模拟器.exe"
```

The Unity project uses `Assets/Branding/agibot-x2-icon.png` for its Windows
Player icon. Open the project in Unity and rebuild it before packaging; the
Editor icon setup also runs just before a Windows Player build. An already
built `UnityEnvironment.exe` keeps its old icon until rebuilt.

On first run the launcher extracts into
`%LOCALAPPDATA%\UnityPortable\<payload SHA-256 prefix>`, checks each file
against the build-time manifest, and launches the Unity player with forwarded
arguments. Subsequent launches reuse the versioned cache. A new Unity build
produces a new cache directory; old caches may be removed when the player is
closed. To prepare the cache without starting Unity, run the packaged EXE with
`--portable-extract-only`. This is single-file *distribution*, not in-memory
execution. `UNITY_PORTABLE_CACHE` can override the cache parent directory
when an isolated cache is needed.

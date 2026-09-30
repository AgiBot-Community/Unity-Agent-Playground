# Unity Windows portable packager

Packages a complete Windows x64 Unity player directory as a single distributable EXE.
Build the player in Unity before running this tool.

## Requirements

Python 3.10+ and the Windows .NET Framework x64 C#
compiler (`%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe`) are required
on the build machine. The end user needs Windows x64 and .NET Framework 4.5+.
This tool does not build the Unity project or bundle a separate Python agent.

## Package a build

Run from the repository root. Replace the paths with your build directory and desired output:

```powershell
python tools/unity-packager/pack.py `
  --source ".\build\Windows" `
  --player "UnityEnvironment.exe" `
  --output ".\release\MySimulator.exe" `
  --icon ".\tools\unity-packager\assets\agibot-x2.ico"
```

`--icon` is optional and changes the **outer EXE's** Explorer icon, not the
Unity player's window/taskbar icon. Set the latter in Unity Player Settings
and rebuild the Unity player. The output folder also receives a
`MySimulator.sha256` checksum file.

Packaging fails if the EXE reaches 100,000,000 bytes. The check uses decimal
megabytes, so the default limit is stricter than 100 MiB.

### X2 builds

For the X2 project, `assets/agibot-x2.ico` is the matching icon and
`pack-x2.ps1` passes it automatically:

```powershell
.\tools\unity-packager\pack-x2.ps1 -Source ".\build\Windows" -Output ".\release\x2-simulator-windows-x64.exe"
```

The Unity project uses `Assets/Branding/agibot-x2-icon.png` for its Windows
Player icon. Open the project in Unity and rebuild it before packaging; the
Editor icon setup also runs just before a Windows Player build. An already
built `UnityEnvironment.exe` keeps its old icon until rebuilt.

## Extraction and cache

The payload uses solid LZMA compression (preset 9 extreme, 64 MiB dictionary).
The bundled public-domain C# decoder extracts it without an installed 7-Zip.
First extraction needs space for both the uncompressed archive and extracted build;
the temporary archive is removed after extraction.

On first run the launcher extracts into
`%LOCALAPPDATA%\UnityPortable\<payload SHA-256 prefix>`, checks each file
against the build-time manifest, and launches the Unity player with forwarded
arguments. Subsequent launches reuse the versioned cache. A new Unity build
produces a new cache directory; old caches may be removed when the player is
closed. To prepare the cache without starting Unity, run the packaged EXE with
`--portable-extract-only`. The player runs from the extracted files.
`UNITY_PORTABLE_CACHE` can override the cache parent directory
when an isolated cache is needed.

## Release files

Publish the EXE and its matching checksum as assets of the same GitHub
Release. Build output belongs in ignored `build/` and `release/` directories;
downloaded copies may be placed in ignored `exe/` for the Python console.
Do not commit these binaries or checksums to the source repository.

Use the ASCII asset name `x2-simulator-windows-x64.exe` on GitHub Releases:
GitHub may remove non-ASCII characters from uploaded asset names. The released
`.sha256` must name that exact file. After verification, users may rename the
download to `exe/x2模拟器.exe` for the console launcher.

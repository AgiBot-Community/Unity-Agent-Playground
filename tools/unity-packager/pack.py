#!/usr/bin/env python3
"""Wrap an existing Windows x64 Unity build in a portable single EXE."""

import argparse
import hashlib
import json
import os
from pathlib import Path
import subprocess
import tempfile
import zipfile


def sha256(path):
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def collect(source):
    paths = sorted(source.rglob("*"), key=lambda p: p.relative_to(source).as_posix())
    for path in paths:
        if path.is_symlink():
            raise ValueError(f"Symlinks are not supported: {path}")
        relative = path.relative_to(source).as_posix()
        if any(ord(char) < 32 for char in relative):
            raise ValueError(f"Control characters in build path: {path}")
        if relative.lower() == ".complete":
            raise ValueError("Build directory contains reserved file: .complete")
    return [path for path in paths if path.is_file()]


def validate(source, player, output, icon):
    if os.name != "nt":
        raise ValueError("This tool requires Windows")
    if not source.is_dir():
        raise ValueError(f"Build directory does not exist: {source}")
    if player.name != str(player) or player.suffix.lower() != ".exe":
        raise ValueError("--player must be a top-level .exe name")
    if not (source / player).is_file():
        raise ValueError(f"Player not found: {player}")
    if not (source / (player.stem + "_Data")).is_dir():
        raise ValueError(f"Missing Unity data directory: {player.stem}_Data")
    if not (source / (player.stem + "_Data") / "globalgamemanagers").is_file():
        raise ValueError("Unity data directory is missing globalgamemanagers")
    if output.suffix.lower() != ".exe":
        raise ValueError("--output must be an .exe path")
    if output == source or source in output.parents:
        raise ValueError("Output must be outside the Unity build directory")
    if icon and (not icon.is_file() or icon.suffix.lower() != ".ico"):
        raise ValueError("--icon must point to an existing .ico file")


def build(source, player, output, icon=None):
    source = source.resolve()
    output = output.resolve()
    icon = icon.resolve() if icon else None
    validate(source, player, output, icon)
    paths = collect(source)
    compiler = Path(os.environ["WINDIR"]) / "Microsoft.NET/Framework64/v4.0.30319/csc.exe"
    if not compiler.is_file():
        raise ValueError(f".NET Framework C# compiler not found: {compiler}")
    output.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(dir=output.parent) as tmp:
        temp = Path(tmp)
        payload = temp / "payload.zip"
        manifest = temp / "manifest.tsv"
        info = temp / "BuildInfo.cs"
        with zipfile.ZipFile(payload, "w", zipfile.ZIP_DEFLATED, compresslevel=9,
                             allowZip64=True) as archive:
            for path in paths:
                archive.write(path, path.relative_to(source).as_posix())
        manifest.write_text("".join(
            f"{sha256(path)}\t{path.relative_to(source).as_posix()}\n"
            for path in paths
        ), encoding="utf-8")
        info.write_text(
            "internal static class BuildInfo {\n"
            f" internal const string Version = {json.dumps(sha256(payload))};\n"
            f" internal const string Player = {json.dumps(player.name)};\n"
            f" internal const string Title = {json.dumps(output.stem, ensure_ascii=True)};\n"
            "}\n", encoding="ascii"
        )
        command = [
            str(compiler), "/nologo", "/target:winexe", "/platform:x64", "/optimize+",
            "/r:System.Windows.Forms.dll", "/r:System.IO.Compression.dll",
            f"/out:{output}", f"/resource:{payload},payload",
            f"/resource:{manifest},manifest",
            str(Path(__file__).with_name("PortableLauncher.cs")), str(info),
        ]
        if icon:
            command.append(f"/win32icon:{icon}")
        subprocess.run(command, check=True)
    checksum = sha256(output)
    output.with_suffix(".sha256").write_text(
        f"{checksum}  {output.name}\n", encoding="utf-8"
    )
    return checksum


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", required=True, type=Path, help="complete Unity build directory")
    parser.add_argument("--player", required=True, type=Path, help="Unity .exe name within the build")
    parser.add_argument("--output", required=True, type=Path, help="portable .exe to create")
    parser.add_argument("--icon", type=Path, help="optional icon (.ico) for the outer EXE")
    args = parser.parse_args(argv)
    try:
        checksum = build(args.source, args.player, args.output, args.icon)
        print(f"Built {args.output} ({args.output.stat().st_size} bytes, SHA-256 {checksum})")
    except (ValueError, OSError, subprocess.CalledProcessError) as error:
        parser.exit(1, f"error: {error}\n")


if __name__ == "__main__":
    main()

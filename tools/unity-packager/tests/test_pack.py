import importlib.util
import os
from pathlib import Path
import subprocess
import struct
import tempfile
import unittest


SCRIPT = Path(__file__).resolve().parents[1] / "pack.py"
spec = importlib.util.spec_from_file_location("unity_packager", SCRIPT)
pack = importlib.util.module_from_spec(spec)
spec.loader.exec_module(pack)


class PackagerTests(unittest.TestCase):
    def setUp(self):
        scratch = SCRIPT.parents[2] / ".diagnostics"
        scratch.mkdir(exist_ok=True)
        self.temp = tempfile.TemporaryDirectory(dir=scratch)
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.source = self.root / "build"
        self.source.mkdir()
        (self.source / "Robot.exe").write_bytes(b"stub")
        data = self.source / "Robot_Data"
        data.mkdir()
        (data / "globalgamemanagers").write_bytes(b"unity data")
        self.output = self.root / "release" / "RobotPortable.exe"

    def test_accepts_complete_build(self):
        pack.validate(self.source, Path("Robot.exe"), self.output, None)
        self.assertEqual(len(pack.collect(self.source)), 2)

    def test_rejects_output_inside_source(self):
        with self.assertRaisesRegex(ValueError, "outside"):
            pack.validate(self.source, Path("Robot.exe"), self.source / "release.exe", None)

    def test_rejects_missing_data(self):
        (self.source / "Robot_Data" / "globalgamemanagers").unlink()
        with self.assertRaisesRegex(ValueError, "globalgamemanagers"):
            pack.validate(self.source, Path("Robot.exe"), self.output, None)

    def test_rejects_bad_player_path(self):
        with self.assertRaisesRegex(ValueError, "top-level"):
            pack.validate(self.source, Path("../Robot.exe"), self.output, None)

    def test_builds_single_exe(self):
        checksum = pack.build(self.source, Path("Robot.exe"), self.output)
        self.assertEqual(checksum, pack.sha256(self.output))
        self.assertEqual(
            self.output.with_suffix(".sha256").read_text(encoding="utf-8"),
            f"{checksum}  {self.output.name}\n",
        )
        self.assertGreater(self.output.stat().st_size, 0)
        cache = self.root / "cache"
        cache.mkdir()
        env = dict(os.environ, UNITY_PORTABLE_CACHE=str(cache))
        subprocess.run([str(self.output), "--portable-extract-only"],
                       env=env, check=True, timeout=30)
        extracted = list(cache.glob("*/Robot.exe"))
        self.assertEqual(len(extracted), 1)
        self.assertEqual(extracted[0].read_bytes(), b"stub")

    def test_builds_with_icon(self):
        icon = self.root / "app.ico"
        width = height = 16
        pixels = b"\x00\x80\xff\xff" * (width * height)
        mask = b"\x00" * (height * 4)
        bitmap = struct.pack("<IiiHHIIiiII", 40, width, height * 2, 1, 32,
                             0, len(pixels), 0, 0, 0, 0) + pixels + mask
        icon.write_bytes(
            struct.pack("<HHH", 0, 1, 1)
            + struct.pack("<BBBBHHII", width, height, 0, 0, 1, 32,
                          len(bitmap), 22)
            + bitmap
        )
        pack.build(self.source, Path("Robot.exe"), self.output, icon)
        self.assertTrue(self.output.is_file())

    def test_builds_with_x2_icon(self):
        icon = SCRIPT.parent / "assets" / "agibot-x2.ico"
        pack.build(self.source, Path("Robot.exe"), self.output, icon)
        self.assertTrue(self.output.is_file())

    def test_builds_with_non_ascii_output_name(self):
        self.output = self.output.with_name("x2模拟器.exe")
        checksum = pack.build(self.source, Path("Robot.exe"), self.output)
        self.assertEqual(
            self.output.with_suffix(".sha256").read_text(encoding="utf-8"),
            f"{checksum}  {self.output.name}\n",
        )


if __name__ == "__main__":
    unittest.main()

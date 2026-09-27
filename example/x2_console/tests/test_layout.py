"""Verify console entry points and resource paths after separating the package."""
from pathlib import Path
import os
import shutil
import subprocess
import sys
import tempfile
import unittest

CONSOLE_ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(CONSOLE_ROOT))

from x2_console import PROJECT_ROOT, REPOSITORY_ROOT
from x2_console import client, ui
from x2_console import gateway


class ConsoleLayoutTests(unittest.TestCase):
    def test_entry_runs_from_an_unrelated_working_directory(self):
        with tempfile.TemporaryDirectory() as folder:
            result = subprocess.run(
                [sys.executable, "-B", str(CONSOLE_ROOT / "main.py"), "--help"],
                cwd=folder, capture_output=True, timeout=20)
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertIn(b"--host", result.stdout)
        self.assertIn(b"--port", result.stdout)

    def test_package_entry_runs_from_console_directory(self):
        result = subprocess.run([sys.executable, "-B", "-m", "x2_console", "--help"],
                                cwd=CONSOLE_ROOT, capture_output=True, timeout=20)
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertIn(b"usage:", result.stdout.lower())

    def test_local_protocol_and_repository_resources_resolve(self):
        self.assertEqual(PROJECT_ROOT, CONSOLE_ROOT)
        self.assertEqual(ui.ROOT, REPOSITORY_ROOT)
        self.assertTrue((ui.ROOT / "exe/x2模拟器.exe").is_file())
        self.assertTrue((ui.ROOT / "tools/unity-packager/assets/agibot-x2.ico").is_file())
        self.assertIs(client.T, gateway.T)
        self.assertIs(client.build_headers, gateway.build_headers)

    def test_console_runs_without_sibling_agent_project(self):
        def ignore(_folder, names):
            return [name for name in names if name == "__pycache__" or
                    (name.startswith(".env") and name != ".env.example")]
        with tempfile.TemporaryDirectory() as folder:
            isolated = Path(folder) / "standalone-console"
            shutil.copytree(CONSOLE_ROOT, isolated, ignore=ignore)
            (isolated / ".env").write_text("X2_CONSOLE_TEST_MARKER=local\n", encoding="utf-8")
            environment = dict(os.environ)
            environment.pop("PYTHONPATH", None)
            help_result = subprocess.run(
                [sys.executable, "-B", str(isolated / "main.py"), "--help"],
                cwd=folder, env=environment, capture_output=True, timeout=20)
            self.assertEqual(help_result.returncode, 0, help_result.stderr)
            script = (
                "import sys,os; "
                "from x2_console.client import ConsoleClient,ConnectionSettings; "
                "from x2_console import PROJECT_ROOT; "
                "assert 'X2_CONSOLE_TEST_MARKER' not in os.environ; "
                "assert 'x2_agent' not in sys.modules; "
                "assert 'httpx' not in sys.modules; "
                "assert not (PROJECT_ROOT/'x2_console'/'voice').exists(); "
                "assert not hasattr(ConsoleClient,'_make_agent'); "
                "assert PROJECT_ROOT.name=='standalone-console'"
            )
            subprocess.run([sys.executable, "-B", "-c", script],
                           cwd=isolated, env=environment, check=True, timeout=20)

    def test_import_does_not_load_credentials(self):
        script = (
            "import os; "
            "os.environ.pop('ARK_API_KEY', None); "
            "os.environ.pop('DOUBAO_SPEECH_API_KEY', None); "
            "import x2_console.client; "
            "assert 'ARK_API_KEY' not in os.environ; "
            "assert 'DOUBAO_SPEECH_API_KEY' not in os.environ"
        )
        subprocess.run([sys.executable, "-B", "-c", script],
                       cwd=CONSOLE_ROOT, check=True, timeout=20)

    def test_management_import_has_no_cloud_dependency(self):
        script = """
import importlib.abc
import sys
class BlockCloud(importlib.abc.MetaPathFinder):
    def find_spec(self, fullname, path=None, target=None):
        if fullname == 'httpx' or fullname == 'x2_agent' or fullname.startswith('x2_console.voice'):
            raise AssertionError('Unexpected cloud dependency: ' + fullname)
sys.meta_path.insert(0, BlockCloud())
from x2_console import client, ui
worker = client.ConsoleClient()
worker.close()
worker._thread.join(3)
assert worker.stopped
"""
        subprocess.run([sys.executable, "-B", "-c", script], cwd=CONSOLE_ROOT, check=True, timeout=20)

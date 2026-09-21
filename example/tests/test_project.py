"""Project boundaries introduced by package extraction."""
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch

from x2_agent.config import load_env
from x2_agent import agent, demo, gateway


class ProjectTests(unittest.TestCase):
    def test_explicit_config_preserves_environment_and_handles_bom(self):
        with tempfile.TemporaryDirectory() as folder:
            config = Path(folder) / 'settings.env'
            config.write_text('# comment\nEXISTING=from-file\nNEW="loaded"\n', encoding='utf-8-sig')
            with patch.dict(os.environ, {'EXISTING': 'from-shell'}, clear=True):
                load_env(config)
                self.assertEqual(os.environ['EXISTING'], 'from-shell')
                self.assertEqual(os.environ['NEW'], 'loaded')

    def test_missing_explicit_config_is_reported(self):
        with tempfile.TemporaryDirectory() as folder:
            with self.assertRaises(FileNotFoundError):
                load_env(Path(folder) / 'missing.env')

    def test_import_does_not_load_local_credentials(self):
        script = (
            "import os; "
            "os.environ.pop('ARK_API_KEY', None); "
            "os.environ.pop('DOUBAO_SPEECH_API_KEY', None); "
            "import x2_agent.agent; "
            "assert 'ARK_API_KEY' not in os.environ; "
            "assert 'DOUBAO_SPEECH_API_KEY' not in os.environ"
        )
        subprocess.run([sys.executable, '-B', '-c', script], check=True,
                       cwd=Path(__file__).resolve().parents[1], timeout=20)

    def test_clients_share_gateway_protocol(self):
        self.assertIs(agent.build_headers, gateway.build_headers)
        self.assertIs(demo.build_headers, gateway.build_headers)
        self.assertIs(agent.envelope, demo.envelope)
        self.assertIs(agent.T, demo.T)

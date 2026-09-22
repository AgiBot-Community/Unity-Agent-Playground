"""Load local credentials at CLI startup, never as an import side effect."""
import os
from pathlib import Path


def load_env(path=None):
    """Existing environment variables win; explicit paths must exist.

    Default to example/.env beside the entry scripts, regardless of working directory.
    """
    if path is not None:
        env_path = Path(path).expanduser().resolve(strict=True)
    else:
        env_path = Path(__file__).resolve().parent.parent / '.env'
        if not env_path.is_file():
            return
    for line in env_path.read_text(encoding='utf-8-sig').splitlines():
        line = line.strip()
        if not line or line.startswith('#') or '=' not in line:
            continue
        key, _, value = line.partition('=')
        key, value = key.strip(), value.strip().strip("'\"")
        if key:
            os.environ.setdefault(key, value)

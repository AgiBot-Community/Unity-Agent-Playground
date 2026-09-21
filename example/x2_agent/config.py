"""Load local credentials at CLI startup, never as an import side effect."""
import os
from pathlib import Path


def load_env(path=None):
    """Existing environment variables win; explicit paths must exist.

    Source checkouts use their project-root .env regardless of working directory.
    Installed distributions use the current directory, unless --env-file is set.
    """
    if path is not None:
        env_path = Path(path).expanduser().resolve(strict=True)
    else:
        project = Path(__file__).resolve().parent.parent
        base = project if (project / 'pyproject.toml').is_file() else Path.cwd()
        env_path = base / '.env'
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

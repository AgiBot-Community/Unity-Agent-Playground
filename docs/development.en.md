# Development and maintenance

[中文](development.md) | **English** | [Français](development.fr.md)

## Environment and entry points

Create a Python 3.10+ environment in `example/` and run `python -m pip install -e .`. Start with `python -m x2_agent` or `python -m x2_agent.demo`; no client EXE is generated. See the [agent guide](../example/docs/README.en.md).

Declare dependencies only in `pyproject.toml`. Configuration precedence is CLI, existing environment, `.env`, then defaults. Imports do not load credentials. Use `--env-file` for an explicit configuration path.

## Module boundaries

`agent.py` owns sessions and concurrency. `asr.py`, `llm.py`, `tts.py` and `sentence_tts.py` own their respective transports. Unity messages belong in `gateway.py`; Doubao binary framing belongs in `speech_protocol.py`. WAV utilities and configuration belong in `audio.py` and `config.py`.

Preserve cancellation, error propagation and connection cleanup when changing transports. Do not block the voice pipeline with synchronous network calls. Check both demo and production clients when changing gateway fields.

## Checks and tests

From the repository root, using a Python environment with project dependencies installed:

```powershell
python -B scripts/check_docs.py
cd example
python -B -m unittest discover -s tests -v
```

Tests use local mock services, with no Unity or API keys. CI is configured to run these checks on Windows with Python 3.10 and 3.12. A configured workflow is not evidence that remote CI has already passed.

Manual end-to-end validation: start Unity → connect one agent → wait for the greeting → speak → confirm ASR text, LLM text and audio → trigger a skill → disconnect and reconnect. Record model, speech resources and per-stage timings; do not turn a single measurement into a guarantee.

## Changes and documentation

- Cover behavior changes with local mock tests; check relative links and language coverage for documentation changes.
- Chinese files have no suffix; English uses `.en.md`, French `.fr.md`. Keep technical identifiers unchanged.
- Exclude `.env`, recordings, logs, environments, caches and `.diagnostics/`. Do not paste credential-bearing logs into issues or PRs.
- `exe/x2模拟器.exe` is a deliberate release artifact; do not ignore all `*.exe` files. Record size, SHA-256 and startup validation when replacing it.
- `build/`, `dist/` and `*.egg-info/` are generated. Remove only confirmed generated directories; preserve configuration and source.
- Describe the problem, resulting behavior and validation in PRs. State when Unity or cloud testing was not performed.

## Unity and distribution boundaries

The Unity project is in [unity-project/](../unity-project/), using editor `2022.3.62f3c1`. Rebuild Unity after changing the gateway or skills; the portable EXE does not update automatically. The maintainer's `../x2-simulator/` contains separate packaging materials that are not available in a normal clone. The launcher uses a disk cache, so do not describe it as memory-only execution. See the [simulator guide](simulator.en.md).

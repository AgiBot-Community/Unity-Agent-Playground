# Contributing

[中文](../../CONTRIBUTING.md) | **English** | [Français](../fr/CONTRIBUTING.md)

Submit fixes, features, documentation and translations through pull requests. Start with the [README](README.md) for installation and the [development guide](development.md) for modules and full test commands.

## Report a problem

Search existing [issues](https://github.com/AgiBot-Community/Unity-Agent-Playground/issues) before opening a new one. Include:

- Release version or commit, operating system, and Python / Unity versions.
- Minimal reproduction steps, expected behavior and actual behavior.
- Relevant logs; screenshots or short videos help with UI and motion problems.
- Model, voice and resource IDs for speech issues. Remove API keys, signatures and private conversations from logs.

For feature requests, describe the use case, the current limitation and the expected result. Discuss substantial protocol, dependency or layout changes in an issue before implementation.

## Submit a change

1. Fork the repository and create a working branch from the target branch.
2. Keep each PR focused on one problem, with the relevant tests or documentation.
3. Run checks for the affected components and report the results. State which Unity or cloud tests were not run.
4. Describe the problem, resulting behavior and verification steps in the PR. Link related issues.

Python client tests use local mock services and require neither Unity nor cloud keys. With the relevant dependencies installed, run from the repository root:

```powershell
cd example/x2_agent
python -B -m unittest discover -s tests -v
cd ../x2_console
python -B -m unittest discover -s tests -v
cd ../..
```

Check both the agent and console when changing the gateway protocol. For Unity skills, audio or sessions, run the relevant checks in the [development guide](development.md).

## Documentation and translations

The README introduces the project and its shortest startup sequence. User guides cover operations, the protocol reference defines messages and behavior, and the development guide covers testing and releases. Add new content to the appropriate page and link it from the [documentation index](../README.md).

General guides live in `docs/zh-CN/`, `docs/en/` and `docs/fr/`, with matching filenames across languages. The Chinese project README and CONTRIBUTING remain at the repository root; translations live in their language directories. Agent guides use the same language directory structure under `example/x2_agent/docs/`. Update corresponding translations when behavior or options change. Keep commands, protocol fields, model IDs and filenames unchanged.

Describe concrete steps, defaults and results. Keep personal work logs, temporary verification notes and machine-specific paths out of user documentation. Check relative links, heading anchors and command working directories before submitting.

## Files and licensing

Preserve Unity `.meta` files. Do not commit `.env`, credentials, logs, caches or build output; see [`.gitignore`](../../.gitignore). Publish portable executables and checksums through GitHub Releases as described in the [development guide](development.md).

Original project code uses [Apache License 2.0](../../LICENSE). Preserve license and source notices when adding third-party code or assets.

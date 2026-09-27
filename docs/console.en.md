# Python desktop console

[中文](console.md) | **English** | [Français](console.fr.md)

Run from the repository root:

```powershell
python -m pip install -r example/x2_console/requirements.txt
python example/x2_console/main.py
```

The console implementation and tests live in `example/x2_console/`. From that directory,
use `python main.py` or `python -m x2_console`. This is a management/monitoring client with
WebSocket as its only third-party dependency. It does not load `.env` or a voice engine.
Run console tests from the repository root with
`python -B -m unittest discover -s example/x2_console/tests -v`.

The native GUI uses Tkinter; no Qt or frontend build is needed. Python distributions without
Tk need their platform's Tk support. Start the simulator with the launch button or start Unity
manually, then connect to `127.0.0.1:9002`. Advanced settings contain the path and signing credentials;
credentials are kept in memory only.

The robot tab controls gestures, walking, turns, emotions and interruption after explicit takeover.
The sessions tab lists all clients and their control/voice ownership. Select a non-console client,
enter an integer from **0–999**, then apply and wait for the server's updated value.
The console stays at **1000** and cannot be edited. Eight connections are allowed by default.
Priorities reset on reconnect; earlier connections win ties.

Connections start in monitoring mode, without preempting Agent actions or microphone ownership.
The console keeps priority 1000, diagnostics and priority administration. Enable action takeover only
when manual operation is needed; release it for the separate voice Agent. The managing console's Stop button
reclaims control before interrupting. Restart the updated simulator if the checkbox is unavailable.
If speech promises a wave but nothing moves, check the action owner and error `4091`.

Voice conversations and cloud credentials belong only to `example/x2_agent`.
The console contains no ASR, LLM, TTS, greeting or recordings and explicitly opts out of audio.
Lower-priority clients cannot execute skills while a console explicitly owns action control.

Logs support severity/source filters, message and stack search, paused display, clearing and JSONL
export of the current filter. The last 1200 records are retained. Pausing display does not stop reception.
The monitoring panel shows gateway-reported power, network and latest skill status.
`Ctrl+L` focuses search; `Ctrl+.` stops speech and movement. Closing the window disconnects and
stops the network thread.

The semantic theme uses **Catppuccin Mocha** (`catppuccin/palette`, MIT): dark neutral surfaces,
Teal primary actions, Yellow warnings and Red errors. Reference and tokens are in
[`theme.py`](../example/x2_console/x2_console/theme.py). Tk uses native styles rather than loading CSS
or remote fonts; keyboard focus and high-DPI scaling are supported.

See the [protocol](interface.en.md) for arbitration and the [development guide](development.en.md)
for local Python and real Unity regression checks.

# Python desktop console

[中文](../zh-CN/console.md) | **English** | [Français](../fr/console.md)

The console provides manual robot controls, session priority management and logs. Voice conversations run in the separate [agent](../../example/x2_agent/docs/en/README.md).

## Installation and startup

Run from the repository root:

```powershell
python -m pip install -r example/x2_console/requirements.txt
python example/x2_console/main.py
```

The implementation and tests live in `example/x2_console/`. From that directory, use `python main.py` or `python -m x2_console`. The directory can run independently; `websockets` is its third-party dependency. The GUI uses Tkinter. Install your platform's Tk support if it is absent from your Python distribution.

The launch button reads `exe/x2模拟器.exe` within the repository. Download and verify the EXE using the [simulator guide](simulator.md), then save it at that path. When using a separate copy of the console directory, start the simulator manually.

## Connection and controls

Start the simulator, then connect to `127.0.0.1:9002`. Advanced settings contain the path and signing credentials, which are kept in memory only. The console does not load `.env`.

The robot tab controls gestures, walking, turns, emotions and interruption after explicit takeover.
The sessions tab lists all clients and their control/voice ownership. Select a non-console client,
enter an integer from **0–999**, then apply and wait for the server's updated value.
The console stays at **1000** and cannot be edited. Eight connections are allowed by default.
Priorities reset on reconnect; earlier connections win ties.

Connections start in monitoring mode, without preempting Agent actions or microphone ownership.
The console keeps priority 1000, diagnostics and priority administration. Enable action takeover only
when manual operation is needed; release it for the separate voice Agent. The managing console's Stop button
reclaims control before interrupting. If the checkbox is unsupported, update and restart the simulator as prompted.
If a voice request produces no action, check the action owner and error `4091` (insufficient authority).

Voice conversations and cloud credentials belong only to `example/x2_agent`.
The console explicitly disables microphone reception in its handshake.
Lower-priority clients cannot execute skills while a console explicitly owns action control.

## Logs and shortcuts

Logs support severity/source filters, message and stack search, paused display, clearing and JSONL
export of the current filter. The last 1200 records are retained. Pausing display does not stop reception.
The monitoring panel shows gateway-reported power, network and latest skill status.
`Ctrl+L` focuses search; `Ctrl+.` stops speech and movement. Closing the window disconnects and
stops the network thread.

## Tests and theme

Run tests from the repository root:

```powershell
python -B -m unittest discover -s example/x2_console/tests -v
```

The theme uses **Catppuccin Mocha** (MIT). Color definitions and attribution are in [`theme.py`](../../example/x2_console/x2_console/theme.py).

See the [protocol](interface.md) for arbitration and the [development guide](development.md)
for local Python and real Unity regression checks.

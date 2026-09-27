"""Native Tk desktop console. Widgets are accessed only on Tk's main thread."""
import argparse
import json
import os
import subprocess
import tkinter as tk
from tkinter import filedialog, ttk

from .client import ConnectionSettings, ConsoleClient
from .model import LogHistory, display_time
from .theme import COLORS
from . import REPOSITORY_ROOT
ROOT = REPOSITORY_ROOT
LEVELS = {"全部级别": "", "Info": "info", "Warning": "warning", "Error": "error"}
STATES = {"offline": "未连接", "connecting": "连接中", "connected": "等待上线",
          "online": "在线", "disconnecting": "断开中", "reconnecting": "重连中"}

def configure_dpi():
    if os.name == "nt":
        import ctypes
        try:
            ctypes.windll.shcore.SetProcessDpiAwareness(2)
        except (AttributeError, OSError):
            pass


class ConsoleWindow:
    def __init__(self, root, client=None):
        self.root = root
        root.withdraw()
        self.client = client or ConsoleClient()
        self.history = LogHistory()
        self.paused = False
        self._closing = False
        self._filter_after = None
        self._poll_after = None
        self._rendered = {}
        self._last_snapshot = {}
        self._command_buttons = []
        self._connection_fields = []
        self._auth = dict(path="/api/V1/open-portal/app/wss/agent-sdk",
                          app_id="demo-app", app_key="demo-key", app_secret="demo-secret")
        self.scale = max(1.0, float(root.tk.call("tk", "scaling")) / (96 / 72))
        root.title("X2 控制台")
        width = min(round(1280 * self.scale), root.winfo_screenwidth() - 80)
        height = min(round(880 * self.scale), root.winfo_screenheight() - 120)
        root.geometry(f"{width}x{height}+{(root.winfo_screenwidth() - width) // 2}+30")
        root.minsize(min(round(1060 * self.scale), width), min(round(760 * self.scale), height))
        root.configure(bg=COLORS["bg"])
        try:
            root.iconbitmap(str(ROOT / "tools/unity-packager/assets/agibot-x2.ico"))
        except tk.TclError:
            pass
        self._style()
        self._build()
        root.protocol("WM_DELETE_WINDOW", self.close)
        root.bind("<Control-period>", lambda _: self._command("interrupt"))
        root.bind("<Control-l>", lambda _: self._focus_search())
        self.client.events.log("info", "控制台", "控制台已就绪。先启动 Unity 模拟器，再连接网关。")
        root.update_idletasks()
        self._poll()
        root.deiconify()

    def _style(self):
        self.font = "Microsoft YaHei UI" if os.name == "nt" else "TkDefaultFont"
        style = ttk.Style(self.root)
        style.theme_use("clam")
        style.configure(".", font=(self.font, 10), background=COLORS["panel"],
                        foreground=COLORS["text"], bordercolor=COLORS["border"],
                        lightcolor=COLORS["border"], darkcolor=COLORS["border"])
        style.configure("TFrame", background=COLORS["bg"])
        style.configure("Card.TFrame", background=COLORS["panel"])
        style.configure("TLabel", background=COLORS["panel"], foreground=COLORS["text"])
        style.configure("Muted.TLabel", foreground=COLORS["muted"])
        style.configure("Title.TLabel", font=(self.font, 12, "bold"))
        style.configure("Metric.TLabel", font=(self.font, 19, "bold"))
        style.configure("TButton", padding=(10, 8), background=COLORS["button"], borderwidth=1,
                        focusthickness=2, focuscolor=COLORS["accent"])
        style.map("TButton", background=[("active", COLORS["hover"]), ("disabled", COLORS["disabled"])],
                  foreground=[("disabled", COLORS["disabled_text"])])
        style.configure("Primary.TButton", background=COLORS["accent"],
                        foreground=COLORS["on_accent"], font=(self.font, 10, "bold"))
        style.map("Primary.TButton", background=[("active", COLORS["accent_hover"]), ("disabled", COLORS["accent_disabled"])],
                  foreground=[("disabled", COLORS["muted"])])
        style.configure("Stop.TButton", background=COLORS["stop"], foreground=COLORS["stop_text"],
                        font=(self.font, 10, "bold"))
        style.map("Stop.TButton", background=[("active", COLORS["stop_hover"]), ("disabled", COLORS["disabled"])],
                  foreground=[("disabled", COLORS["disabled_text"])])
        style.configure("TEntry", fieldbackground=COLORS["inset"], insertcolor=COLORS["text"], padding=7)
        style.map("TEntry", bordercolor=[("focus", COLORS["accent"])])
        style.configure("TCombobox", fieldbackground=COLORS["inset"], padding=6,
                        arrowcolor=COLORS["text"])
        style.map("TCombobox", fieldbackground=[("readonly", COLORS["inset"])],
                  selectbackground=[("readonly", COLORS["inset"])],
                  selectforeground=[("readonly", COLORS["text"])])
        style.configure("TCheckbutton", background=COLORS["panel"], focuscolor=COLORS["accent"],
                        indicatorsize=round(12 * self.scale), indicatorbackground=COLORS["inset"])
        style.map("TCheckbutton", background=[("active", COLORS["panel"])])
        style.map("TCheckbutton", indicatorbackground=[("selected", COLORS["accent"])])
        style.configure("Treeview", background=COLORS["inset"], fieldbackground=COLORS["inset"],
                        rowheight=round(29 * self.scale), borderwidth=0, font=(self.font, 10))
        style.configure("Treeview.Heading", background=COLORS["heading"],
                        font=(self.font, 10, "bold"), padding=8)
        style.map("Treeview", background=[("selected", COLORS["selected"])],
                  foreground=[("selected", COLORS["text"])])
        style.configure("TNotebook", background=COLORS["bg"], borderwidth=0)
        style.configure("TNotebook.Tab", background=COLORS["button"], padding=(12, 8))
        style.map("TNotebook.Tab", background=[("selected", COLORS["panel"])],
                  foreground=[("selected", COLORS["accent"])])
        for name in ("Vertical.TScrollbar", "Horizontal.TScrollbar"):
            style.configure(name, background=COLORS["button"], troughcolor=COLORS["inset"],
                            arrowcolor=COLORS["muted"], bordercolor=COLORS["panel"],
                            lightcolor=COLORS["button"], darkcolor=COLORS["button"])
            style.map(name, background=[("active", COLORS["hover"])])
        self.root.option_add("*TCombobox*Listbox.background", COLORS["inset"])
        self.root.option_add("*TCombobox*Listbox.foreground", COLORS["text"])
        self.root.option_add("*TCombobox*Listbox.selectBackground", COLORS["selected"])

    def _card(self, parent, **grid):
        card = ttk.Frame(parent, style="Card.TFrame", padding=round(16 * self.scale))
        card.grid(**grid)
        return card

    def _build(self):
        self.root.columnconfigure(0, weight=1)
        self.root.rowconfigure(1, weight=1)
        header = tk.Frame(self.root, bg=COLORS["bg"])
        header.grid(row=0, column=0, sticky="ew", padx=24, pady=(20, 16))
        header.columnconfigure(1, weight=1)
        tk.Label(header, text="X2", font=(self.font, 25, "bold"), bg=COLORS["bg"],
                 fg=COLORS["accent"]).grid(row=0, column=0, rowspan=2, padx=(0, 16))
        tk.Label(header, text="机器人控制台", font=(self.font, 18, "bold"),
                 bg=COLORS["bg"], fg=COLORS["text"]).grid(row=0, column=1, sticky="w")
        tk.Label(header, text="MANAGEMENT CONSOLE  /  Sessions · Controls · Diagnostics",
                 font=(self.font, 9), bg=COLORS["bg"], fg=COLORS["muted"]).grid(row=1, column=1, sticky="w")
        ttk.Button(header, text="启动 Unity 模拟器", command=self._launch_simulator).grid(row=0, column=2, rowspan=2)

        body = ttk.Frame(self.root)
        body.grid(row=1, column=0, sticky="nsew", padx=24)
        body.columnconfigure(1, weight=1)
        body.rowconfigure(0, weight=1)
        left_shell = ttk.Frame(body)
        left_shell.grid(row=0, column=0, sticky="ns", padx=(0, 16))
        left_shell.rowconfigure(0, weight=1)
        self.left_canvas = tk.Canvas(left_shell, width=round(352 * self.scale), height=1,
                                     bg=COLORS["bg"], highlightthickness=0)
        self.left_canvas.grid(row=0, column=0, sticky="ns")
        left_scroll = ttk.Scrollbar(left_shell, orient="vertical", command=self.left_canvas.yview)
        left_scroll.grid(row=0, column=1, sticky="ns")
        self.left_canvas.configure(yscrollcommand=left_scroll.set)
        left = ttk.Frame(self.left_canvas)
        self._left_content = left
        content_id = self.left_canvas.create_window(0, 0, window=left, anchor="nw")
        left.bind("<Configure>", lambda _: self.left_canvas.configure(
            scrollregion=self.left_canvas.bbox("all"), width=left.winfo_reqwidth()))
        self.left_canvas.bind("<Configure>", lambda e: self.left_canvas.itemconfigure(content_id, width=e.width))
        self.root.bind("<MouseWheel>", self._scroll_left, add="+")
        left.columnconfigure(0, weight=1)
        connection = self._card(left, row=0, column=0, sticky="ew", pady=(0, 12))
        self._connection_card(connection)
        tabs = ttk.Notebook(left)
        tabs.grid(row=1, column=0, sticky="ew")
        control_page = ttk.Frame(tabs, style="Card.TFrame")
        session_page = ttk.Frame(tabs, style="Card.TFrame")
        for page in (control_page, session_page):
            page.columnconfigure(0, weight=1)
        tabs.add(control_page, text="机器人控制")
        tabs.add(session_page, text="会话 / 优先级")
        self.control_tabs = tabs
        controls = self._card(control_page, row=0, column=0, sticky="new")
        self._controls_card(controls)
        sessions = self._card(session_page, row=0, column=0, sticky="ew")
        self._sessions_card(sessions)
        right = ttk.Frame(body)
        right.grid(row=0, column=1, sticky="nsew")
        right.columnconfigure(0, weight=1)
        right.rowconfigure(1, weight=1)
        metrics = ttk.Frame(right)
        metrics.grid(row=0, column=0, sticky="ew", pady=(0, 12))
        self.metric_vars = {}
        for index, (key, title, initial) in enumerate([
            ("connection", "网关状态", "未连接"), ("traffic", "消息 RX / TX", "0 / 0"),
            ("alerts", "Warning / Error", "0 / 0"),
        ]):
            metrics.columnconfigure(index, weight=1, uniform="metrics")
            card = self._card(metrics, row=0, column=index, sticky="ew",
                              padx=(0 if index == 0 else 10, 0))
            ttk.Label(card, text=title, style="Muted.TLabel").pack(anchor="w")
            var = tk.StringVar(value=initial)
            self.metric_vars[key] = var
            ttk.Label(card, textvariable=var, style="Metric.TLabel").pack(anchor="w", pady=(4, 0))
        log_card = self._card(right, row=1, column=0, sticky="nsew")
        self._logs_card(log_card)
        status = self._card(right, row=2, column=0, sticky="ew", pady=(12, 0))
        self._status_card(status)
        footer = tk.Frame(self.root, bg=COLORS["bg"])
        footer.grid(row=2, column=0, sticky="ew", padx=24, pady=(12, 14))
        footer.columnconfigure(0, weight=1)
        self.footer = tk.StringVar(value="就绪")
        tk.Label(footer, textvariable=self.footer, bg=COLORS["bg"], fg=COLORS["muted"],
                 font=(self.font, 9), anchor="w").grid(row=0, column=0, sticky="ew")
        tk.Label(footer, text="Catppuccin Mocha   ·   Ctrl+L 搜索   ·   Ctrl+. 停止", bg=COLORS["bg"],
                 fg=COLORS["muted"], font=(self.font, 9)).grid(row=0, column=1, padx=(10, 0))

    def _connection_card(self, card):
        card.columnconfigure(0, weight=1)
        card.columnconfigure(1, weight=0)
        ttk.Label(card, text="连接机器人", style="Title.TLabel").grid(row=0, column=0, columnspan=2, sticky="w")
        ttk.Label(card, text="主机", style="Muted.TLabel").grid(row=1, column=0, sticky="w", pady=(12, 4))
        ttk.Label(card, text="端口", style="Muted.TLabel").grid(row=1, column=1, sticky="w", padx=(8, 0), pady=(12, 4))
        self.host = tk.StringVar(value="127.0.0.1")
        self.port = tk.StringVar(value="9002")
        host = ttk.Entry(card, textvariable=self.host, width=17)
        host.grid(row=2, column=0, sticky="ew")
        port = ttk.Entry(card, textvariable=self.port, width=6)
        port.grid(row=2, column=1, padx=(8, 0), sticky="ew")
        self._connection_fields.extend((host, port))
        ttk.Label(card, text="管理会话与日志。语音对话由独立 x2_agent 负责。",
                  wraplength=round(260 * self.scale), style="Muted.TLabel").grid(
            row=3, column=0, columnspan=2, sticky="w", pady=(12, 8))
        options = ttk.Frame(card, style="Card.TFrame")
        options.grid(row=4, column=0, columnspan=2, sticky="ew")
        self.reconnect = tk.BooleanVar(value=True)
        reconnect = ttk.Checkbutton(options, text="自动重连", variable=self.reconnect)
        reconnect.pack(side="left")
        self._connection_fields.append(reconnect)
        actions = ttk.Frame(card, style="Card.TFrame")
        actions.grid(row=5, column=0, columnspan=2, sticky="ew", pady=(10, 0))
        actions.columnconfigure(0, weight=1)
        self.connect_button = ttk.Button(actions, text="连接网关", style="Primary.TButton", command=self._toggle_connection)
        self.connect_button.grid(row=0, column=0, sticky="ew", padx=(0, 8))
        self.advanced_button = ttk.Button(actions, text="高级", command=self._advanced)
        self.advanced_button.grid(row=0, column=1)
        self.connection_detail = tk.StringVar(value="尚未连接")
        ttk.Label(card, textvariable=self.connection_detail, style="Muted.TLabel",
                  wraplength=round(260 * self.scale)).grid(row=6, column=0, columnspan=2, sticky="w", pady=(8, 0))

    def _controls_card(self, card):
        card.columnconfigure(0, weight=1)
        ttk.Label(card, text="快捷控制", style="Title.TLabel").grid(row=0, column=0, sticky="w")
        self.stop_button = self._control_button(card, "停止动作与播报", lambda: self._command("interrupt"),
                                               style="Stop.TButton")
        self.stop_button.grid(row=1, column=0, sticky="ew", pady=(10, 12))
        gestures = ttk.Frame(card, style="Card.TFrame")
        gestures.grid(row=2, column=0, sticky="ew")
        for i, (title, name) in enumerate([("挥手", "wave_hands"), ("张开双臂", "open_arms")]):
            gestures.columnconfigure(i, weight=1)
            self._control_button(gestures, title, lambda n=name: self._skill("gesture", n)).grid(
                row=0, column=i, sticky="ew", padx=(0 if i == 0 else 8, 0))
        movement = ttk.Frame(card, style="Card.TFrame")
        movement.grid(row=3, column=0, sticky="ew", pady=(12, 0))
        movement.columnconfigure(1, weight=1)
        ttk.Label(movement, text="距离 / m", style="Muted.TLabel").grid(row=0, column=0, sticky="w")
        self.distance = tk.StringVar(value="1")
        ttk.Entry(movement, textvariable=self.distance, width=6).grid(row=0, column=1, padx=8, sticky="ew")
        self._control_button(movement, "前进", lambda: self._skill("movement", "walk", self.distance.get())).grid(row=0, column=2)
        ttk.Label(movement, text="角度 / °", style="Muted.TLabel").grid(row=1, column=0, pady=(8, 0), sticky="w")
        self.angle = tk.StringVar(value="90")
        ttk.Entry(movement, textvariable=self.angle, width=6).grid(row=1, column=1, padx=8, pady=(8, 0), sticky="ew")
        turns = ttk.Frame(movement, style="Card.TFrame")
        turns.grid(row=1, column=2, pady=(8, 0))
        self._control_button(turns, "左转", lambda: self._turn(-1)).pack(side="left")
        self._control_button(turns, "右转", lambda: self._turn(1)).pack(side="left", padx=(4, 0))
        ttk.Label(card, text="表情 · 持续 3 秒", style="Muted.TLabel").grid(row=4, column=0, sticky="w", pady=(12, 6))
        emotions = ttk.Frame(card, style="Card.TFrame")
        emotions.grid(row=5, column=0, sticky="ew")
        for i, (title, name) in enumerate([("开心", "happy"), ("难过", "sad"), ("惊讶", "surprised"),
                                          ("生气", "angry"), ("爱心", "love"), ("复位", "neutral")]):
            emotions.columnconfigure(i % 3, weight=1)
            self._control_button(emotions, title, lambda n=name: self._skill("emotion", n)).grid(
                row=i // 3, column=i % 3, sticky="ew", padx=(0 if i % 3 == 0 else 6, 0), pady=(0, 6))
        self.skill_status = tk.StringVar(value="尚无技能执行")
        ttk.Label(card, textvariable=self.skill_status, style="Muted.TLabel", wraplength=round(280 * self.scale)).grid(
            row=6, column=0, sticky="w", pady=(8, 0))

    def _control_button(self, parent, text, command, **kwargs):
        button = ttk.Button(parent, text=text, command=command, state="disabled", **kwargs)
        self._command_buttons.append(button)
        return button

    def _sessions_card(self, card):
        card.columnconfigure(0, weight=1)
        ttk.Label(card, text="会话与优先级", style="Title.TLabel").grid(row=0, column=0, sticky="w")
        ttk.Label(card, text="控制台固定 1000 · 其它会话 0–999",
                  style="Muted.TLabel").grid(row=1, column=0, sticky="w", pady=(6, 10))
        self.take_control = tk.BooleanVar(value=False)
        self.takeover_button = ttk.Checkbutton(card, text="控制台接管动作（关闭后交给 Agent）",
                                               variable=self.take_control, command=self._toggle_takeover,
                                               state="disabled")
        self.takeover_button.grid(row=2, column=0, sticky="w", pady=(0, 10))
        session_table = ttk.Frame(card, style="Card.TFrame")
        session_table.grid(row=3, column=0, sticky="ew")
        session_table.columnconfigure(0, weight=1)
        self.session_tree = ttk.Treeview(session_table, columns=("name", "priority", "owner"),
                                         show="headings", height=4, selectmode="browse")
        for key, title, width in [("name", "客户端", 130), ("priority", "优先级", 60), ("owner", "归属", 82)]:
            self.session_tree.heading(key, text=title, anchor="w")
            self.session_tree.column(key, width=round(width * self.scale), minwidth=round(45 * self.scale),
                                     stretch=key == "name")
        self.session_tree.grid(row=0, column=0, sticky="ew")
        session_scroll = ttk.Scrollbar(session_table, orient="vertical", command=self.session_tree.yview)
        session_scroll.grid(row=0, column=1, sticky="ns")
        self.session_tree.configure(yscrollcommand=session_scroll.set)
        self.session_tree.bind("<<TreeviewSelect>>", self._select_session)
        editor = ttk.Frame(card, style="Card.TFrame")
        editor.grid(row=4, column=0, sticky="ew", pady=(10, 0))
        editor.columnconfigure(1, weight=1)
        ttk.Label(editor, text="优先级", style="Muted.TLabel").grid(row=0, column=0, padx=(0, 8))
        self.priority = tk.StringVar(value="50")
        self.priority_entry = ttk.Entry(editor, textvariable=self.priority, width=7, state="disabled")
        self.priority_entry.grid(row=0, column=1, sticky="ew", padx=(0, 8))
        self.priority_button = ttk.Button(editor, text="应用", state="disabled", command=self._apply_priority)
        self.priority_button.grid(row=0, column=2)
        self.owner_status = tk.StringVar(value="连接后显示所有客户端")
        ttk.Label(card, textvariable=self.owner_status, style="Muted.TLabel",
                  wraplength=round(270 * self.scale)).grid(row=5, column=0, sticky="w", pady=(8, 0))
        self._sessions = {}

    def _toggle_takeover(self):
        requested = self.take_control.get()
        self.take_control.set(self._last_snapshot.get("control_enabled", False))
        self.owner_status.set("正在等待服务器确认动作控制权…")
        self._command("set_control", enabled=requested)

    def _select_session(self, _=None):
        selected = self.session_tree.selection()
        row = self._sessions.get(selected[0]) if selected else None
        editable = bool(row and row.get("role") != "controller" and self._last_snapshot.get("can_manage"))
        self.priority_entry.configure(state="normal" if editable else "disabled")
        self.priority_button.configure(state="normal" if editable else "disabled")
        if row:
            self.priority.set(str(row.get("priority", 0)))

    def _apply_priority(self):
        selected = self.session_tree.selection()
        if selected:
            try:
                priority = int(self.priority.get())
                if not 0 <= priority <= 999:
                    raise ValueError()
            except ValueError:
                self.owner_status.set("优先级必须是 0–999 的整数")
                return
            self._command("set_priority", robot_cid=selected[0], priority=priority)

    def _refresh_sessions(self, snapshot):
        rows = snapshot.get("sessions", [])
        selected = self.session_tree.selection()
        self._sessions = {row["robotCid"]: row for row in rows}
        self.session_tree.delete(*self.session_tree.get_children())
        for cid, row in self._sessions.items():
            owner = "控制 / 语音" if row.get("controlActive") and row.get("audioActive") else (
                "控制" if row.get("controlActive") else "语音" if row.get("audioActive") else "待命")
            if row.get("role") == "controller" and not row.get("controlEnabled", True):
                owner = "已交还动作"
            name = row.get("name", row.get("role", "")) + (
                "（本机）" if cid == snapshot["robot_cid"] else " · " + cid[-6:])
            self.session_tree.insert("", "end", iid=cid, values=(name, row.get("priority"), owner))
        if selected and selected[0] in self._sessions:
            self.session_tree.selection_set(selected[0])
        status = (("本控制台持有控制权" if snapshot.get("control_active") else
                              ("动作已交还 Agent" + ("，停止按钮可收回控制" if snapshot.get("can_manage") else ""))
                              if not snapshot.get("control_enabled", True)
                              else "当前无控制权") +
                  f" · {len(rows)} 路连接")
        if snapshot.get("state") == "online" and not snapshot.get("control_switch_supported"):
            status += "\n请更新模拟器以启用动作控制交还。"
        elif snapshot.get("control_active"):
            status += "\n外部语音 Agent 要执行动作，请关闭上方接管。"
        self.owner_status.set(status)

    def _logs_card(self, card):
        card.columnconfigure(0, weight=1)
        card.rowconfigure(2, weight=1)
        title = ttk.Frame(card, style="Card.TFrame")
        title.grid(row=0, column=0, sticky="ew")
        ttk.Label(title, text="运行日志", style="Title.TLabel").pack(side="left")
        self.log_count = tk.StringVar(value="0 条")
        ttk.Label(title, textvariable=self.log_count, style="Muted.TLabel").pack(side="left", padx=12)
        ttk.Button(title, text="导出 JSONL", command=self._export).pack(side="right")
        ttk.Button(title, text="清空", command=self._clear).pack(side="right", padx=6)
        self.pause_button = ttk.Button(title, text="暂停滚动", command=self._pause)
        self.pause_button.pack(side="right")
        filters = ttk.Frame(card, style="Card.TFrame")
        filters.grid(row=1, column=0, sticky="ew", pady=(10, 10))
        filters.columnconfigure(3, weight=1)
        self.level = tk.StringVar(value="全部级别")
        level_box = ttk.Combobox(filters, textvariable=self.level, values=list(LEVELS), state="readonly", width=10)
        level_box.grid(row=0, column=0)
        self.source = tk.StringVar(value="全部来源")
        source_box = ttk.Combobox(filters, textvariable=self.source,
                                 values=["全部来源", "Unity", "控制台", "协议"], state="readonly", width=10)
        source_box.grid(row=0, column=1, padx=8)
        ttk.Label(filters, text="搜索", style="Muted.TLabel").grid(row=0, column=2, padx=(0, 8))
        self.query = tk.StringVar()
        self.search = ttk.Entry(filters, textvariable=self.query)
        self.search.grid(row=0, column=3, sticky="ew")
        self.query.trace_add("write", lambda *_: self._filter_changed())
        level_box.bind("<<ComboboxSelected>>", lambda _: self._render())
        source_box.bind("<<ComboboxSelected>>", lambda _: self._render())
        split = ttk.Panedwindow(card, orient="vertical")
        split.grid(row=2, column=0, sticky="nsew")
        table = ttk.Frame(split, style="Card.TFrame")
        table.columnconfigure(0, weight=1)
        table.rowconfigure(0, weight=1)
        self.tree = ttk.Treeview(table, columns=("time", "level", "source", "message"),
                                 show="headings", selectmode="browse", height=12)
        for key, title, width, stretch in [("time", "时间", 102, False), ("level", "级别", 78, False),
                                           ("source", "来源", 66, False), ("message", "内容", 420, True)]:
            self.tree.heading(key, text=title, anchor="w")
            self.tree.column(key, width=round(width * self.scale),
                             minwidth=round((width if not stretch else 180) * self.scale),
                             stretch=stretch, anchor="w")
        self.tree.tag_configure("warning", foreground=COLORS["warning"])
        self.tree.tag_configure("error", foreground=COLORS["error"])
        self.tree.grid(row=0, column=0, sticky="nsew")
        scroll = ttk.Scrollbar(table, orient="vertical", command=self.tree.yview)
        scroll.grid(row=0, column=1, sticky="ns")
        self.tree.configure(yscrollcommand=scroll.set)
        self.tree.bind("<<TreeviewSelect>>", self._select_log)
        split.add(table, weight=3)
        details = ttk.Frame(split, style="Card.TFrame", padding=(0, 10, 0, 0))
        details.columnconfigure(0, weight=1)
        details.rowconfigure(1, weight=1)
        ttk.Label(details, text="详情与堆栈", style="Muted.TLabel").grid(row=0, column=0, sticky="w", pady=(0, 4))
        self.details = self._text(details, height=5)
        self.details.grid(row=1, column=0, sticky="nsew")
        detail_scroll = ttk.Scrollbar(details, orient="vertical", command=self.details.yview)
        detail_scroll.grid(row=1, column=1, sticky="ns")
        self.details.configure(yscrollcommand=detail_scroll.set)
        self._set_text(self.details, "选择一条日志，查看完整正文、堆栈和转发信息。\n日志保留最近 1200 条，可按级别、来源和关键词筛选。")
        split.add(details, weight=1)

    def _text(self, parent, height):
        return tk.Text(parent, height=height, bg=COLORS["inset"], fg=COLORS["text"],
                       insertbackground=COLORS["text"], selectbackground=COLORS["selected"],
                       font=(self.font, 10), relief="flat", padx=10, pady=8,
                       wrap="word", state="disabled", highlightthickness=1,
                       highlightbackground=COLORS["border"], highlightcolor=COLORS["accent"])

    @staticmethod
    def _set_text(widget, value):
        widget.configure(state="normal")
        widget.delete("1.0", "end")
        widget.insert("1.0", value)
        widget.configure(state="disabled")

    def _status_card(self, card):
        card.columnconfigure(1, weight=1)
        ttk.Label(card, text="状态监控", style="Title.TLabel").grid(row=0, column=0, sticky="w")
        self.activity = tk.StringVar(value="等待连接")
        ttk.Label(card, textvariable=self.activity, style="Muted.TLabel").grid(row=0, column=1, sticky="w", padx=12)
        fields = ttk.Frame(card, style="Card.TFrame")
        fields.grid(row=1, column=0, columnspan=2, sticky="ew", pady=(12, 0))
        self.status_vars = {}
        for index, (key, title) in enumerate((("power", "电源状态"), ("network", "网络状态"), ("skill", "最近技能"))):
            fields.columnconfigure(index, weight=1, uniform="status")
            group = ttk.Frame(fields, style="Card.TFrame")
            group.grid(row=0, column=index, sticky="nsew", padx=(0 if index == 0 else 12, 0))
            ttk.Label(group, text=title, style="Muted.TLabel").pack(anchor="w")
            value = tk.StringVar(value="—")
            self.status_vars[key] = value
            ttk.Label(group, textvariable=value, wraplength=round(180 * self.scale)).pack(anchor="w", pady=(4, 0))

    def _settings(self):
        try:
            port = int(self.port.get())
        except ValueError:
            raise ValueError("端口必须是整数。") from None
        return ConnectionSettings(host=self.host.get().strip(), port=port,
                                  reconnect=self.reconnect.get(), **self._auth)

    def _toggle_connection(self):
        if self.client.active:
            self.client.disconnect()
            return
        try:
            self.client.connect(self._settings())
        except ValueError as exc:
            self.connection_detail.set(str(exc))
            self.client.events.log("error", "控制台", str(exc))

    def _command(self, name, **params):
        self.client.command(name, **params)

    def _skill(self, kind, name, value=None):
        self._command("skill", skill_type=kind, skill=name, value=value)

    def _turn(self, sign):
        try:
            angle = float(self.angle.get())
            if not 0 <= angle <= 360:
                raise ValueError()
        except ValueError:
            self.client.events.log("error", "控制台", "转向角度请输入 0–360 之间的数字。")
            return
        self._skill("movement", "turn", sign * angle)

    def _advanced(self):
        window = tk.Toplevel(self.root)
        window.title("高级连接")
        window.configure(bg=COLORS["panel"])
        window.transient(self.root)
        window.resizable(False, False)
        form = ttk.Frame(window, style="Card.TFrame", padding=20)
        form.pack(fill="both", expand=True)
        fields = {}
        for row, (key, title) in enumerate([("path", "网关路径"), ("app_id", "应用 ID"),
                                            ("app_key", "应用 Key"), ("app_secret", "签名密钥")]):
            ttk.Label(form, text=title).grid(row=row, column=0, sticky="w", padx=(0, 12), pady=6)
            var = tk.StringVar(value=self._auth[key])
            fields[key] = var
            ttk.Entry(form, textvariable=var, width=44, show="•" if key == "app_secret" else "").grid(row=row, column=1, pady=6)
        ttk.Label(form, text="凭据仅在本次运行中使用，不写入磁盘。", style="Muted.TLabel").grid(
            row=4, column=0, columnspan=2, sticky="w", pady=10)
        def save():
            self._auth = {key: var.get().strip() for key, var in fields.items()}
            window.destroy()
        actions = ttk.Frame(form, style="Card.TFrame")
        actions.grid(row=5, column=0, columnspan=2, sticky="e")
        ttk.Button(actions, text="取消", command=window.destroy).pack(side="left", padx=8)
        ttk.Button(actions, text="应用", style="Primary.TButton", command=save).pack(side="left")
        window.bind("<Escape>", lambda _: window.destroy())
        window.grab_set()

    def _filters(self):
        return dict(level=LEVELS[self.level.get()], source="" if self.source.get() == "全部来源" else self.source.get(),
                    query=self.query.get().strip())

    def _filter_changed(self):
        if self._filter_after is not None:
            self.root.after_cancel(self._filter_after)
        self._filter_after = self.root.after(180, self._render)

    def _render(self):
        self._filter_after = None
        selected = self.tree.selection()
        records = self.history.filtered(**self._filters())
        self.tree.delete(*self.tree.get_children())
        self._rendered = {}
        for record in records:
            self._insert(record)
        if selected and selected[0] in self._rendered:
            self.tree.selection_set(selected[0])
        if not self.paused and records:
            self.tree.see(records[-1]["id"])
        self.log_count.set(f"{len(records)} / {len(self.history.records)} 条")

    def _insert(self, record):
        self._rendered[record["id"]] = record
        summary = record.get("message", "").replace("\n", " ").replace("\r", " ")
        self.tree.insert("", "end", iid=record["id"], values=(
            display_time(record.get("timestampMs")), record["level"].upper(), record["source"], summary[:350]),
            tags=(record["level"],))

    def _select_log(self, _=None):
        selected = self.tree.selection()
        record = self._rendered.get(selected[0]) if selected else None
        if not record:
            return
        header = f"{display_time(record.get('timestampMs'))}   {record['level'].upper()}   {record['source']}"
        text = header + "\n\n" + record.get("message", "")
        if record.get("stackTrace"):
            text += "\n\n" + record["stackTrace"]
        metadata = {key: value for key, value in record.items()
                    if key not in ("id", "message", "stackTrace", "level", "source") and value is not None}
        text += "\n\n" + json.dumps(metadata, ensure_ascii=False, indent=2)
        self._set_text(self.details, text)

    def _pause(self):
        self.paused = not self.paused
        self._last_snapshot = {}
        self.pause_button.configure(text="继续显示" if self.paused else "暂停滚动")
        if not self.paused:
            self._render()

    def _clear(self):
        self.history.clear()
        self._render()
        self._set_text(self.details, "日志已清空。新日志仍会继续接收。")

    def _export(self):
        path = filedialog.asksaveasfilename(parent=self.root, title="导出当前筛选的日志",
                                          defaultextension=".jsonl", initialfile="x2-console.jsonl",
                                          filetypes=[("JSON Lines", "*.jsonl"), ("所有文件", "*.*")])
        if not path:
            return
        try:
            count = self.history.export(path, **self._filters())
            self.client.events.log("info", "控制台", f"已导出 {count} 条日志到 {path}")
        except OSError as exc:
            self.client.events.log("error", "控制台", "导出失败：" + str(exc))

    def _launch_simulator(self):
        path = ROOT / "exe/x2模拟器.exe"
        if os.name != "nt" or not path.is_file():
            self.client.events.log("error", "控制台", "未找到 Windows 模拟器，请手动启动 Unity 工程或指定的 EXE。")
            return
        try:
            subprocess.Popen([str(path)], cwd=str(path.parent))
            self.client.events.log("info", "控制台", "正在启动模拟器，窗口出现后点击连接网关。")
        except OSError as exc:
            self.client.events.log("error", "控制台", "启动失败：" + str(exc))

    def _focus_search(self):
        self.search.focus_set()
        self.search.selection_range(0, "end")
        return "break"

    def _poll(self):
        if self._closing:
            return
        snapshot, records = self.client.events.drain()
        for raw in records:
            record = self.history.add(raw)
            if not self.paused and self.history.matches(record, **self._filters()):
                self._insert(record)
        if not self.paused:
            retained = {r["id"] for r in self.history.records}
            for old in list(self._rendered):
                if old not in retained:
                    self.tree.delete(old)
                    del self._rendered[old]
            if records and self.tree.get_children():
                self.tree.see(self.tree.get_children()[-1])
            self.log_count.set(f"{len(self._rendered)} / {len(self.history.records)} 条")
        state = snapshot["state"]
        self.metric_vars["connection"].set(STATES.get(state, state))
        self.metric_vars["traffic"].set(f"{snapshot['rx']} / {snapshot['tx']}")
        self.metric_vars["alerts"].set(f"{self.history.counts['warning']} / {self.history.counts['error']}")
        if snapshot != self._last_snapshot:
            active = self.client.active
            self.connect_button.configure(text="断开连接" if active else "连接网关",
                                          state="disabled" if state == "disconnecting" else "normal")
            for field in self._connection_fields:
                field.configure(state="disabled" if active else "normal")
            self.advanced_button.configure(state="disabled" if active else "normal")
            for button in self._command_buttons:
                button.configure(state="normal" if state == "online" and snapshot.get("control_active") else "disabled")
            if state == "online" and snapshot.get("can_manage"):
                self.stop_button.configure(state="normal")
            self.take_control.set(snapshot.get("control_enabled", False))
            self.takeover_button.configure(
                state="normal" if state == "online" and snapshot.get("control_switch_supported") else "disabled")
            self.connection_detail.set(snapshot["detail"])
            self.activity.set(snapshot["activity"])
            self.skill_status.set(snapshot["skill"] or "尚无技能执行")
            for key, variable in self.status_vars.items():
                value = snapshot.get(key) or "—"
                variable.set("正常" if value == "ok" else value)
            if snapshot.get("sessions") != self._last_snapshot.get("sessions"):
                self._refresh_sessions(snapshot)
            self.footer.set((snapshot["robot_cid"] or "支持多路连接 · 控制台默认最高优先级") +
                            (f"   ·   接收缓冲已丢弃 {snapshot['uiDropped']} 条" if snapshot["uiDropped"] else "") +
                            ("   ·   显示已暂停，日志仍在接收" if self.paused else ""))
            self._last_snapshot = snapshot
        self._poll_after = self.root.after(50, self._poll)

    def _scroll_left(self, event):
        if event.widget is self.session_tree:
            return
        widget = event.widget
        while widget is not None and widget is not self.root:
            if widget is self._left_content or widget is self.left_canvas:
                if self.left_canvas.yview() != (0.0, 1.0):
                    self.left_canvas.yview_scroll(-int(event.delta / 120), "units")
                return "break"
            widget = getattr(widget, "master", None)

    def close(self):
        if self._closing:
            return
        self._closing = True
        if self._poll_after:
            self.root.after_cancel(self._poll_after)
        if self._filter_after:
            self.root.after_cancel(self._filter_after)
        self.footer.set("正在断开连接并停止后台任务…")
        self.client.close()
        self._wait_closed()

    def _wait_closed(self):
        if self.client.stopped:
            self.root.destroy()
        else:
            self.root.after(50, self._wait_closed)


def main(argv=None):
    parser = argparse.ArgumentParser(description="X2 Python 图形控制台")
    parser.add_argument("--host", default="127.0.0.1")
    parser.add_argument("--port", type=int, default=9002)
    args = parser.parse_args(argv)
    configure_dpi()
    root = tk.Tk()
    window = ConsoleWindow(root)
    window.host.set(args.host)
    window.port.set(str(args.port))
    root.mainloop()


if __name__ == "__main__":
    main()

using System.Collections.Generic;
using UnityEngine;
using X02Competition.Protocol;
using X02Competition.Robot;

namespace X02Competition.Bootstrap
{
    /// <summary>
    /// 调试面板 + 字幕（OnGUI，录视频时字幕直接出镜）。
    /// 显示：连接状态 / ASR 与 LLM 字幕 / 技能触发 / 错误；按钮：麦克风开合、重放语料。
    /// </summary>
    public class DebugHud : MonoBehaviour
    {
        public CompetitionLauncher Launcher;

        [Tooltip("显示/隐藏面板快捷键（录屏时可隐藏调试 UI）")]
        public KeyCode ToggleKey = KeyCode.F1;

        const int MaxLines = 48;
        readonly List<string> _lines = new List<string>();
        readonly List<Texture2D> _textures = new List<Texture2D>();
        VirtualRobotRuntime _runtime;
        string _asrText = "";
        string _llmText = "";
        string _asrPhase = "等待语音";
        bool _replyStarted;
        bool _showSkills;
        float _lastActivity = -1f;
        int _eventCount;
        Vector2 _scroll;
        Vector2 _compactScroll;
        Rect _guiBounds;
        static float UiScale => Mathf.Clamp(Mathf.Min(Screen.width / 1920f, Screen.height / 1080f), 1f, 1.35f);
        public float OccludedWidthFraction => (_visible ? ConsoleBounds(Screen.width / UiScale, Screen.height / UiScale).xMax :
            12 + Mathf.Min(360, Mathf.Max(1, Screen.width / UiScale - 24))) * UiScale / Mathf.Max(1, Screen.width);
        public bool PointerOverHud => _guiBounds.Contains(new Vector2(Input.mousePosition.x / UiScale,
            (Screen.height - Input.mousePosition.y) / UiScale));
        GUIStyle _panel, _card, _title, _body, _muted, _section, _button, _badge;
        Font _font;
        static readonly Color Ink = new Color32(241, 245, 249, 255);
        static readonly Color Muted = new Color32(174, 190, 210, 255);
        static readonly Color Accent = new Color32(94, 234, 212, 255);
        static readonly Color Warning = new Color32(253, 211, 123, 255);
        bool _visible = false;   // 默认隐藏（演示/录屏干净），F1 呼出

        // AddComponent invokes Awake/OnEnable before the caller assigns Launcher.
        // Explicit initialization also works before Start and the first gateway event.
        public void Initialize(CompetitionLauncher launcher)
        {
            Launcher = launcher;
            if (isActiveAndEnabled) BindRuntime();
        }

        void OnEnable() { BindRuntime(); }
        void Start() { BindRuntime(); }
        void OnDisable() { UnbindRuntime(); }

        void BindRuntime()
        {
            if (Launcher == null) Launcher = GetComponent<CompetitionLauncher>();
            var next = Launcher != null ? Launcher.Runtime : null;
            if (ReferenceEquals(next, _runtime)) return;
            UnbindRuntime();
            if (next == null) return;
            _runtime = next;
            _runtime.AsrTextReceived += OnAsr;
            _runtime.LlmDeltaReceived += OnLlm;
            _runtime.SkillRequested += OnSkill;
            _runtime.SkillStateChanged += OnSkillState;
            _runtime.InterruptReceived += OnInterrupt;
            _runtime.AgentErrorReceived += OnError;
            _runtime.Connected += OnConnected;
            _runtime.Disconnected += OnDisconnected;
            Push("调试面板已就绪 · 正在接收实时事件");
        }

        void UnbindRuntime()
        {
            if (!ReferenceEquals(_runtime, null))
            {
                _runtime.AsrTextReceived -= OnAsr;
                _runtime.LlmDeltaReceived -= OnLlm;
                _runtime.SkillRequested -= OnSkill;
                _runtime.SkillStateChanged -= OnSkillState;
                _runtime.InterruptReceived -= OnInterrupt;
                _runtime.AgentErrorReceived -= OnError;
                _runtime.Connected -= OnConnected;
                _runtime.Disconnected -= OnDisconnected;
            }
            _runtime = null;
        }

        void OnAsr(string text, bool isFinal)
        {
            _asrText = LimitText(text);
            _asrPhase = isFinal ? "识别完成" : "正在识别";
            Touch();
            if (isFinal) { _replyStarted = false; Push("ASR · " + _asrText); }
        }

        void OnLlm(string delta)
        {
            if (string.IsNullOrEmpty(delta)) return;
            if (!_replyStarted)
            {
                _llmText = "";
                _replyStarted = true;
                Push("LLM · 开始接收机器人回复");
            }
            _llmText = LimitText(_llmText + delta);
            Touch();
        }

        void OnSkill(string type, string name) { Push("技能请求 · " + type + " / " + name); }
        void OnSkillState(string name, string state) { Push("技能状态 · " + name + " → " + state); }
        void OnInterrupt(string type) { _replyStarted = false; Push("对话打断 · " + type); }
        void OnError(int code, string message) { Push("Agent 错误 · " + code + " · " + message); }
        void OnConnected() { _replyStarted = false; Push("Agent 已连接 · 可以开始对话"); }
        void OnDisconnected() { _replyStarted = false; Push("Agent 已断开 · 等待重新连接"); }
        void Touch() { _lastActivity = Time.realtimeSinceStartup; }

        static string LimitText(string text)
        {
            const int max = 16000;
            if (string.IsNullOrEmpty(text)) return "";
            return text.Length <= max ? text : "…" + text.Substring(text.Length - max);
        }

        void Push(string line)
        {
            _eventCount++;
            Touch();
            _lines.Insert(0, System.DateTime.Now.ToString("HH:mm:ss") + "   " + LimitText(line));
            if (_lines.Count > MaxLines) _lines.RemoveAt(_lines.Count - 1);
        }

        void TestSkill(string type, string name, Dictionary<string, object> p = null)
        {
            if (_runtime == null) return;
            _runtime.OnAgentSkill(Ids.NewEventId(), Ids.NewItemId(), type, name, p);
        }

        void Update()
        {
            BindRuntime();
            if (Input.GetKeyDown(ToggleKey)) _visible = !_visible;
        }

        string Connection => _runtime == null ? "正在初始化" : _runtime.Port != null ? "Agent 已连接" : "等待 Agent 连接";
        bool IsSpeaking => _runtime != null && _runtime.Tts != null && _runtime.Tts.IsStreamingActive;

        string InputState
        {
            get
            {
                if (_runtime == null || _runtime.Input == null) return "输入未就绪";
                if (!_runtime.Input.IsRunning) return "输入已暂停";
                if (_runtime.Port == null) return "输入就绪";
                if (IsSpeaking) return "机器人播报中";
                return _runtime.Vad.InSpeech ? "正在收音" : "正在聆听";
            }
        }

        string Guidance
        {
            get
            {
                if (_runtime == null) return "正在准备对话环境，请稍候。";
                if (_runtime.Port == null) return "请先启动 Agent，连接成功后即可与机器人对话。";
                if (_runtime.Input == null || !_runtime.Input.IsRunning) return "打开调试面板并点击“开始输入”，继续语音对话。";
                if (IsSpeaking) return "机器人正在回答，播报结束后会自动恢复聆听。";
                if (Launcher != null && Launcher.Mode == CompetitionLauncher.InputMode.TestClip)
                    return "当前使用测试语料；可在调试面板中重放。";
                return "试着说：“你好，介绍一下自己” 或 “向我挥挥手”。";
            }
        }

        void OnGUI()
        {
            EnsureStyles();
            var oldMatrix = GUI.matrix;
            var oldColor = GUI.color;
            var oldBackground = GUI.backgroundColor;
            var oldContent = GUI.contentColor;
            var oldEnabled = GUI.enabled;
            // Preserve readable type in small windows; cap growth on large displays.
            var scale = UiScale;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, Vector3.one * scale);
            GUI.color = GUI.backgroundColor = GUI.contentColor = Color.white;
            try
            {
                if (_visible) DrawConsole(Screen.width / scale, Screen.height / scale);
                else DrawCompact(Screen.width / scale, Screen.height / scale);
            }
            finally
            {
                GUI.matrix = oldMatrix;
                GUI.color = oldColor;
                GUI.backgroundColor = oldBackground;
                GUI.contentColor = oldContent;
                GUI.enabled = oldEnabled;
            }
        }

        void DrawCompact(float width, float height)
        {
            // One corner card leaves the centre and bottom of the scene unobstructed.
            var cardWidth = Mathf.Min(360, Mathf.Max(1, width - 24));
            var contentWidth = Mathf.Max(1, cardWidth - _panel.padding.horizontal - 18);
            var status = Connection + "  /  " + InputState;
            var statusHeight = _muted.CalcHeight(new GUIContent(status), contentWidth);
            var hintHeight = _body.CalcHeight(new GUIContent(Guidance), contentWidth);
            var cameras = Launcher != null ? Launcher.Cameras : null;
            var cardHeight = Mathf.Min(height - 24, 106 + statusHeight + hintHeight + (cameras != null && cameras.Ready ? 48 : 0));
            _guiBounds = new Rect(12, 12, cardWidth, Mathf.Max(1, cardHeight));
            GUILayout.BeginArea(_guiBounds, _panel);
            _compactScroll = GUILayout.BeginScrollView(_compactScroll, false, false);
            GUILayout.BeginVertical(GUILayout.Width(contentWidth));
            if (GUILayout.Button("X2  /  " + ToggleKey + " 打开调试面板", _button, GUILayout.Width(Mathf.Max(1, contentWidth - 4)))) _visible = true;
            WrappedLabel(status, _muted, contentWidth);
            if (cameras != null && cameras.Ready && GUILayout.Button("视角：" + RobotCameraRig.ViewNames[cameras.ViewIndex] + " · C 切换", _button))
                cameras.SetView((cameras.ViewIndex + 1) % 4);
            GUILayout.Space(6);
            WrappedLabel(Guidance, _body, contentWidth);
            GUILayout.EndVertical();
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        // Logical pixels after DPI scaling. A normal desktop console stays near
        // one third of the viewport; narrow windows trade width for readable text.
        internal static Rect ConsoleBounds(float width, float height)
        {
            var panelWidth = Mathf.Min(Mathf.Clamp(width * 0.32f, 320, 440), Mathf.Max(1, width - 24));
            return new Rect(12, 12, panelWidth, Mathf.Max(1, Mathf.Min(760, height - 24)));
        }

        void DrawConsole(float width, float height)
        {
            var bounds = ConsoleBounds(width, height);
            _guiBounds = bounds;
            var contentWidth = Mathf.Max(1, bounds.width - _panel.padding.horizontal - 18);
            GUILayout.BeginArea(bounds, _panel);
            // Include all controls in the scroll area, so even a short Game view
            // cannot make the close button or footer overlap the conversation.
            _scroll = GUILayout.BeginScrollView(_scroll, false, false);
            GUILayout.BeginVertical(GUILayout.Width(contentWidth));
            var stackedHeader = contentWidth < 300;
            if (!stackedHeader) GUILayout.BeginHorizontal();
            GUILayout.Label("X2 / 实时调试", _title, GUILayout.MinWidth(0));
            if (GUILayout.Button(ToggleKey + " 收起", _button, GUILayout.Width(stackedHeader ? Mathf.Max(1, contentWidth - 4) : 88))) _visible = false;
            if (!stackedHeader) GUILayout.EndHorizontal();
            DrawStatus(contentWidth);
            DrawCameraControls(contentWidth);
            GUILayout.Space(8);
            WrappedLabel(Guidance, _muted, contentWidth);
            GUILayout.Space(12);
            DrawTranscript("01 / 用户语音 · ASR", _asrPhase, _asrText, "识别结果会实时显示在这里。请先连接 Agent 并开始输入。", contentWidth);
            DrawTranscript("02 / 机器人回复 · LLM", IsSpeaking ? "正在播报" : "文字回复", _llmText, "等待机器人回复；收到文字后将逐步显示。", contentWidth);

            var stackActions = contentWidth < 240;
            if (!stackActions) GUILayout.BeginHorizontal();
            GUI.enabled = _runtime != null && _runtime.Input != null;
            var running = _runtime != null && _runtime.Input != null && _runtime.Input.IsRunning;
            if (GUILayout.Button(running ? "暂停输入" : "开始输入", _button))
            {
                if (running) _runtime.Input.End();
                else _runtime.Input.Begin();
                Push(_runtime.Input.IsRunning ? "音频输入已启动" : "音频输入已暂停或未能启动");
            }
            GUI.enabled = true;
            if (GUILayout.Button("清空字幕", _button)) { _asrText = _llmText = ""; _asrPhase = "等待语音"; }
            if (!stackActions) GUILayout.EndHorizontal();
            if (Launcher != null && Launcher.ClipSource != null && Launcher.ClipSource.Clip != null)
                if (GUILayout.Button("重放测试语料", _button)) { Launcher.ClipSource.Replay(); Push("重放测试语料"); }
            GUILayout.Space(12);
            if (GUILayout.Button(_showSkills ? "技能测试  −  收起" : "技能测试  +  展开", _button)) _showSkills = !_showSkills;
            if (_showSkills) DrawSkills(contentWidth);
            GUILayout.Space(16);
            GUILayout.Label("03  /  实时事件", _section);
            WrappedLabel("累计 " + _eventCount + " 条 · 保留最近 " + MaxLines + " 条 · 最新在上", _muted, contentWidth);
            for (var i = 0; i < _lines.Count; i++)
            {
                GUILayout.BeginVertical(_card, GUILayout.Width(contentWidth));
                WrappedLabel(_lines[i], _body, contentWidth - _card.padding.horizontal);
                GUILayout.EndVertical();
            }
            GUILayout.Space(8);
            var activity = _lastActivity < 0 ? "尚无事件" : "最近更新 " + Mathf.Max(0, (int)(Time.realtimeSinceStartup - _lastActivity)) + " 秒前";
            WrappedLabel(activity + " / 隐藏面板后仍会接收事件", _muted, contentWidth);
            GUILayout.EndVertical();
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        void DrawStatus(float width)
        {
            var stacked = _badge.CalcSize(new GUIContent(Connection)).x + _badge.CalcSize(new GUIContent(InputState)).x + 12 > width;
            if (!stacked) GUILayout.BeginHorizontal();
            var color = GUI.contentColor;
            GUI.contentColor = _runtime != null && _runtime.Port != null ? Accent : Warning;
            GUILayout.Label(Connection, _badge);
            GUI.contentColor = color;
            GUILayout.Space(stacked ? 4 : 8);
            GUILayout.Label(InputState, _badge);
            if (!stacked) { GUILayout.FlexibleSpace(); GUILayout.EndHorizontal(); }
        }

        void DrawCameraControls(float width)
        {
            var cameras = Launcher != null ? Launcher.Cameras : null;
            if (cameras == null || !cameras.Ready) return;
            GUILayout.Space(6);
            WrappedLabel("观察视角 · C 轮换 / F2–F5 直达", _muted, width);
            var columns = width >= 260 ? 2 : 1;
            for (var i = 0; i < RobotCameraRig.ViewNames.Length; i++)
            {
                if (i % columns == 0) GUILayout.BeginHorizontal();
                var label = (i == cameras.ViewIndex ? "● " : "") + "F" + (i + 2) + " " + RobotCameraRig.ViewNames[i];
                if (GUILayout.Button(label, _button, GUILayout.Width(width / columns - 4), GUILayout.MinHeight(38))) cameras.SetView(i);
                if (i % columns == columns - 1 || i == RobotCameraRig.ViewNames.Length - 1) GUILayout.EndHorizontal();
            }
            WrappedLabel(cameras.ViewIndex == 0 ? "保留起点与当前位置，自动拉远观察行走。" : cameras.ViewIndex == 3 ?
                "画面内按住鼠标右键环绕；滚轮拉远，始终保留全身。" : "小范围移动后平滑跟随，保留地面参照与转向变化。", _muted, width);
        }

        static void WrappedLabel(string text, GUIStyle style, float width)
        {
            width = Mathf.Max(1, width);
            // IMGUI's CalcHeight uses the font's line advance, which can be
            // smaller than the visible CJK glyph bounds (especially at 13/14px).
            // Reserve extra vertical space rather than clipping the descenders.
            var height = Mathf.Ceil(style.CalcHeight(new GUIContent(text), width)) + 8;
            GUILayout.Label(text, style, GUILayout.Width(width),
                GUILayout.Height(height));
        }

        void DrawTranscript(string title, string phase, string text, string placeholder, float width)
        {
            GUILayout.BeginVertical(_card, GUILayout.Width(width));
            var innerWidth = width - _card.padding.horizontal;
            WrappedLabel(title, _section, innerWidth);
            WrappedLabel(phase, _muted, innerWidth);
            GUILayout.Space(6);
            WrappedLabel(string.IsNullOrEmpty(text) ? placeholder : text, string.IsNullOrEmpty(text) ? _muted : _body, innerWidth);
            GUILayout.EndVertical();
            GUILayout.Space(8);
        }

        void DrawSkills(float width)
        {
            WrappedLabel("直接触发机器人动作，用于检查场景反馈。", _muted, width);
            GUI.enabled = _runtime != null;
            var columns = Mathf.Max(1, Mathf.FloorToInt(width / 100));
            for (var i = 0; i < SkillLabels.Length; i++)
            {
                if (i % columns == 0) GUILayout.BeginHorizontal();
                if (GUILayout.Button(SkillLabels[i], _button, GUILayout.Width(width / columns - 4), GUILayout.MinHeight(38)))
                {
                    if (i < 2) TestSkill("gesture", SkillNames[i]);
                    else if (i < 7) TestSkill("emotion", SkillNames[i]);
                    else if (i == 7) TestSkill("movement", "walk", new Dictionary<string, object> { ["distanceM"] = 1f });
                    else if (i == 8) TestSkill("movement", "turn", new Dictionary<string, object> { ["angleDeg"] = 90f });
                    else TestSkill("movement", "stop");
                }
                if (i % columns == columns - 1 || i == SkillLabels.Length - 1) GUILayout.EndHorizontal();
            }
            GUI.enabled = true;
        }

        static readonly string[] SkillLabels = { "挥手", "张臂", "开心", "难过", "惊讶", "生气", "爱心", "前进 1m", "右转 90°", "停止运动" };
        static readonly string[] SkillNames = { "wave_hands", "open_arms", "happy", "sad", "surprised", "angry", "love" };

        void EnsureStyles()
        {
            if (_panel != null) return;
            _font = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei UI", "Microsoft YaHei", "Noto Sans CJK SC", "Arial" }, 16);
            _body = TextStyle(16, Ink);
            _muted = TextStyle(13, Muted);
            _section = TextStyle(14, Accent, FontStyle.Bold);
            _title = TextStyle(18, Ink, FontStyle.Bold);
            _panel = new GUIStyle { padding = new RectOffset(16, 16, 12, 12), border = new RectOffset(8, 8, 8, 8) };
            _panel.normal.background = Surface(new Color32(15, 23, 42, 248));
            _card = new GUIStyle { padding = new RectOffset(12, 12, 12, 12), margin = new RectOffset(0, 0, 3, 3), border = new RectOffset(8, 8, 8, 8) };
            _card.normal.background = Surface(new Color32(30, 41, 59, 255));
            _button = new GUIStyle(GUI.skin.button)
            {
                font = _font, fontSize = 14, fixedHeight = 0, wordWrap = true,
                padding = new RectOffset(8, 8, 10, 10), margin = new RectOffset(2, 2, 4, 4),
                border = new RectOffset(8, 8, 8, 8), richText = false,
                alignment = TextAnchor.MiddleCenter, clipping = TextClipping.Overflow,
                contentOffset = Vector2.zero
            };
            _button.normal.background = Surface(new Color32(42, 58, 78, 255));
            _button.hover.background = Surface(new Color32(56, 78, 100, 255));
            _button.active.background = Surface(new Color32(24, 92, 91, 255));
            _button.focused.background = _button.hover.background;
            _button.normal.textColor = _button.hover.textColor = _button.active.textColor = _button.focused.textColor = Ink;
            _badge = TextStyle(13, Color.white);
            _badge.padding = new RectOffset(8, 8, 6, 8);
            _badge.normal.background = _card.normal.background;
            _badge.border = new RectOffset(8, 8, 8, 8);
        }

        GUIStyle TextStyle(int size, Color color, FontStyle weight = FontStyle.Normal)
        {
            var style = new GUIStyle(GUI.skin.label)
            {
                font = _font, fontSize = size, fontStyle = weight, wordWrap = true,
                richText = false, clipping = TextClipping.Overflow,
                alignment = TextAnchor.UpperLeft, contentOffset = Vector2.zero,
                padding = new RectOffset(2, 2, 4, 6),
                margin = new RectOffset(0, 0, 3, 3)
            };
            style.normal.textColor = color;
            return style;
        }

        Texture2D Surface(Color color)
        {
            const int size = 20;
            const float radius = 7f;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
            var pixels = new Color[size * size];
            for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var dx = Mathf.Max(radius - x, x - (size - 1 - radius), 0);
                var dy = Mathf.Max(radius - y, y - (size - 1 - radius), 0);
                var pixel = color;
                pixel.a *= Mathf.Clamp01(radius + 0.5f - Mathf.Sqrt(dx * dx + dy * dy));
                pixels[y * size + x] = pixel;
            }
            texture.SetPixels(pixels);
            texture.Apply(false, true);
            _textures.Add(texture);
            return texture;
        }

        void OnDestroy()
        {
            UnbindRuntime();
            foreach (var texture in _textures) if (texture != null) Destroy(texture);
            if (_font != null) Destroy(_font);
        }
    }
}

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

        const int MaxLines = 12;
        readonly List<string> _lines = new List<string>();
        string _asrText = "";
        string _llmText = "";
        string _connState = "未连接";
        bool _visible = false;   // 默认隐藏（演示/录屏干净），F1 呼出

        void Awake()
        {
            if (Launcher == null) return;
            var rt = Launcher.Runtime;
            if (rt == null) return;
            rt.AsrTextReceived += (text, isFinal) =>
            {
                _asrText = (isFinal ? "[最终] " : "[中间] ") + text;
                if (isFinal) _llmText = "";
            };
            rt.LlmDeltaReceived += delta => _llmText += delta;
            rt.SkillRequested += (t, n) => Push("技能: " + t + "/" + n);
            rt.SkillStateChanged += (n, s) => Push("技能状态: " + n + " → " + s);
            rt.InterruptReceived += t => Push("打断: " + t);
            rt.AgentErrorReceived += (c, m) => Push("Agent错误: " + c + " " + m);
            rt.Connected += () => { _connState = "已连接"; Push("Agent 已连接"); };
            rt.Disconnected += () => { _connState = "已断开（等待重连）"; Push("Agent 已断开"); };
        }

        void Push(string line)
        {
            _lines.Add(System.DateTime.Now.ToString("HH:mm:ss ") + line);
            if (_lines.Count > MaxLines) _lines.RemoveAt(0);
        }

        void TestSkill(string type, string name, Dictionary<string, object> p = null)
        {
            var rt = Launcher.Runtime;
            rt.OnAgentSkill(Ids.NewEventId(), Ids.NewItemId(), type, name, p);
        }

        void Update()
        {
            if (Input.GetKeyDown(ToggleKey)) _visible = !_visible;
        }

        void OnGUI()
        {
            if (Launcher == null || Launcher.Runtime == null) return;
            var rt = Launcher.Runtime;

            if (!_visible)
            {
                // 隐藏态：右下角一行小提示，避免"怎么找回面板"
                GUI.Label(new Rect(Screen.width - 260, Screen.height - 24, 250, 20),
                    "按 " + ToggleKey + " 显示调试面板");
                return;
            }

            GUILayout.BeginArea(new Rect(16, 16, 560, 700), "X02 竞赛 Mock 网关", "box");
            GUILayout.Label("连接: " + _connState +
                (rt.Port != null ? "  VAD: " + (rt.Vad.Enabled ? "开启" : "冻结(播报中)") : "") +
                "  [按 " + ToggleKey + " 隐藏面板]");
            GUILayout.Space(6);
            GUILayout.Label("用户(ASR): " + _asrText);
            GUILayout.Label("机器人(LLM): " + _llmText);
            GUILayout.Space(6);

            if (GUILayout.Button(rt.Input != null && rt.Input.IsRunning ? "停止输入" : "开始输入"))
            {
                if (rt.Input != null)
                {
                    if (rt.Input.IsRunning) rt.Input.End();
                    else rt.Input.Begin();
                }
            }
            if (Launcher.ClipSource != null && Launcher.ClipSource.Clip != null)
            {
                if (GUILayout.Button("重放测试语料")) Launcher.ClipSource.Replay();
            }
            if (GUILayout.Button("清空字幕"))
            {
                _asrText = "";
                _llmText = "";
            }

            // ---- 技能手动触发（不经网关，直接调 Runtime 路由） ----
            GUILayout.Space(6);
            GUILayout.Label("技能测试:");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("挥手")) TestSkill("gesture", "wave_hands");
            if (GUILayout.Button("张臂")) TestSkill("gesture", "open_arms");
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("开心")) TestSkill("emotion", "happy");
            if (GUILayout.Button("难过")) TestSkill("emotion", "sad");
            if (GUILayout.Button("惊讶")) TestSkill("emotion", "surprised");
            if (GUILayout.Button("生气")) TestSkill("emotion", "angry");
            if (GUILayout.Button("爱心")) TestSkill("emotion", "love");
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("前进1m")) TestSkill("movement", "walk",
                new Dictionary<string, object> { ["distanceM"] = 1f });
            if (GUILayout.Button("右转90°")) TestSkill("movement", "turn",
                new Dictionary<string, object> { ["angleDeg"] = 90f });
            if (GUILayout.Button("停止")) TestSkill("movement", "stop");
            GUILayout.EndHorizontal();

            GUILayout.Space(6);
            GUILayout.Label("事件:");
            for (int i = _lines.Count - 1; i >= 0; i--) GUILayout.Label("  " + _lines[i]);
            GUILayout.EndArea();
        }
    }
}

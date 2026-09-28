using System.Collections.Generic;
using UnityEngine;

namespace X02Competition.Robot
{
    /// <summary>
    /// 头部表情屏控制器：在机器人头部运行时生成一块"屏幕"（Quad + 程序化贴图），
    /// 按表情名绘制像素风眼睛/嘴巴；TTS 播放时嘴巴随音频能量张合（口型）。
    /// 零美术资产依赖。
    /// </summary>
    public class EmotionController : MonoBehaviour
    {
        [Header("屏幕（头部局部偏移/朝向，方向反了就调这里）")]
        public Vector3 ScreenLocalPosition = new Vector3(0f, 0.02f, 0.12f);
        public Vector3 ScreenLocalEuler = new Vector3(0f, 180f, 180f);
        public Vector2 ScreenSize = new Vector2(0.16f, 0.10f);

        [Header("配色")]
        public Color Background = new Color(0.02f, 0.02f, 0.05f, 0.95f);
        public Color Foreground = new Color(0.25f, 0.95f, 1.00f);

        [Tooltip("日志开关")]
        public bool Verbose = true;

        public const int W = 64;
        public const int H = 40;

        Transform _head;
        GameObject _screen;
        Texture2D _tex;

        string _emotion = "neutral";
        float _holdUntil;          // 定时回 neutral
        float _mouthLevel;         // 0~1（TTS 能量驱动）
        float _mouthSmooth;
        float _lastRedraw;
        bool _dirty = true;

        static readonly HashSet<string> KnownEmotions = new HashSet<string>
        {
            "neutral", "happy", "sad", "surprised", "angry", "love",
        };
        static readonly string[] HeartRows =
        {
            "   ###     ###   ",
            "  #####   #####  ",
            " ####### ####### ",
            "#################",
            "#################",
            " ############### ",
            "  #############  ",
            "   ###########   ",
            "    #########    ",
            "     #######     ",
            "      #####      ",
            "       ###       ",
            "        #        ",
        };

        /// <summary>是否正在显示非 neutral 表情（含说话口型）。</summary>
        public bool IsShowing => _emotion != "neutral" || _mouthSmooth > 0.02f;

        void Start()
        {
            _head = FindHead();
            if (_head == null)
            {
                Debug.LogWarning("[Emotion] 未找到头部（层级内名字含 'head'），表情屏挂到本物体");
                _head = transform;
            }
            BuildScreen();
            if (Verbose)
                Debug.Log("[Emotion] 头部=" + _head.name + " 世界位置=" +
                    _head.position.ToString("F3") + " lossyScale=" +
                    _head.lossyScale.ToString("F3") + " 屏世界位置=" +
                    _screen.transform.position.ToString("F3"));
            Redraw();
        }

        Transform FindHead()
        {
            // 优先真实头部连杆（head_pitch_link > head_yaw_link），
            // 排除相机/传感器（rgb/depth/camera/depth/lidar）——
            // 之前误挂到 rgb_head_center 的 Visuals 深层，位置/尺度不可控
            Transform pitch = null, yaw = null;
            foreach (var t in GetComponentsInChildren<Transform>())
            {
                var n = t.name.ToLowerInvariant();
                if (!n.Contains("head")) continue;
                if (n.Contains("rgb") || n.Contains("camera") ||
                    n.Contains("depth") || n.Contains("lidar")) continue;
                if (n.Contains("head_pitch_link")) { if (pitch == null) pitch = t; }
                else if (n.Contains("head_yaw_link")) { if (yaw == null) yaw = t; }
            }
            return pitch ?? yaw ?? transform;
        }

        void BuildScreen()
        {
            // 手工建 Mesh（不用 CreatePrimitive：其自带非凸 MeshCollider
            // 挂在 ArticulationBody 层级下会触发物理不兼容警告）
            _screen = new GameObject("EmotionScreen");
            _screen.transform.SetParent(_head, false);
            // Quad 正面朝局部 +Z（实测指向脑后），绕 Y 转 180° 朝外
            _screen.transform.localPosition = ScreenLocalPosition;
            _screen.transform.localRotation = Quaternion.Euler(ScreenLocalEuler);
            // 头链若有缩放（URDF Visuals 常见 0.01），补偿到期望世界尺寸
            var s = _head.lossyScale;
            _screen.transform.localScale = new Vector3(
                ScreenSize.x / Mathf.Max(Mathf.Abs(s.x), 1e-4f),
                ScreenSize.y / Mathf.Max(Mathf.Abs(s.y), 1e-4f),
                1f / Mathf.Max(Mathf.Abs(s.z), 1e-4f));

            var mesh = new Mesh
            {
                vertices = new[]
                {
                    new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f),
                    new Vector3(-0.5f,  0.5f, 0f), new Vector3(0.5f,  0.5f, 0f),
                },
                triangles = new[] { 0, 2, 1, 2, 3, 1 },
                uv = new[]
                {
                    new Vector2(0f, 0f), new Vector2(1f, 0f),
                    new Vector2(0f, 1f), new Vector2(1f, 1f),
                },
            };
            mesh.RecalculateNormals();
            _screen.AddComponent<MeshFilter>().sharedMesh = mesh;

            _tex = new Texture2D(W, H, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
            };
            _tex.wrapMode = TextureWrapMode.Clamp;

            // 按项目实际渲染管线选 shader：装了 URP 包但 GraphicsSettings
            // 未启用 SRP（Built-in）时，URP Unlit 会渲染成粉色
            var srp = UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline;
            var shader = srp != null
                ? Shader.Find("Universal Render Pipeline/Unlit")
                : Shader.Find("Unlit/Texture");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            var mat = new Material(shader)
            {
                mainTexture = _tex,
            };
            _screen.AddComponent<MeshRenderer>().material = mat;
            if (Verbose)
                Debug.Log("[Emotion] 管线=" + (srp != null ? srp.name : "Built-in") +
                    " shader=" + shader.name);
        }

        /// <summary>设置表情。durationSec &lt;= 0 持续显示；&gt; 0 到时回 neutral。</summary>
        public bool SetEmotion(string emotion, float durationSec)
        {
            if (!KnownEmotions.Contains(emotion))
            {
                Debug.LogWarning("[Emotion] 未知表情: " + emotion + "，支持: " +
                    string.Join("/", KnownEmotions));
                return false;
            }
            _emotion = emotion;
            _holdUntil = durationSec > 0f ? Time.time + durationSec : float.MaxValue;
            _dirty = true;
            if (Verbose) Debug.Log("[Emotion] " + emotion + " " + durationSec.ToString("F1") + "s");
            return true;
        }

        /// <summary>TTS 口型能量驱动（0~1）。0 持续一小段时间后自动闭嘴。</summary>
        public void SetMouthLevel(float level) => _mouthLevel = Mathf.Clamp01(level);

        void Update()
        {
            if (_holdUntil != float.MaxValue && Time.time >= _holdUntil && _emotion != "neutral")
            {
                _emotion = "neutral";
                _dirty = true;
            }

            var target = _mouthLevel;
            _mouthLevel = 0f; // 每帧能量需重喂（TtsStreamPlayer 驱动）
            var previousMouth = _mouthSmooth;
            _mouthSmooth = Mathf.MoveTowards(_mouthSmooth, target, Time.deltaTime * 10f);
            if (!Mathf.Approximately(previousMouth, _mouthSmooth)) _dirty = true;

            // 限频重绘（口型变化才画）
            if ((Time.time - _lastRedraw) > 0.08f &&
                (_dirty || _mouthSmooth > 0.02f || IsShowing))
            {
                Redraw();
            }
        }

        // ---------------- 像素绘制 ----------------

        void Redraw()
        {
            _lastRedraw = Time.time;
            _dirty = false;

            var px = new Color32[W * H];
            Fill(px, Background);

            bool speaking = _mouthSmooth > 0.05f;
            string face = _emotion;

            switch (face)
            {
                case "happy":
                    // Broad closed smiling eyes remain recognizable during speech.
                    Arch(px, 16, 25, 8, 5);
                    Arch(px, 47, 25, 8, 5);
                    Mouth(px, curve: +1f, open: speaking ? _mouthSmooth : 0f);
                    break;
                case "sad":
                    Eyes(px, squint: 0.65f, tilt: 0);
                    ThickLine(px, 7, 29, 23, 34);
                    ThickLine(px, 40, 34, 56, 29);
                    Mouth(px, curve: -1f, open: speaking ? _mouthSmooth : 0f);
                    DrawOval(px, 10, 16, 4, 7);
                    break;
                case "surprised":
                    Eyes(px, squint: 0f, tilt: 0, big: true);
                    Mouth(px, curve: 0f, open: speaking ? Mathf.Max(_mouthSmooth, 0.6f) : 0.6f, round: true);
                    break;
                case "angry":
                    // Inner brow ends point down toward the nose.
                    ThickLine(px, 6, 35, 24, 27);
                    ThickLine(px, 39, 27, 57, 35);
                    Eyes(px, squint: 0.85f, tilt: 0);
                    Mouth(px, curve: -0.6f, open: speaking ? _mouthSmooth : 0f);
                    break;
                case "love":
                    Heart(px, 16, 26);
                    Heart(px, W - 17, 26);
                    Mouth(px, curve: +1f, open: speaking ? _mouthSmooth : 0f);
                    break;
                default: // neutral
                    Eyes(px, squint: 0f, tilt: 0);
                    Mouth(px, curve: 0f, open: speaking ? _mouthSmooth : 0f);
                    break;
            }

            _tex.SetPixels32(px);
            _tex.Apply();
        }

        static void Fill(Color32[] px, Color c)
        {
            var cc = (Color32)c;
            for (int i = 0; i < px.Length; i++) px[i] = cc;
        }

        void Eyes(Color32[] px, float squint, int tilt, bool big = false)
        {
            // 两只眼：椭圆，squint 压扁，tilt 1=右眼高（疑惑眉）
            var eyeW = big ? 14 : 10;
            var eyeH = Mathf.RoundToInt((big ? 12 : 11) * (1f - squint * 0.8f));
            var cy = 24;
            DrawOval(px, 14, cy + (tilt != 0 ? tilt * 2 : 0), eyeW, eyeH);
            DrawOval(px, W - 15, cy, eyeW, eyeH);
        }

        void Mouth(Color32[] px, float curve, float open, bool round = false)
        {
            var cx = W / 2;
            var cy = 10;
            if (round)
            {
                var h = Mathf.RoundToInt(7 + open * 5);
                DrawOval(px, cx, cy, 10, h);
                DrawOval(px, cx, cy, 5, h - 5, Background);
                return;
            }
            // 弧线嘴：curve +1 上弯（笑）/ -1 下弯
            var openH = Mathf.RoundToInt(open * (curve > 0f ? 4 : 6));
            var halfW = 12;
            for (int dx = -halfW; dx <= halfW; dx++)
            {
                var t = dx / (float)halfW;
                var bend = -curve * 4f * (1f - t * t); // smiling center below corners
                var y = cy + bend;
                var th = 2 + openH;
                for (int dy = 0; dy < th; dy++)
                    SetPx(px, cx + dx, Mathf.RoundToInt(y - dy));
            }
        }

        void ThickLine(Color32[] px, int x0, int y0, int x1, int y1)
        {
            for (int dy = -1; dy <= 1; dy++)
                Line(px, x0, y0 + dy, x1, y1 + dy);
        }

        void Arch(Color32[] px, int cx, int cy, int radius, int height)
        {
            for (int dx = -radius; dx <= radius; dx++)
            {
                float t = dx / (float)radius;
                int y = cy + Mathf.RoundToInt(height * (1f - t * t));
                for (int dy = -1; dy <= 1; dy++) SetPx(px, cx + dx, y + dy);
            }
        }

        void DrawOval(Color32[] px, int cx, int cy, int w, int h, Color? color = null)
        {
            if (w <= 0 || h <= 0) return;
            var fg = (Color32)(color ?? Foreground);
            for (int x = -w / 2; x <= w / 2; x++)
                for (int y = -h / 2; y <= h / 2; y++)
                {
                    if ((x * x * 4) / (float)(w * w) + (y * y * 4) / (float)(h * h) <= 1f)
                        SetPx(px, cx + x, cy + y, fg);
                }
        }

        /// <summary>Pixel heart with a deep notch and a tapered point.</summary>
        void Heart(Color32[] px, int cx, int cy)
        {
            for (int row = 0; row < HeartRows.Length; row++)
                for (int column = 0; column < HeartRows[row].Length; column++)
                    if (HeartRows[row][column] == '#')
                        SetPx(px, cx + column - 8, cy + 6 - row);
        }

        /// <summary>像素线段（斜眉等）。</summary>
        void Line(Color32[] px, int x0, int y0, int x1, int y1)
        {
            var fg = (Color32)Foreground;
            var steps = Mathf.Max(Mathf.Abs(x1 - x0), Mathf.Abs(y1 - y0));
            for (int i = 0; i <= steps; i++)
            {
                var t = steps == 0 ? 0f : i / (float)steps;
                SetPx(px, Mathf.RoundToInt(x0 + (x1 - x0) * t),
                    Mathf.RoundToInt(y0 + (y1 - y0) * t), fg);
            }
        }

        void SetPx(Color32[] px, int x, int y, Color32? c = null)
        {
            if (x < 0 || x >= W || y < 0 || y >= H) return;
            px[(H - 1 - y) * W + x] = c ?? (Color32)Foreground;
        }
    }
}

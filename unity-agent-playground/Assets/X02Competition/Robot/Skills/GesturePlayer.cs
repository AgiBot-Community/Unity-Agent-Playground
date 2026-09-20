using System;
using System.Collections.Generic;
using UnityEngine;

namespace X02Competition.Robot
{
    /// <summary>
    /// 程序化手势播放器：驱动上半身（肩/肘/腕）与头部（yaw/pitch）关节做手势动画。
    /// 关节以"基线角度 + 偏移包络"方式驱动，与步态层（仅驱动腿部 12 关节）无冲突。
    /// 注意：关节旋转轴向/符号取决于模型导入，跑起来若方向反了，调 Inspector 幅度符号。
    /// </summary>
    public class GesturePlayer : MonoBehaviour
    {
        [Header("wave_hands 挥手（仅右臂）")]
        public float WaveDuration = 2.5f;
        public float WaveShoulderLift = 95f;    // 肩 roll 侧举角（度，右臂负=外展）
        public float WaveElbowSwing = 35f;      // 肘摆动幅度（度，只能往弯的方向）
        public float WaveSwingHz = 1.4f;        // 摆动频率

        [Header("open_arms 张开双臂")]
        public float OpenArmsDuration = 3.0f;
        public float OpenShoulderDeg = 90f;     // 双肩 roll 外展（左正右负，限位 ∓171）
        public float OpenElbowDeg = 15f;        // 肘微弯（自然）

        [Tooltip("日志开关")]
        public bool Verbose = true;

        ArticulationBody _headYaw;
        readonly List<ArticulationBody> _shoulderPitch = new List<ArticulationBody>();
        readonly List<ArticulationBody> _shoulderRoll = new List<ArticulationBody>();
        readonly List<ArticulationBody> _shoulderYaw = new List<ArticulationBody>();
        readonly List<ArticulationBody> _elbows = new List<ArticulationBody>();
        readonly List<ArticulationBody> _wrists = new List<ArticulationBody>();
        Dictionary<ArticulationBody, float> _baseline;

        string _current;
        float _t;
        float _duration;
        float _diagT;
        Action<bool> _onDone;

        /// <summary>是否有手势正在播放。</summary>
        public bool IsPlaying => _current != null;

        /// <summary>中止当前手势（关节回基线，回调 false）。</summary>
        public void Abort()
        {
            if (_current != null) Finish(false);
        }

        void Awake()
        {
            var unlocked = 0;
            foreach (var b in GetComponentsInChildren<ArticulationBody>())
            {
                var n = b.gameObject.name.ToLowerInvariant();
                var wanted = n.Contains("head_yaw") ||
                    n.Contains("shoulder") || n.Contains("elbow") || n.Contains("wrist");
                if (!wanted) continue;

                // 本场景手臂/头关节被禁用为 Fixed（但保留了 URDF 的
                // xDrive 限位）。播放前改回 Revolute 恢复单自由度驱动。
                // 注：此 Unity 版本枚举无 Fixed 成员名，用数值（Fixed=0）
                if ((int)b.jointType == 0)
                {
                    b.jointType = ArticulationJointType.RevoluteJoint;
                    unlocked++;
                    if (Verbose)
                        Debug.Log("[Gesture] 解锁关节 " + b.gameObject.name +
                            " (Fixed→Revolute)");
                }
                if (b.jointType != ArticulationJointType.RevoluteJoint) continue;

                if (n.Contains("head_yaw")) _headYaw = b;
                else if (n.Contains("shoulder_pitch")) _shoulderPitch.Add(b);
                else if (n.Contains("shoulder_roll")) _shoulderRoll.Add(b);
                else if (n.Contains("shoulder_yaw")) _shoulderYaw.Add(b);
                else if (n.Contains("elbow")) _elbows.Add(b);
                else if (n.Contains("wrist")) _wrists.Add(b);
            }
            if (unlocked > 0)
                Debug.Log("[Gesture] 已解锁 " + unlocked + " 个 Fixed 上半身关节");
        }

        void Start()
        {
            _baseline = new Dictionary<ArticulationBody, float>();
            foreach (var j in AllJoints())
            {
                _baseline[j] = Mathf.Rad2Deg * j.jointPosition[0];
                // 解锁的关节初始 stiffness=0，无保位会重力下垂：
                // 上保位 PD 支撑在当前位姿（待机姿态）
                var d = j.xDrive;
                d.stiffness = 200f;
                d.damping = 10f;
                j.xDrive = d;
            }
            if (Verbose)
            {
                Debug.Log("[Gesture] 关节: headYaw=" + (_headYaw != null) +
                    " shoulderPitch=" + _shoulderPitch.Count +
                    " shoulderRoll=" + _shoulderRoll.Count +
                    " shoulderYaw=" + _shoulderYaw.Count +
                    " elbows=" + _elbows.Count + " wrists=" + _wrists.Count);
                foreach (var j in _shoulderRoll)
                {
                    var d = j.xDrive;
                    Debug.Log("[Gesture] 肩roll " + j.gameObject.name + " limit=[" +
                        d.lowerLimit + "," + d.upperLimit + "] 度");
                }
            }
        }

        IEnumerable<ArticulationBody> AllJoints()
        {
            if (_headYaw != null) yield return _headYaw;
            foreach (var j in _shoulderPitch) yield return j;
            foreach (var j in _shoulderRoll) yield return j;
            foreach (var j in _shoulderYaw) yield return j;
            foreach (var j in _elbows) yield return j;
            foreach (var j in _wrists) yield return j;
        }

        static readonly HashSet<string> KnownGestures = new HashSet<string>
        {
            "wave_hands", "open_arms",
        };

        /// <summary>播手势。返回 false=未知手势名；新播会掐断旧手势（回调 false）。</summary>
        public bool Play(string gestureName, Action<bool> onDone)
        {
            if (!KnownGestures.Contains(gestureName))
            {
                Debug.LogWarning("[Gesture] 未知手势: " + gestureName +
                    "（支持: " + string.Join("/", KnownGestures) + "）");
                return false;
            }
            if (_current != null) Finish(false); // 掐断旧手势

            _current = gestureName;
            _t = 0f;
            _duration = gestureName switch
            {
                "open_arms" => OpenArmsDuration,
                _ => WaveDuration,
            };
            _onDone = onDone;
            if (Verbose) Debug.Log("[Gesture] 播放: " + gestureName);
            return true;
        }

        void FixedUpdate()
        {
            if (_current == null) return;
            _t += Time.fixedDeltaTime;
            var p = Mathf.Clamp01(_t / _duration);
            ApplyGesture(_current, p);
            Telemetry();
            if (p >= 1f) Finish(true);
        }

        /// <summary>播放期间每 0.5s 打印关键关节 实际角→目标角/限位，定位"目标已下发但关节不动"。</summary>
        void Telemetry()
        {
            _diagT += Time.fixedDeltaTime;
            if (_diagT < 0.5f) return;
            _diagT = 0f;
            if (!Verbose) return;
            foreach (var j in _shoulderRoll)
                if (IsRight(j)) LogJoint(j, "右肩roll");
            foreach (var j in _elbows)
                if (IsRight(j)) LogJoint(j, "右肘");
        }

        void LogJoint(ArticulationBody j, string label)
        {
            var d = j.xDrive;
            Debug.Log("[Gesture遥测] " + label + " " + j.gameObject.name +
                " 实际=" + (Mathf.Rad2Deg * j.jointPosition[0]).ToString("F1") +
                "° → 目标=" + d.target.ToString("F1") +
                "° 限位=[" + d.lowerLimit.ToString("F0") + "," + d.upperLimit.ToString("F0") +
                "] k=" + d.stiffness.ToString("F0") + " f=" + d.forceLimit.ToString("F0"));
        }

        void Finish(bool completed)
        {
            // 关节回基线
            foreach (var kv in _baseline)
            {
                var j = kv.Key;
                var d = j.xDrive;
                d.stiffness = 200f;
                d.damping = 10f;
                d.target = kv.Value;
                j.xDrive = d;
            }
            var cb = _onDone;
            _current = null;
            _onDone = null;
            cb?.Invoke(completed);
        }

        // ---------------- 手势轨迹（p ∈ [0,1]） ----------------

        void ApplyGesture(string name, float p)
        {
            var env = Envelope(p);
            switch (name)
            {
                case "wave_hands":
                    // 仅右臂：肩 roll 外展举臂 + 肘往弯的方向周期摆 + 头微偏
                    foreach (var j in _shoulderRoll)
                        if (IsRight(j)) SetJoint(j, -WaveShoulderLift * env);
                    foreach (var j in _elbows)
                        if (IsRight(j)) SetJoint(j,
                            -(0.5f + 0.5f * Mathf.Sin(2f * Mathf.PI * WaveSwingHz * _t)) *
                            WaveElbowSwing * env);
                    SetJoint(_headYaw, 6f * env);
                    break;

                case "open_arms":
                    // 双肩 roll 外展（左正右负）+ 肘微弯
                    foreach (var j in _shoulderRoll)
                        SetJoint(j, Side(j) * OpenShoulderDeg * env);
                    foreach (var j in _elbows)
                        SetJoint(j, -OpenElbowDeg * env);
                    break;
            }
        }

        /// <summary>头尾各 20% 缓入缓出的包络。</summary>
        static float Envelope(float p)
        {
            var a = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(p / 0.2f));
            var b = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((1f - p) / 0.2f));
            return a * b;
        }

        static float Side(ArticulationBody joint)
        {
            // 左右关节对称（镜像），基线之上叠加同向"外侧"偏移
            return joint.gameObject.name.ToLowerInvariant().Contains("left") ? 1f : -1f;
        }

        static bool IsRight(ArticulationBody joint)
        {
            return joint.gameObject.name.ToLowerInvariant().Contains("right");
        }

        void SetJoint(ArticulationBody joint, float offsetDeg, float stiffness = 200f)
        {
            if (joint == null || _baseline == null || !_baseline.ContainsKey(joint)) return;
            var d = joint.xDrive;
            d.stiffness = stiffness;
            d.damping = 10f;
            d.target = _baseline[joint] + offsetDeg;
            joint.xDrive = d;
        }
    }
}

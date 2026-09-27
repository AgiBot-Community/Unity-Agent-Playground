using System;
using System.Collections.Generic;
using UnityEngine;

namespace X02Competition.Robot
{
    /// <summary>
    /// Coordinated upper-body gestures. Targets are baseline-relative, smoothed and limited;
    /// locomotion retains ownership of the root, spine and legs.
    /// </summary>
    public class GesturePlayer : MonoBehaviour
    {
        [Header("wave_hands：抬手、轻摆、收回")]
        [Min(2f)] public float WaveDuration = GestureMotion.DefaultWaveDuration;
        [Range(20f, 90f)] public float WaveShoulderLift = 55f;
        [Range(60f, 115f)] public float WaveElbowBend = 100f;
        [Range(0f, 15f)] public float WaveElbowSwing = 8f;
        [Range(0.4f, 1.5f)] public float WaveSwingHz = 1f;
        [Range(0f, 25f)] public float WaveWristSwing = 16f;

        [Header("open_arms：舒展、停留、收回")]
        [Min(2f)] public float OpenArmsDuration = GestureMotion.DefaultOpenDuration;
        [Range(25f, 85f)] public float OpenShoulderDeg = 55f;
        [Range(5f, 40f)] public float OpenElbowDeg = 32f;

        [Header("过渡")]
        [Min(0.1f)] public float EntryBlendSeconds = 0.65f;
        [Min(0.1f)] public float ReturnSeconds = 0.8f;
        [Min(0.02f)] public float TargetSmoothSeconds = 0.12f;
        [Min(10f)] public float MaxTargetSpeed = 110f;
        public bool Verbose = true;

        enum JointKind { None, HeadYaw, ShoulderPitch, ShoulderRoll, ShoulderYaw, Elbow, WristYaw, WristPitch, WristRoll }
        sealed class JointState
        {
            public ArticulationBody Body;
            public JointKind Kind;
            public bool Right;
            public float Baseline, Target, Velocity, Entry;
        }

        readonly List<JointState> _joints = new List<JointState>();
        static readonly HashSet<string> KnownGestures = new HashSet<string> { "wave_hands", "open_arms" };
        bool _ready, _returning;
        string _current;
        float _t, _duration, _diagT;
        Action<bool> _onDone;

        public bool IsPlaying => _current != null || _returning;

        /// <summary>Episode reset teleports joints, so cancel the old trajectory and align its targets.</summary>
        public void ResetToRest()
        {
            if (_ready) Finish(false);
        }

        void Awake()
        {
            var unlocked = 0;
            foreach (var body in GetComponentsInChildren<ArticulationBody>())
            {
                var name = body.gameObject.name.ToLowerInvariant();
                var kind = KindOf(name);
                if (kind == JointKind.None) continue;
                if (kind != JointKind.HeadYaw && !name.Contains("left") && !name.Contains("right")) continue;
                // The imported upper-body joints start fixed but retain their URDF drive limits.
                if ((int)body.jointType == 0)
                {
                    body.jointType = ArticulationJointType.RevoluteJoint;
                    unlocked++;
                }
                if (body.jointType != ArticulationJointType.RevoluteJoint) continue;
                _joints.Add(new JointState { Body = body, Kind = kind, Right = name.Contains("right") });
            }
            if (Verbose) Debug.Log("[Gesture] upper-body joints=" + _joints.Count + ", unlocked=" + unlocked);
        }

        void Start()
        {
            EnsureReady();
            if (!Verbose) return;
            foreach (var joint in _joints)
            {
                var drive = joint.Body.xDrive;
                Debug.Log("[Gesture] " + joint.Body.name + " limit=[" +
                    drive.lowerLimit + "," + drive.upperLimit + "] deg");
            }
        }

        void EnsureReady()
        {
            if (_ready) return;
            foreach (var joint in _joints)
            {
                var drive = joint.Body.xDrive;
                var position = joint.Body.jointPosition;
                joint.Baseline = ClampTarget(drive, position.dofCount > 0 ? position[0] * Mathf.Rad2Deg : drive.target);
                joint.Target = joint.Entry = joint.Baseline;
                drive.target = joint.Baseline;
                drive.targetVelocity = 0f;
                drive.stiffness = 200f;
                drive.damping = 12f;
                joint.Body.xDrive = drive;
            }
            _ready = true;
        }

        /// <summary>Start from the current target pose; replacement cancels the previous callback once.</summary>
        public bool Play(string gestureName, Action<bool> onDone)
        {
            if (!KnownGestures.Contains(gestureName))
            {
                Debug.LogWarning("[Gesture] Unknown gesture: " + gestureName);
                return false;
            }
            EnsureReady();
            if (!HasArm(gestureName == "wave_hands"))
            {
                Debug.LogWarning("[Gesture] Missing required arm joints for " + gestureName);
                return false;
            }
            var replaced = _onDone;
            CaptureEntry();
            _current = gestureName;
            _returning = false;
            _t = _diagT = 0f;
            _duration = gestureName == "open_arms"
                ? Duration(OpenArmsDuration, GestureMotion.DefaultOpenDuration)
                : Duration(WaveDuration, GestureMotion.DefaultWaveDuration);
            _onDone = onDone;
            replaced?.Invoke(false);
            if (Verbose) Debug.Log("[Gesture] 播放: " + gestureName + " duration=" + _duration.ToString("F1") + "s");
            return true;
        }

        /// <summary>Cancel the gesture immediately, then smoothly return the upper body to rest.</summary>
        public void Abort()
        {
            if (_current == null) return;
            var callback = _onDone;
            _onDone = null;
            _current = null;
            _returning = true;
            _t = 0f;
            CaptureEntry();
            callback?.Invoke(false);
        }

        void FixedUpdate()
        {
            if (!IsPlaying) return;
            _t += Time.fixedDeltaTime;
            var duration = _returning ? Mathf.Max(0.1f, ReturnSeconds) : _duration;
            var p = Mathf.Clamp01(_t / duration);
            var pose = _returning ? default : SamplePose(_current, p);
            var blend = GestureMotion.Ease(_t / (_returning ? duration : Mathf.Max(0.1f, EntryBlendSeconds)));
            var settled = ApplyPose(pose, blend);
            Telemetry();
            if (p >= 1f && settled) Finish(!_returning);
        }

        public GesturePose SamplePose(string name, float progress)
        {
            return name == "wave_hands"
                ? GestureMotion.Wave(progress, Duration(WaveDuration, GestureMotion.DefaultWaveDuration),
                    WaveShoulderLift, WaveElbowSwing, WaveSwingHz, WaveElbowBend, WaveWristSwing)
                : name == "open_arms" ? GestureMotion.OpenArms(progress, OpenShoulderDeg, OpenElbowDeg) : default;
        }

        void CaptureEntry()
        {
            foreach (var joint in _joints) joint.Entry = joint.Target;
        }

        bool HasArm(bool wave)
        {
            bool right = false, left = false, elbow = false;
            foreach (var joint in _joints)
            {
                if (joint.Body == null) continue;
                if (joint.Kind == JointKind.ShoulderRoll) { if (joint.Right) right = true; else left = true; }
                if (joint.Kind == JointKind.Elbow && joint.Right) elbow = true;
            }
            return wave ? right && elbow : right && left;
        }

        bool ApplyPose(GesturePose pose, float blend)
        {
            bool settled = true;
            foreach (var joint in _joints)
            {
                if (joint.Body == null) continue;
                var drive = joint.Body.xDrive;
                var goal = joint.Baseline + Offset(pose, joint.Kind, joint.Right);
                goal = ClampTarget(drive, Mathf.Lerp(joint.Entry, goal, blend));
                joint.Target = Mathf.SmoothDamp(joint.Target, goal, ref joint.Velocity,
                    Mathf.Max(0.02f, TargetSmoothSeconds), Mathf.Max(10f, MaxTargetSpeed), Time.fixedDeltaTime);
                joint.Target = ClampTarget(drive, joint.Target);
                drive.target = joint.Target;
                drive.targetVelocity = 0f;
                joint.Body.xDrive = drive;
                if (Mathf.Abs(joint.Target - joint.Baseline) > 0.08f || Mathf.Abs(joint.Velocity) > 0.5f)
                    settled = false;
            }
            return settled;
        }

        void Finish(bool completed)
        {
            foreach (var joint in _joints)
            {
                if (joint.Body == null) continue;
                var drive = joint.Body.xDrive;
                joint.Target = joint.Baseline;
                joint.Velocity = 0f;
                drive.target = joint.Baseline;
                drive.targetVelocity = 0f;
                joint.Body.xDrive = drive;
            }
            var callback = _onDone;
            _current = null;
            _returning = false;
            _onDone = null;
            callback?.Invoke(completed);
        }

        void Telemetry()
        {
            _diagT += Time.fixedDeltaTime;
            if (_diagT < 0.5f || !Verbose) return;
            _diagT = 0f;
            foreach (var joint in _joints)
            {
                if (joint.Body == null || !joint.Right) continue;
                if (joint.Kind != JointKind.ShoulderRoll && joint.Kind != JointKind.Elbow && joint.Kind != JointKind.WristRoll) continue;
                var drive = joint.Body.xDrive;
                Debug.Log("[Gesture遥测] " + joint.Kind + " " + joint.Body.name +
                    " 实际=" + (joint.Body.jointPosition[0] * Mathf.Rad2Deg).ToString("F1") +
                    "° → 目标=" + drive.target.ToString("F1") + "°");
            }
        }

        static float Duration(float value, float fallback) =>
            float.IsNaN(value) || float.IsInfinity(value) ? fallback : Mathf.Max(2f, value);

        static float ClampTarget(ArticulationDrive drive, float target)
        {
            if (float.IsNaN(target) || float.IsInfinity(target)) target = 0f;
            return Mathf.Clamp(target, drive.lowerLimit, drive.upperLimit);
        }

        static JointKind KindOf(string name)
        {
            if (name.Contains("head_yaw")) return JointKind.HeadYaw;
            if (name.Contains("shoulder_pitch")) return JointKind.ShoulderPitch;
            if (name.Contains("shoulder_roll")) return JointKind.ShoulderRoll;
            if (name.Contains("shoulder_yaw")) return JointKind.ShoulderYaw;
            if (name.Contains("elbow")) return JointKind.Elbow;
            if (name.Contains("wrist_yaw")) return JointKind.WristYaw;
            if (name.Contains("wrist_pitch")) return JointKind.WristPitch;
            if (name.Contains("wrist_roll")) return JointKind.WristRoll;
            return JointKind.None;
        }

        static float Offset(GesturePose pose, JointKind kind, bool right)
        {
            var arm = right ? pose.Right : pose.Left;
            switch (kind)
            {
                case JointKind.HeadYaw: return pose.HeadYaw;
                case JointKind.ShoulderPitch: return arm.ShoulderPitch;
                case JointKind.ShoulderRoll: return arm.ShoulderRoll;
                case JointKind.ShoulderYaw: return arm.ShoulderYaw;
                case JointKind.Elbow: return arm.Elbow;
                case JointKind.WristYaw: return arm.WristYaw;
                case JointKind.WristPitch: return arm.WristPitch;
                case JointKind.WristRoll: return arm.WristRoll;
                default: return 0f;
            }
        }

        void OnDisable()
        {
            if (_ready) Finish(false);
        }
    }
}

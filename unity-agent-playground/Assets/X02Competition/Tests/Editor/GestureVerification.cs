#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using X02Competition.Bootstrap;
using X02Competition.Robot;

/// <summary>Real-scene gesture playback, target continuity, interruption and episode-reset checks.</summary>
[InitializeOnLoad]
public static class GestureVerification
{
    const string Pending = "X2.GestureVerification.Pending";
    static CompetitionLauncher _launcher;
    static GesturePlayer _gestures;
    static X02newAgent _agent;
    static ArticulationBody _body;
    static ArticulationBody[] _upper;
    static Dictionary<ArticulationBody, float> _rest, _previous;
    static readonly List<string> _samples = new List<string>();
    static readonly HashSet<string> _images = new HashSet<string>();
    static string _output, _phase;
    static double _phaseStart, _lastSample, _deadline;
    static Vector3 _startPosition;
    static float _maxTilt, _maxDrift, _maxTargetSpeed, _wristMin, _wristMax;
    static int _falls, _topologyChanges, _checks, _waveDone, _openDone, _cancelled, _resetCancelled;
    static bool _initialized, _finished;

    static GestureVerification()
    {
        if (SessionState.GetBool(Pending, false)) Subscribe();
    }

    static void Subscribe()
    {
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
        Application.logMessageReceived -= OnLog;
        Application.logMessageReceived += OnLog;
    }

    public static void Run()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Use a separate batch editor.");
        EditorSceneManager.OpenScene("Assets/X02Competition/Scenes/scene.unity", OpenSceneMode.Single);
        var launcher = UnityEngine.Object.FindObjectOfType<CompetitionLauncher>();
        if (launcher == null) throw new Exception("Scene launcher missing.");
        launcher.Port = 0;
        launcher.AutoBeginInput = false;
        launcher.ShowDebugHud = false;
        launcher.Mode = CompetitionLauncher.InputMode.TestClip;
        SessionState.SetBool(Pending, true);
        Subscribe();
        EditorApplication.EnterPlaymode();
    }

    static void Require(bool condition, string message)
    {
        _checks++;
        if (!condition) throw new Exception("Gesture verification: " + message);
    }

    static void OnLog(string message, string stack, LogType type)
    {
        if (message.Contains("Episode reset: fall protection")) _falls++;
        if (message.Contains("Articulation topology changed after initialization")) _topologyChanges++;
    }

    static float[] Values(GesturePose p) => new[]
    {
        p.Left.ShoulderPitch, p.Left.ShoulderRoll, p.Left.ShoulderYaw, p.Left.Elbow,
        p.Left.WristYaw, p.Left.WristPitch, p.Left.WristRoll,
        p.Right.ShoulderPitch, p.Right.ShoulderRoll, p.Right.ShoulderYaw, p.Right.Elbow,
        p.Right.WristYaw, p.Right.WristPitch, p.Right.WristRoll, p.HeadYaw
    };

    static void VerifyCurves()
    {
        foreach (var name in new[] { "wave_hands", "open_arms" })
        {
            Require(Values(_gestures.SamplePose(name, 0)).All(v => Mathf.Abs(v) < 0.0001f), name + " start is not neutral");
            Require(Values(_gestures.SamplePose(name, 1)).All(v => Mathf.Abs(v) < 0.0001f), name + " end is not neutral");
            var previous = Values(_gestures.SamplePose(name, 0));
            var duration = name == "wave_hands" ? _gestures.WaveDuration : _gestures.OpenArmsDuration;
            for (var i = 1; i <= 1000; i++)
            {
                var pose = _gestures.SamplePose(name, i / 1000f);
                var values = Values(pose);
                Require(values.All(v => !float.IsNaN(v) && !float.IsInfinity(v)), "non-finite pose");
                Require(pose.Left.ShoulderRoll >= -0.001f && pose.Right.ShoulderRoll <= 0.001f,
                    "shoulders crossed inward");
                Require(pose.Left.Elbow <= 0.001f && pose.Right.Elbow <= 0.001f &&
                    pose.Left.Elbow >= -130f && pose.Right.Elbow >= -130f, "elbow outside flexion range");
                for (var j = 0; j < values.Length; j++)
                    Require(Mathf.Abs(values[j] - previous[j]) / (duration / 1000f) < 220f, "raw curve has an abrupt step");
                previous = values;
            }
            Require(Values(_gestures.SamplePose(name, 0.0001f)).Max(v => Mathf.Abs(v)) < 0.0001f,
                "initial easing is not soft");
        }
        var midpoint = _gestures.SamplePose("wave_hands", 0.5f);
        Require(midpoint.Right.Elbow < -80 && midpoint.Right.ShoulderRoll > -80, "wave should use a bent elbow, not a straight lateral arm");
        var open = _gestures.SamplePose("open_arms", 0.5f);
        Require(open.Left.ShoulderRoll > 50 && open.Right.ShoulderRoll < -50, "open gesture not wide enough");
        Require(Mathf.Abs(open.Left.ShoulderRoll + open.Right.ShoulderRoll) < 1f, "open hold is unbalanced");
    }

    static void SetPhase(string phase)
    {
        _phase = phase;
        _phaseStart = Time.timeAsDouble;
        Debug.Log("[GestureVerification] phase=" + phase);
    }

    static Dictionary<ArticulationBody, float> Targets() =>
        _upper.ToDictionary(j => j, j => j.xDrive.target);

    static void NoJump(Dictionary<ArticulationBody, float> before, string operation)
    {
        Require(_upper.All(j => Mathf.Abs(j.xDrive.target - before[j]) < 0.0001f),
            operation + " changed targets synchronously");
    }

    static void Tick()
    {
        if (_finished || !EditorApplication.isPlaying || EditorApplication.isCompiling) return;
        try
        {
            if (!_initialized)
            {
                _launcher = UnityEngine.Object.FindObjectOfType<CompetitionLauncher>();
                if (_launcher == null || _launcher.Gestures == null) return;
                _launcher.Runtime.Input?.End();
                _launcher.Runtime.StatePublishInterval = 0;
                _gestures = _launcher.Gestures;
                _agent = UnityEngine.Object.FindObjectOfType<X02newAgent>();
                _body = _agent.GetComponentsInChildren<ArticulationBody>().First(b => b.isRoot);
                _upper = _gestures.GetComponentsInChildren<ArticulationBody>().Where(j =>
                    j.name.Contains("shoulder") || j.name.Contains("elbow") || j.name.Contains("wrist") || j.name.Contains("head_yaw")).ToArray();
                Require(_upper.Length >= 15, "upper body bindings missing");
                _output = Path.GetFullPath(Path.Combine(Application.dataPath, "../../.diagnostics/gesture-polish"));
                Directory.CreateDirectory(_output);
                _deadline = EditorApplication.timeSinceStartup + 100;
                _samples.Add("phase,time,joint,target_deg,actual_deg,root_pitch,root_roll");
                _initialized = true;
                VerifyCurves();
                SetPhase("warmup");
            }
            Require(EditorApplication.timeSinceStartup < _deadline, "test timeout");
            var elapsed = Time.timeAsDouble - _phaseStart;
            if (_phase == "warmup")
            {
                if (elapsed < 2) return;
                _rest = Targets();
                _previous = Targets();
                _lastSample = Time.fixedTimeAsDouble;
                _startPosition = _body.transform.position;
                Capture("rest");
                Require(_gestures.Play("wave_hands", ok => { Require(ok, "wave failed"); _waveDone++; }), "wave not accepted");
                SetPhase("wave");
                return;
            }

            Sample();
            Require(_falls == 0, "robot fell during gesture playback");
            Require(_topologyChanges == 0, "upper-body topology changed after policy initialization");
            if (_phase == "wave" || _phase == "open")
            {
                foreach (var sampleTime in new[] { 1.3, 2.8, 4.2, 5.5 })
                    if (elapsed >= sampleTime && _images.Add(_phase + "-" + sampleTime.ToString("F1", CultureInfo.InvariantCulture)))
                        Capture(_phase + "-" + sampleTime.ToString("F1", CultureInfo.InvariantCulture));
                var expected = _phase == "wave" ? _gestures.WaveDuration : _gestures.OpenArmsDuration;
                Require(elapsed < expected + 2, _phase + " never finished");
                if (!_gestures.IsPlaying)
                {
                    Require(elapsed >= expected - 0.1, "gesture finished early");
                    Require(_upper.All(j => Mathf.Abs(j.xDrive.target - _rest[j]) < 0.1f), "targets did not return to rest");
                    if (_phase == "wave")
                    {
                        Require(_waveDone == 1, "wave callback count");
                        Require(_wristMax - _wristMin > 15, "wrist did not participate in wave");
                        SetPhase("between");
                    }
                    else
                    {
                        Require(_openDone == 1, "open callback count");
                        SetPhase("before-chain");
                    }
                }
            }
            else if (_phase == "between" && elapsed > 1)
            {
                Require(_gestures.Play("open_arms", ok => { Require(ok, "open failed"); _openDone++; }), "open not accepted");
                SetPhase("open");
            }
            else if (_phase == "before-chain" && elapsed > 1)
            {
                _gestures.Play("wave_hands", ok => { Require(!ok, "replaced wave reported done"); _cancelled++; });
                SetPhase("chain-wave");
            }
            else if (_phase == "chain-wave" && elapsed > 1.8)
            {
                var before = Targets();
                _gestures.Play("open_arms", ok => { Require(!ok, "cancelled open reported done"); _cancelled++; });
                NoJump(before, "replacement");
                Require(_cancelled == 1, "replacement callback count");
                SetPhase("chain-open");
            }
            else if (_phase == "chain-open" && elapsed > 1.5)
            {
                var before = Targets();
                _gestures.Abort();
                _gestures.Abort();
                NoJump(before, "abort");
                Require(_cancelled == 2 && _gestures.IsPlaying, "abort should notify once and continue a smooth return");
                SetPhase("return");
            }
            else if (_phase == "return")
            {
                Require(elapsed < 3, "return never finished");
                if (!_gestures.IsPlaying)
                {
                    Require(_cancelled == 2, "return duplicated cancellation");
                    _gestures.Play("wave_hands", ok => { Require(!ok, "reset wave reported done"); _resetCancelled++; });
                    SetPhase("before-reset");
                }
            }
            else if (_phase == "before-reset" && elapsed > 2)
            {
                _agent.OnEpisodeBegin();
                Require(!_gestures.IsPlaying && _resetCancelled == 1, "episode reset retained old gesture");
                Require(_upper.All(j => Mathf.Abs(j.xDrive.target - _rest[j]) < 0.1f), "reset targets not neutral");
                _previous = Targets(); // Teleport is intentionally outside the smooth-motion rate check.
                _lastSample = Time.fixedTimeAsDouble;
                SetPhase("after-reset");
            }
            else if (_phase == "after-reset" && elapsed > 1.5)
            {
                Require(!_gestures.IsPlaying, "reset gesture resumed");
                Require(_maxTilt < 22f, "excessive body tilt");
                Require(_maxDrift < 0.4f, "excessive standing drift");
                Capture("final-rest");
                File.WriteAllLines(Path.Combine(_output, "joint-samples.csv"), _samples);
                File.WriteAllText(Path.Combine(_output, "metrics.txt"),
                    $"checks={_checks}\nmaxTilt={_maxTilt:F3}\nmaxDrift={_maxDrift:F3}\nmaxTargetSpeed={_maxTargetSpeed:F3}\n" +
                    $"waveWristRange={_wristMax - _wristMin:F3}\nfalls={_falls}\ntopologyChanges={_topologyChanges}\n");
                Debug.Log($"GESTURE_VERIFICATION_PASSED: checks={_checks}, tilt={_maxTilt:F2}deg, drift={_maxDrift:F3}m, " +
                    $"targetSpeed={_maxTargetSpeed:F2}deg/s, wristRange={_wristMax - _wristMin:F2}deg, callbacks/reset/limits/continuity.");
                Finish(0);
            }
        }
        catch (Exception error)
        {
            Debug.LogException(error);
            if (_output != null) File.WriteAllLines(Path.Combine(_output, "joint-samples.csv"), _samples);
            Finish(1);
        }
    }

    static void Sample()
    {
        var time = Time.fixedTimeAsDouble;
        var dt = time - _lastSample;
        if (dt <= 0) return;
        var pitch = Mathf.DeltaAngle(0, _body.transform.eulerAngles.x);
        var roll = Mathf.DeltaAngle(0, _body.transform.eulerAngles.z);
        _maxTilt = Mathf.Max(_maxTilt, Mathf.Abs(pitch), Mathf.Abs(roll));
        _maxDrift = Mathf.Max(_maxDrift, Vector3.ProjectOnPlane(_body.transform.position - _startPosition, Vector3.up).magnitude);
        foreach (var joint in _upper)
        {
            var drive = joint.xDrive;
            Require(!float.IsNaN(drive.target) && !float.IsInfinity(drive.target), "non-finite target");
            Require(drive.target >= drive.lowerLimit - 0.001f && drive.target <= drive.upperLimit + 0.001f,
                "target exceeded limit: " + joint.name);
            var speed = Mathf.Abs(drive.target - _previous[joint]) / (float)dt;
            _maxTargetSpeed = Mathf.Max(_maxTargetSpeed, speed);
            Require(speed <= _gestures.MaxTargetSpeed + 5, "target jumped: " + joint.name + " speed=" + speed);
            _previous[joint] = drive.target;
            var actual = joint.jointPosition.dofCount > 0 ? joint.jointPosition[0] * Mathf.Rad2Deg : 0;
            if (_phase == "wave" && joint.name == "right_wrist_roll_link")
            {
                _wristMin = Mathf.Min(_wristMin, actual);
                _wristMax = Mathf.Max(_wristMax, actual);
            }
            _samples.Add(string.Format(CultureInfo.InvariantCulture, "{0},{1:F3},{2},{3:F3},{4:F3},{5:F3},{6:F3}",
                _phase, time, joint.name, drive.target, actual, pitch, roll));
        }
        _lastSample = time;
    }

    static void Capture(string name)
    {
        foreach (var view in new[] { 1, 3 })
        {
            _launcher.Cameras.SetView(view);
            var camera = _launcher.Cameras.ActiveCamera;
            var target = new RenderTexture(1280, 960, 24);
            target.Create();
            var oldActive = RenderTexture.active;
            var oldTarget = camera.targetTexture;
            var oldAspect = camera.aspect;
            var image = new Texture2D(1280, 960, TextureFormat.RGB24, false);
            try
            {
                camera.aspect = 4f / 3f;
                if (GraphicsSettings.currentRenderPipeline != null)
                    RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
                else
                {
                    camera.targetTexture = target;
                    camera.Render();
                }
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, 1280, 960), 0, 0);
                image.Apply();
                File.WriteAllBytes(Path.Combine(_output, name + "-view" + view + ".png"), image.EncodeToPNG());
            }
            finally
            {
                camera.aspect = oldAspect;
                camera.targetTexture = oldTarget;
                RenderTexture.active = oldActive;
                target.Release();
                UnityEngine.Object.DestroyImmediate(target);
                UnityEngine.Object.DestroyImmediate(image);
            }
        }
        _launcher.Cameras.SetView(1);
    }

    static void Finish(int code)
    {
        _finished = true;
        SessionState.SetBool(Pending, false);
        EditorApplication.update -= Tick;
        Application.logMessageReceived -= OnLog;
        // Notify/dispose ML-Agents while its articulation state is still alive.
        if (_gestures != null)
        {
            _gestures.ResetToRest();
            _gestures.enabled = false;
        }
        if (_agent != null) _agent.enabled = false;
        // Disposing individual policies does not dispose the Academy's shared inference workers.
        if (Unity.MLAgents.Academy.IsInitialized) Unity.MLAgents.Academy.Instance.Dispose();
        _launcher?.Server?.Stop();
        Lightmapping.Cancel();
        EditorApplication.Exit(code);
    }
}
#endif

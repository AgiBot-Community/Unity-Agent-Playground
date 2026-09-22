using UnityEngine;

namespace X02Competition.Bootstrap
{
    /// <summary>Four views with bounds-based framing, independent of robot control.</summary>
    [DefaultExecutionOrder(300)]
    public sealed class RobotCameraRig : MonoBehaviour
    {
        public static readonly string[] ViewNames = { "全景观察", "正面跟随", "侧面跟随", "自由环绕" };
        public int ViewIndex { get; private set; } = 1;
        public Camera ActiveCamera => _cameras[ViewIndex];
        public Rect SafeFrame { get; private set; }
        public Bounds RobotBounds { get; private set; }
        public bool Ready => _target != null && ActiveCamera != null;

        readonly Camera[] _cameras = new Camera[4];
        readonly AudioListener[] _listeners = new AudioListener[4];
        Renderer[] _renderers;
        Transform _target;
        DebugHud _hud;
        Camera _original;
        Behaviour _oldFollow;
        bool _oldFollowEnabled, _originalEnabled, _originalListenerEnabled;
        Vector3 _originalPosition;
        Quaternion _originalRotation;
        float _originalFov, _originalNear, _originalFar;
        bool _originalOrthographic;
        Rect _originalRect;
        Bounds _startBounds;
        Vector3 _focus;
        float _frontYaw, _orbitYaw, _orbitPitch = 18f, _zoom = 1f, _distance;

        public void Initialize(Transform target, DebugHud hud)
        {
            if (_original != null || target == null) return;
            var source = Camera.main;
            if (source == null) { Debug.LogWarning("[CameraRig] 缺少 MainCamera，无法创建观察视角。"); return; }
            _target = target;
            _hud = hud;
            _renderers = target.GetComponentsInChildren<Renderer>(true);
            _original = source;
            _originalPosition = source.transform.position;
            _originalRotation = source.transform.rotation;
            _originalFov = source.fieldOfView;
            _originalNear = source.nearClipPlane;
            _originalFar = source.farClipPlane;
            _originalOrthographic = source.orthographic;
            _originalRect = source.rect;
            _originalEnabled = source.enabled;
            _startBounds = MeasureRobot();
            _focus = _startBounds.center;
            var facing = source.transform.position - _focus;
            _frontYaw = Mathf.Atan2(facing.x, facing.z) * Mathf.Rad2Deg;
            _orbitYaw = _frontYaw - 35f;

            foreach (var behaviour in source.GetComponents<MonoBehaviour>())
                if (behaviour.GetType().FullName == "Unity.MLAgentsExamples.CameraFollow")
                { _oldFollow = behaviour; _oldFollowEnabled = behaviour.enabled; behaviour.enabled = false; }

            _cameras[0] = source;
            _listeners[0] = source.GetComponent<AudioListener>();
            _originalListenerEnabled = _listeners[0] != null && _listeners[0].enabled;
            for (var i = 1; i < _cameras.Length; i++)
            {
                var go = new GameObject("X2 Camera / " + ViewNames[i]);
                go.transform.SetParent(transform, false);
                go.tag = "MainCamera";
                var camera = go.AddComponent<Camera>();
                camera.CopyFrom(source);
                camera.enabled = false;
                // Preserve URP renderer settings without introducing an assembly dependency.
                foreach (var component in source.GetComponents<MonoBehaviour>())
                    if (component.GetType().Name == "UniversalAdditionalCameraData")
                        JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(component), go.AddComponent(component.GetType()));
                _cameras[i] = camera;
                if (_listeners[0] != null) { _listeners[i] = go.AddComponent<AudioListener>(); _listeners[i].enabled = false; }
            }
            foreach (var camera in _cameras)
            {
                camera.orthographic = false;
                camera.fieldOfView = 48f;
                camera.nearClipPlane = 0.05f;
                camera.rect = new Rect(0, 0, 1, 1);
            }
            SetView(ViewIndex);
        }

        public void SetView(int index)
        {
            if (_original == null) return;
            ViewIndex = Mathf.Clamp(index, 0, _cameras.Length - 1);
            for (var i = 0; i < _cameras.Length; i++)
            {
                _cameras[i].enabled = i == ViewIndex;
                if (_listeners[i] != null) _listeners[i].enabled = i == ViewIndex && _originalListenerEnabled;
            }
            _focus = MeasureRobot().center;
            _distance = 0;
            UpdateFraming(0, _hud != null ? _hud.OccludedWidthFraction : 0);
        }

        void Update()
        {
            if (!Ready) return;
            if (Input.GetKeyDown(KeyCode.C)) SetView((ViewIndex + 1) % _cameras.Length);
            if (Input.GetKeyDown(KeyCode.F2)) SetView(0);
            if (Input.GetKeyDown(KeyCode.F3)) SetView(1);
            if (Input.GetKeyDown(KeyCode.F4)) SetView(2);
            if (Input.GetKeyDown(KeyCode.F5)) SetView(3);
            if (ViewIndex != 3 || (_hud != null && _hud.PointerOverHud)) return;
            if (Input.GetMouseButton(1))
            {
                _orbitYaw += Input.GetAxis("Mouse X") * 3f;
                _orbitPitch = Mathf.Clamp(_orbitPitch - Input.GetAxis("Mouse Y") * 2f, 8f, 65f);
            }
            _zoom = Mathf.Clamp(_zoom - Input.mouseScrollDelta.y * 0.12f, 1f, 2.8f);
        }

        void LateUpdate()
        {
            if (Ready) UpdateFraming(Time.unscaledDeltaTime, _hud != null ? _hud.OccludedWidthFraction : 0);
        }

        Bounds MeasureRobot()
        {
            var bounds = new Bounds(_target.position + Vector3.up, new Vector3(1, 2, 1));
            var found = false;
            foreach (var renderer in _renderers)
            {
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy ||
                    renderer is ParticleSystemRenderer || renderer is LineRenderer) continue;
                if (!found) { bounds = renderer.bounds; found = true; }
                else bounds.Encapsulate(renderer.bounds);
            }
            bounds.Expand(0.12f);
            return bounds;
        }

        void UpdateFraming(float deltaTime, float occupiedFraction)
        {
            var camera = ActiveCamera;
            RobotBounds = MeasureRobot();
            var framedBounds = RobotBounds;
            if (ViewIndex == 0) framedBounds.Encapsulate(_startBounds);
            var centre = framedBounds.center;
            var smoothing = 1f - Mathf.Exp(-5f * deltaTime);
            if (deltaTime <= 0 || Vector3.Distance(_focus, centre) > RobotBounds.size.magnitude * 4f)
                _focus = centre;
            else
            {
                // The robot may travel inside a small dead zone before the camera
                // pans. Ground parallax and a fixed world heading retain motion cues.
                var displacement = centre - _focus;
                var deadZone = ViewIndex == 0 ? 0 : Mathf.Max(0.15f, RobotBounds.size.y * 0.15f);
                var destination = centre - Vector3.ClampMagnitude(displacement, deadZone);
                destination.y = centre.y;
                _focus = Vector3.Lerp(_focus, destination, smoothing);
            }

            var yaw = ViewIndex == 0 ? _frontYaw - 40f : ViewIndex == 2 ? _frontYaw + 90f : ViewIndex == 3 ? _orbitYaw : _frontYaw;
            var pitch = ViewIndex == 0 ? 38f : ViewIndex == 2 ? 16f : ViewIndex == 3 ? _orbitPitch : 12f;
            var outward = Quaternion.Euler(-pitch, yaw, 0) * Vector3.forward;
            var rotation = Quaternion.LookRotation(-outward, Vector3.up);
            camera.transform.rotation = rotation;
            // On narrow windows the HUD can occupy most of the display; the rig
            // still keeps the robot in frame, and F1 restores the wider scene view.
            var left = Mathf.Clamp(occupiedFraction + 0.04f, 0.08f, 0.78f);
            SafeFrame = Rect.MinMaxRect(left, 0.10f, 0.94f, 0.90f);
            var tanY = Mathf.Tan(camera.fieldOfView * Mathf.Deg2Rad * 0.5f);
            var tanX = tanY * Mathf.Max(0.1f, camera.aspect);
            var l = (SafeFrame.xMin * 2 - 1) * tanX;
            var r = (SafeFrame.xMax * 2 - 1) * tanX;
            var b = (SafeFrame.yMin * 2 - 1) * tanY;
            var t = (SafeFrame.yMax * 2 - 1) * tanY;
            var midX = (l + r) * 0.5f;
            var midY = (b + t) * 0.5f;
            var inverse = Quaternion.Inverse(rotation);
            var required = Mathf.Max(2f, RobotBounds.size.y * (ViewIndex == 0 ? 3f : 1.8f));
            // Solve all eight bounds corners against the asymmetric safe frustum.
            // This also compensates immediately for walking, turns and episode resets.
            for (var i = 0; i < 8; i++)
            {
                var point = framedBounds.center + Vector3.Scale(framedBounds.extents,
                    new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                var p = inverse * (point - _focus);
                required = Mathf.Max(required, (l * p.z - p.x) / (midX - l), (p.x - r * p.z) / (r - midX));
                required = Mathf.Max(required, (b * p.z - p.y) / (midY - b), (p.y - t * p.z) / (t - midY));
                required = Mathf.Max(required, camera.nearClipPlane - p.z + 0.2f);
            }
            if (ViewIndex == 3) required *= _zoom;
            // Zoom out immediately for safety; only the return inward is smoothed.
            _distance = Mathf.Max(required, Mathf.Lerp(_distance, required, smoothing));
            camera.transform.position = _focus + outward * _distance -
                camera.transform.right * (midX * _distance) - camera.transform.up * (midY * _distance);
            camera.farClipPlane = Mathf.Max(_originalFar, _distance + framedBounds.size.magnitude + 100);
        }

        void OnDestroy()
        {
            for (var i = 1; i < _cameras.Length; i++) if (_cameras[i] != null) Destroy(_cameras[i].gameObject);
            if (_original != null)
            {
                _original.transform.SetPositionAndRotation(_originalPosition, _originalRotation);
                _original.enabled = _originalEnabled;
                _original.fieldOfView = _originalFov;
                _original.nearClipPlane = _originalNear;
                _original.farClipPlane = _originalFar;
                _original.orthographic = _originalOrthographic;
                _original.rect = _originalRect;
                if (_listeners[0] != null) _listeners[0].enabled = _originalListenerEnabled;
            }
            if (_oldFollow != null) _oldFollow.enabled = _oldFollowEnabled;
        }
    }
}

using UnityEngine;
using X02Competition.Gateway;
using X02Competition.Robot;

namespace X02Competition.Bootstrap
{
    /// <summary>
    /// 竞赛场景入口：场景里放一个空物体挂本组件即完成全部装配。
    /// 装配链：LinkskyGatewayServer(监听) → SessionOpened → runtime.Bind + SendRobotOnline；
    /// 主线程泵驱动会话入站队列；断线 → runtime.Unbind（等 SDK 3s 自动重连，重连后重发 sync）。
    /// </summary>
    // GesturePlayer.Awake unlocks the upper-body joints when added below.
    // Finish that topology setup before X02newAgent.OnEnable/Initialize caches
    // articulation positions and velocities for episode resets (default order 0).
    [DefaultExecutionOrder(-200)]
    public class CompetitionLauncher : MonoBehaviour
    {
        public enum InputMode { Microphone, TestClip }

        [Header("网关")]
        public int Port = 9002;
        public string Path = "/api/V1/open-portal/app/wss/agent-sdk";
        [Tooltip("赛事模式建议开：校验 HMAC 签名。开发时关掉便于快速迭代")]
        public bool StrictAuth = false;
        public string AppId = "demo-app";
        public string AppKey = "demo-key";
        public string AppSecret = "demo-secret";

        [Header("机器人")]
        public AgentMetaProfile MetaProfile;
        public SkillCatalog Catalog;
        [Tooltip("X2 模型的 Animator（技能动画）；可空")]
        public Animator RobotAnimator;

        [Header("音频输入")]
        public InputMode Mode = InputMode.Microphone;
        [Tooltip("TestClip 模式的用户语音语料（16k mono wav 导入的 AudioClip）")]
        public AudioClip TestClip;
        public bool LoopTestClip = false;
        [Tooltip("启动即开始采集；否则用 DebugHud 按钮控制")]
        public bool AutoBeginInput = true;

        [Header("调试")]
        public bool ShowDebugHud = true;

        // 运行时组件（DebugHud 需要）
        [HideInInspector] public LinkskyGatewayServer Server;
        [HideInInspector] public VirtualRobotRuntime Runtime;
        [HideInInspector] public MainThreadPump Pump;
        [HideInInspector] public MicInputSource MicSource;
        [HideInInspector] public ClipInputSource ClipSource;
        [HideInInspector] public DebugHud Hud;
        [HideInInspector] public GesturePlayer Gestures;
        [HideInInspector] public EmotionController Emotions;
        [HideInInspector] public LocomotionCommander Loco;
        [HideInInspector] public SkillRouter Router;
        [HideInInspector] public RobotCameraRig Cameras;

        const string RobotTag = "robot";

        void Awake()
        {
            // ---- L2 组件 ----
            if (Catalog == null) Catalog = SkillCatalog.CreateDefault();
            Runtime = gameObject.AddComponent<VirtualRobotRuntime>();
            var tts = gameObject.AddComponent<TtsStreamPlayer>();
            var skills = gameObject.AddComponent<SkillAnimator>();
            skills.Animator = RobotAnimator;
            skills.Catalog = Catalog;
            Runtime.Tts = tts;
            Runtime.Skills = skills;

            // ---- 技能执行器：手势/表情挂机器人本体，运动/路由挂网关物体 ----
            var robot = FindRobot();
            if (robot != null)
            {
                Gestures = robot.AddComponent<GesturePlayer>();
                Emotions = robot.AddComponent<EmotionController>();
                tts.PlaybackEnergy += Emotions.SetMouthLevel; // TTS 口型
            }
            Loco = gameObject.AddComponent<LocomotionCommander>();
            Router = gameObject.AddComponent<SkillRouter>();
            Router.Catalog = Catalog;
            Router.Legacy = skills;
            Router.Gestures = Gestures;
            Router.Loco = Loco;
            Router.Emotions = Emotions;
            Runtime.Router = Router;

            if (Mode == InputMode.Microphone)
            {
                MicSource = gameObject.AddComponent<MicInputSource>();
                Runtime.Input = MicSource;
            }
            else
            {
                ClipSource = gameObject.AddComponent<ClipInputSource>();
                ClipSource.Clip = TestClip;
                ClipSource.Loop = LoopTestClip;
                Runtime.Input = ClipSource;
            }

            // ---- L3：主线程泵 + HUD ----
            Pump = gameObject.AddComponent<MainThreadPump>();
            if (ShowDebugHud)
            {
                Hud = gameObject.AddComponent<DebugHud>();
                Hud.Initialize(this);
            }
            if (robot != null)
            {
                Cameras = gameObject.AddComponent<RobotCameraRig>();
                Cameras.Initialize(robot.transform, Hud);
            }

            // ---- L1 网关 ----
            Server = new LinkskyGatewayServer
            {
                Path = Path,
                StrictAuth = StrictAuth,
                AppId = AppId,
                AppKey = AppKey,
                AppSecret = AppSecret,
            };
            if (MetaProfile != null) Server.AgentId = MetaProfile.AgentId;

            Server.SessionOpened += OnSessionOpened;
            Server.SessionClosed += OnSessionClosed;
            Server.FrameLoggedDefault();

            Pump.Server = Server;
        }

        void Start()
        {
            Server.Start(Port);
            if (AutoBeginInput) Runtime.Input?.Begin();
        }

        void OnSessionOpened(GatewaySession session)
        {
            // 监听线程上下文：双向装配缺一不可
            //   session.Runtime = Runtime：入站方向（Dispatch → IRobotRuntime 回调）
            //   Runtime.Bind(session)：出站方向（IGatewayPort 上行）
            session.Runtime = Runtime;
            Runtime.Bind(session);
            session.SendRobotOnline(MetaProfile != null ? MetaProfile.ToMeta() : null,
                session.CallbackType);
            Pump.Post(() => Debug.Log("[Launcher] Agent 会话已建立: " + session.RobotCid +
                " callbackType=" + session.CallbackType));
        }

        void OnSessionClosed(GatewaySession session)
        {
            Pump.Post(() =>
            {
                // Closed sessions leave the server before their disconnect queue is pumped.
                // Ignore a delayed close if a newer session has already connected.
                if (Runtime == null || !ReferenceEquals(Runtime.Port, session)) return;
                Runtime.OnAgentDisconnected();
                Debug.Log("[Launcher] Agent 会话已断开，等待重连（SDK 将于 3s 后自动重连）");
            });
        }

        /// <summary>找步态机器人根（x2t2.5 场景 tag=robot；X02RobotBridge 桥接步态）。</summary>
        static GameObject FindRobot()
        {
            try
            {
                var go = GameObject.FindWithTag(RobotTag);
                if (go == null)
                    Debug.LogWarning("[Launcher] 场景无 tag=robot 物体：手势/表情不可用" +
                        "（movement 由 X02newAgent 桥接决定）");
                return go;
            }
            catch (UnityException)
            {   // 项目未定义 robot tag
                Debug.LogWarning("[Launcher] TagManager 未定义 'robot' tag，手势/表情不可用");
                return null;
            }
        }

        void OnDestroy()
        {
            Server?.Stop();
        }
    }

    static class GatewayLogBridge
    {
        /// <summary>默认网关日志桥（扩展方法挂接）。</summary>
        public static void FrameLoggedDefault(this LinkskyGatewayServer server)
        {
            server.Log += line => Debug.Log(line);
        }
    }
}

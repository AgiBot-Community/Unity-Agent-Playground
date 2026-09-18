using UnityEngine;
using X02Competition.Robot;

/// <summary>
/// 程序集桥：把 X02ElderCare 的 X02newAgent（Assembly-CSharp）适配成
/// X02Competition.Robot 的 ILocomotionAgent（asmdef 引用不到 Assembly-CSharp，
/// 经 LocomotionRegistry 静态注入）。场景零改动：运行时自动创建与注册。
/// </summary>
public class X02RobotBridge : MonoBehaviour
{
    /// <summary>机器人 tag（x2t2.5 场景物体 m_TagString=robot）。</summary>
    public const string RobotTag = "robot";

    /// <summary>纯 C# 适配器：字段直通 X02newAgent 的 public 运动指令。</summary>
    class AgentAdapter : ILocomotionAgent
    {
        readonly X02newAgent _a;
        readonly Transform _root;

        public AgentAdapter(X02newAgent agent)
        {
            _a = agent;
            // 与 X02newAgent.Initialize 同源：GetComponentsInChildren 的根
            var root = _a.GetComponentInChildren<ArticulationBody>();
            _root = root != null ? root.transform : _a.transform;
        }

        public float Vr { get => _a.vr; set => _a.vr = value; }
        public float Vd { get => _a.vd; set => _a.vd = value; }
        public float Wr { get => _a.wr; set => _a.wr = value; }
        public Transform Root => _root;
        public bool IsSupportPhase() => _a.IsGaitSupportPhase();
        public bool SettleAtSupportPhase() => _a.SettleGaitAtSupportPhase();
        public bool IsSettled() => _a.IsGaitSettled();
        public void SetExternalCommand(bool active) => _a.externalCommandActive = active;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (FindObjectOfType<X02RobotBridge>() != null) return;
        var go = new GameObject("X02RobotBridge");
        go.AddComponent<X02RobotBridge>();
    }

    void Start()
    {
        var agent = FindObjectOfType<X02newAgent>();
        if (agent == null)
        {
            Debug.Log("[Bridge] 场景无 X02newAgent，movement 技能不可用（手势/表情不受影响）");
            return;
        }
        LocomotionRegistry.Agent = new AgentAdapter(agent);
        Debug.Log("[Bridge] X02newAgent 已桥接为 ILocomotionAgent: " + agent.gameObject.name);
    }
}

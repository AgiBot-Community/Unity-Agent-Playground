using UnityEngine;

namespace X02Competition.Robot
{
    /// <summary>
    /// 步态机器人控制契约。
    /// 实现方在 Assembly-CSharp（X02RobotBridge 适配 X02newAgent）——
    /// asmdef 无法引用 Assembly-CSharp，故经 LocomotionRegistry 静态桥接注入。
    /// </summary>
    public interface ILocomotionAgent
    {
        /// <summary>前后速度指令（+前进，约 [-0.4, 1.2]）。</summary>
        float Vr { get; set; }
        /// <summary>侧移指令（+右，约 [-0.6, 0.6]）。</summary>
        float Vd { get; set; }
        /// <summary>转向指令（+右转，约 [-1.2, 1.2]）。</summary>
        float Wr { get; set; }
        /// <summary>根 Transform（位移/朝向积分用）。</summary>
        Transform Root { get; }
        /// <summary>当前是否处于支撑相位（切命令的安全窗口）。</summary>
        bool IsSupportPhase();
        /// <summary>支撑相位窗口内归零命令；返回是否成功归零。</summary>
        bool SettleAtSupportPhase();
        /// <summary>命令归零且步态幅度已收敛（站稳）。</summary>
        bool IsSettled();
        /// <summary>外部命令接管开关（true 时键盘每帧归零让位，结束必须关回）。</summary>
        void SetExternalCommand(bool active);
    }

    /// <summary>跨程序集桥接注册点：Assembly-CSharp 的桥接脚本在启动时注入实现。</summary>
    public static class LocomotionRegistry
    {
        public static ILocomotionAgent Agent;
        public static bool Available => Agent != null;
    }

    /// <summary>
    /// 步态运动指令器：walk(distanceM) / turn(angleDeg) / stop。
    /// 命令在支撑相位窗口切换（防摔）；结束在支撑相位归零并等站稳后回报 done。
    /// 实现为持续覆盖写 Vr/Vd/Wr（DefaultExecutionOrder 早于步态控制器）。
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public class LocomotionCommander : MonoBehaviour
    {
        [Header("速度（相对 X02newAgent 安全范围）")]
        public float WalkSpeed = 0.8f;
        public float TurnRate = 0.8f;

        public bool Verbose = false;

        public delegate void DoneCallback(bool completed);

        enum Cmd { None, Walk, Turn, StopOnly }

        Cmd _cmd;
        float _remain;              // walk: 剩余距离；turn: 剩余角度
        DoneCallback _onDone;
        Vector3 _lastPos;
        float _lastYaw;
        bool _settling;

        /// <summary>是否有运动指令执行中。</summary>
        public bool IsBusy => _cmd != Cmd.None;

        /// <summary>前进指定距离（米）。执行中再调用会替换指令。</summary>
        public void Walk(float distanceMeters, DoneCallback onDone)
        {
            if (!CheckAgent()) { onDone?.Invoke(false); return; }
            distanceMeters = Mathf.Clamp(distanceMeters, 0.2f, 5f);
            StartCmd(Cmd.Walk, distanceMeters, onDone);
        }

        /// <summary>原地转指定角度（度，+右转）。</summary>
        public void Turn(float angleDeg, DoneCallback onDone)
        {
            if (!CheckAgent()) { onDone?.Invoke(false); return; }
            angleDeg = Mathf.Clamp(angleDeg, -360f, 360f);
            StartCmd(Cmd.Turn, angleDeg, onDone);
        }

        /// <summary>立即停止（支撑相位归零，站稳后回调）。</summary>
        public void Stop(DoneCallback onDone = null)
        {
            if (!CheckAgent()) { onDone?.Invoke(false); return; }
            StartCmd(Cmd.StopOnly, 0f, onDone);
        }

        bool CheckAgent()
        {
            if (LocomotionRegistry.Available) return true;
            Debug.LogWarning("[Loco] 无步态机器人（未找到 ILocomotionAgent），movement 技能不可用");
            return false;
        }

        void StartCmd(Cmd cmd, float amount, DoneCallback onDone)
        {
            _cmd = cmd;
            _remain = amount;
            _onDone = onDone;
            _settling = false;
            var agent = LocomotionRegistry.Agent;
            // 接管：防止步态控制器键盘分支每帧把 vr/vd/wr 归零
            agent.SetExternalCommand(true);
            _lastPos = agent.Root.position;
            _lastYaw = agent.Root.eulerAngles.y;
            if (Verbose) Debug.Log("[Loco] 指令: " + cmd + " amount=" + amount.ToString("F2"));
        }

        void Finish(bool completed)
        {
            var cb = _onDone;
            _cmd = Cmd.None;
            _onDone = null;
            if (LocomotionRegistry.Available)
                LocomotionRegistry.Agent.SetExternalCommand(false);
            if (Verbose) Debug.Log("[Loco] 完成: " + (completed ? "ok" : "aborted"));
            cb?.Invoke(completed);
        }

        void FixedUpdate()
        {
            if (_cmd == Cmd.None) return;
            if (!LocomotionRegistry.Available) { Finish(false); return; }
            var agent = LocomotionRegistry.Agent;
            var root = agent.Root;

            switch (_cmd)
            {
                case Cmd.Walk:
                    if (_settling) break;
                    agent.Wr = 0f;
                    agent.Vd = 0f;
                    agent.Vr = _remain >= 0f ? WalkSpeed : -Mathf.Min(WalkSpeed, 0.4f);
                    // 水平位移积分
                    var pos = root.position;
                    var d = Vector3.ProjectOnPlane(pos - _lastPos, Vector3.up).magnitude;
                    _lastPos = pos;
                    _remain -= Mathf.Sign(_remain) * d;
                    if (Mathf.Abs(_remain) <= 0.1f) BeginSettle(agent);
                    break;

                case Cmd.Turn:
                    if (_settling) break;
                    agent.Vr = 0f;
                    agent.Vd = 0f;
                    agent.Wr = Mathf.Sign(_remain) * TurnRate;
                    // yaw 积分（处理 ±360 环绕）
                    var yaw = NormalizeYaw(root.eulerAngles.y);
                    var dy = Mathf.DeltaAngle(_lastYaw, yaw);
                    _lastYaw = yaw;
                    _remain -= dy;
                    if (Mathf.Abs(_remain) <= 5f) BeginSettle(agent);
                    break;

                case Cmd.StopOnly:
                    BeginSettle(agent);
                    break;
            }

            if (_settling)
            {
                if (agent.SettleAtSupportPhase() && agent.IsSettled())
                    Finish(true);
            }
        }

        void BeginSettle(ILocomotionAgent agent)
        {
            agent.Vr = 0f;
            agent.Vd = 0f;
            agent.Wr = 0f;
            _settling = true;
        }

        static float NormalizeYaw(float deg) => deg > 180f ? deg - 360f : deg;
    }
}

using System.Collections.Generic;
using UnityEngine;

namespace X02Competition.Robot
{
    /// <summary>
    /// 机器人元数据（robot_state.sync 的 agentMeta 载荷来源），well-known keys 对齐 AgentMeta：
    /// def.wakeupWord / def.robotName / def.city / def.buzzWord。
    /// </summary>
    [CreateAssetMenu(fileName = "AgentMetaProfile", menuName = "X02Competition/Agent Meta Profile")]
    public class AgentMetaProfile : ScriptableObject
    {
        public string AgentId = "agent-001";
        public string WakeupWord = "灵犀";
        public string RobotName = "X2";
        public string City = "上海";
        public string BuzzWord = "";

        public Dictionary<string, object> ToMeta()
        {
            return new Dictionary<string, object>
            {
                ["def.wakeupWord"] = WakeupWord,
                ["def.robotName"] = RobotName,
                ["def.city"] = City,
                ["def.buzzWord"] = BuzzWord,
            };
        }
    }
}

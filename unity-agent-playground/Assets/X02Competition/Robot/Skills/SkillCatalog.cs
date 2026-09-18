using System;
using System.Collections.Generic;
using UnityEngine;

namespace X02Competition.Robot
{
    /// <summary>技能执行器类型：决定 skillName 由哪个组件执行。</summary>
    public enum SkillExecutor
    {
        /// <summary>步态运动（walk/turn/stop）→ LocomotionCommander。</summary>
        Locomotion,
        /// <summary>程序化关节手势（wave_hands/nod/...）→ GesturePlayer。</summary>
        Gesture,
        /// <summary>表情（happy/thinking/...）→ EmotionController。</summary>
        Emotion,
        /// <summary>传统 Animator Trigger 路径 → SkillAnimator（保留兼容）。</summary>
        AnimatorTrigger,
    }

    /// <summary>
    /// 赛事技能表（ScriptableObject）。
    /// 此表 = 协议文档技能章节的"技能表"，skillType/skillName 必须与真机 aimdk 侧一一对应；
    /// Executor 决定网关内部分发；AnimatorTrigger 仅 AnimatorTrigger 执行器使用。
    /// 变更需两侧同步。
    /// </summary>
    [CreateAssetMenu(fileName = "SkillCatalog", menuName = "X02Competition/Skill Catalog")]
    public class SkillCatalog : ScriptableObject
    {
        [Serializable]
        public class SkillEntry
        {
            public string SkillType = "gesture";
            public string SkillName = "wave_hands";
            public SkillExecutor Executor = SkillExecutor.Gesture;
            [Tooltip("AnimatorTrigger 执行器的 Trigger 参数名；其余执行器忽略")]
            public string AnimatorTrigger = "";
            [Tooltip("名义时长（秒），用于录视频节奏参考")]
            public float NominalDurationSec = 2f;
        }

        public List<SkillEntry> Entries = new List<SkillEntry>();

        public bool TryGet(string skillType, string skillName, out SkillEntry entry)
        {
            foreach (var e in Entries)
            {
                if (string.Equals(e.SkillType, skillType, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(e.SkillName, skillName, StringComparison.OrdinalIgnoreCase))
                {
                    entry = e;
                    return true;
                }
            }
            entry = null;
            return false;
        }

        /// <summary>运行时默认技能表（Launcher 未配置 Catalog 时兜底；Inspector 自定义可覆盖）。</summary>
        public static SkillCatalog CreateDefault()
        {
            var c = CreateInstance<SkillCatalog>();
            c.Entries = new List<SkillEntry>
            {
                // 手势（程序化关节）
                new SkillEntry { SkillType = "gesture", SkillName = "wave_hands", Executor = SkillExecutor.Gesture, NominalDurationSec = 2.5f },
                new SkillEntry { SkillType = "gesture", SkillName = "open_arms", Executor = SkillExecutor.Gesture, NominalDurationSec = 3f },
                // 运动（步态）
                new SkillEntry { SkillType = "movement", SkillName = "walk", Executor = SkillExecutor.Locomotion },
                new SkillEntry { SkillType = "movement", SkillName = "turn", Executor = SkillExecutor.Locomotion },
                new SkillEntry { SkillType = "movement", SkillName = "stop", Executor = SkillExecutor.Locomotion },
                // 表情（头部表情屏，5 种经典）
                new SkillEntry { SkillType = "emotion", SkillName = "happy", Executor = SkillExecutor.Emotion },
                new SkillEntry { SkillType = "emotion", SkillName = "sad", Executor = SkillExecutor.Emotion },
                new SkillEntry { SkillType = "emotion", SkillName = "surprised", Executor = SkillExecutor.Emotion },
                new SkillEntry { SkillType = "emotion", SkillName = "angry", Executor = SkillExecutor.Emotion },
                new SkillEntry { SkillType = "emotion", SkillName = "love", Executor = SkillExecutor.Emotion },
                new SkillEntry { SkillType = "emotion", SkillName = "neutral", Executor = SkillExecutor.Emotion },
            };
            return c;
        }
    }
}

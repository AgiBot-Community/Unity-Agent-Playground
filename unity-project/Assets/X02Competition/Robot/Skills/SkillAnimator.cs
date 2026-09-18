using UnityEngine;

namespace X02Competition.Robot
{
    /// <summary>
    /// 技能执行器：skillName → X2 Animator Trigger。
    /// 未知技能：日志 + 忽略（协议是 fire-and-forget，没有失败回报通道）。
    /// </summary>
    public class SkillAnimator : MonoBehaviour
    {
        public Animator Animator;
        public SkillCatalog Catalog;

        /// <summary>执行技能。返回是否命中技能表。</summary>
        public bool Play(string skillType, string skillName)
        {
            if (Catalog == null)
            {
                Debug.LogWarning("[SkillAnim] 未配置 SkillCatalog，忽略技能: " + skillName);
                return false;
            }
            if (!Catalog.TryGet(skillType, skillName, out var entry))
            {
                Debug.LogWarning("[SkillAnim] 未知技能（需核对赛事技能表）: " +
                    skillType + "/" + skillName);
                return false;
            }
            if (Animator != null)
            {
                Animator.SetTrigger(entry.AnimatorTrigger);
            }
            else
            {
                Debug.LogWarning("[SkillAnim] 未配置 Animator，仅记录技能: " + skillName);
            }
            return true;
        }
    }
}

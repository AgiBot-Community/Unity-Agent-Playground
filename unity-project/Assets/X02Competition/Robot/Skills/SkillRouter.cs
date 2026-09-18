using System;
using System.Collections.Generic;
using UnityEngine;

namespace X02Competition.Robot
{
    /// <summary>
    /// 技能路由：按 SkillCatalog.Executor 把 xlm_response.skill 分发到对应执行器
    /// （步态运动 / 程序化手势 / 表情屏 / Animator Trigger 兼容路径）。
    /// 每次执行回报状态（running/done/failed），由 Runtime 转成 skill_response.state 帧。
    /// </summary>
    public class SkillRouter : MonoBehaviour
    {
        public SkillCatalog Catalog;
        [Tooltip("AnimatorTrigger 兼容执行器")]
        public SkillAnimator Legacy;

        public GesturePlayer Gestures;
        public LocomotionCommander Loco;
        public EmotionController Emotions;

        public bool Verbose = false;

        public delegate void StateCallback(string state);

        /// <summary>打断当前动作（interrupt / 新一轮说话）：运动停止、手势复位。</summary>
        public void Abort()
        {
            if (Loco != null && Loco.IsBusy) Loco.Stop();
            if (Gestures != null && Gestures.IsPlaying) Gestures.Abort();
        }

        /// <summary>执行技能。onState 至少回调一次（failed）或 running + 终态。</summary>
        public void Execute(string skillType, string skillName,
            Dictionary<string, object> skillParam, StateCallback onState)
        {
            if (Catalog == null || !Catalog.TryGet(skillType, skillName, out var entry))
            {
                // 兜底：资产缺条目时按 skillType 约定路由，不因资产过期而 failed
                var conv = skillType?.ToLowerInvariant();
                var exec = conv == "gesture" ? SkillExecutor.Gesture
                    : conv == "movement" ? SkillExecutor.Locomotion
                    : conv == "emotion" ? SkillExecutor.Emotion
                    : (SkillExecutor?)null;
                if (exec == null)
                {
                    Debug.LogWarning("[SkillRouter] 未知技能: " + skillType + "/" + skillName +
                        "（核对 SkillCatalog）");
                    onState?.Invoke("failed");
                    return;
                }
                entry = new SkillCatalog.SkillEntry
                {
                    SkillType = skillType,
                    SkillName = skillName,
                    Executor = exec.Value,
                };
                if (Verbose) Debug.Log("[SkillRouter] 目录未命中，按约定路由: " +
                    skillType + "/" + skillName + " → " + exec);
            }
            if (Verbose) Debug.Log("[SkillRouter] " + skillType + "/" + skillName +
                " → " + entry.Executor);

            switch (entry.Executor)
            {
                case SkillExecutor.Locomotion:
                    ExecuteLocomotion(skillName, skillParam, onState);
                    break;
                case SkillExecutor.Gesture:
                    onState?.Invoke("running");
                    if (Gestures == null || !Gestures.Play(skillName,
                        ok => onState?.Invoke(ok ? "done" : "failed")))
                        onState?.Invoke("failed");
                    break;
                case SkillExecutor.Emotion:
                    if (!ExecuteEmotion(skillName, skillParam))
                    {
                        onState?.Invoke("failed");
                        return;
                    }
                    onState?.Invoke("running");
                    onState?.Invoke("done"); // 表情为即时状态显示，直接完成
                    break;
                case SkillExecutor.AnimatorTrigger:
                    if (Legacy == null || !Legacy.Play(skillType, skillName))
                    {
                        onState?.Invoke("failed");
                        return;
                    }
                    onState?.Invoke("done");
                    break;
            }
        }

        void ExecuteLocomotion(string skillName, Dictionary<string, object> p,
            StateCallback onState)
        {
            onState?.Invoke("running");
            LocomotionCommander.DoneCallback done =
                ok => onState?.Invoke(ok ? "done" : "failed");
            switch (skillName)
            {
                case "walk":
                    Loco.Walk(Param(p, "distanceM", 1f), done);
                    break;
                case "turn":
                    Loco.Turn(Param(p, "angleDeg", 90f), done);
                    break;
                case "stop":
                    Loco.Stop(done);
                    break;
                default:
                    Debug.LogWarning("[SkillRouter] 未知 movement 技能: " + skillName);
                    onState?.Invoke("failed");
                    break;
            }
        }

        bool ExecuteEmotion(string skillName, Dictionary<string, object> p)
        {
            var durationMs = Param(p, "durationMs", 3000f);
            return Emotions != null && Emotions.SetEmotion(skillName, durationMs / 1000f);
        }

        static float Param(Dictionary<string, object> p, string key, float def)
        {
            if (p == null) return def;
            return p.TryGetValue(key, out var v) && v is IConvertible conv
                ? Convert.ToSingle(conv)
                : def;
        }
    }
}

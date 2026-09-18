using System.Collections;
using System.IO;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using X02Competition.Robot;

namespace X02Competition.Tests.PlayMode
{
    /// <summary>
    /// 挥手动作诊断：加载真实竞赛场景播放 wave_hands，采样右臂各关节
    /// 实际角度 vs xDrive 目标、限位、刚度，以及腕/手末端世界坐标。
    /// 用于定位"肩关节目标已下发但不动/轴向不对"。结果写入 /tmp/wave_diag.tsv。
    /// </summary>
    public class GestureDiagTests
    {
        const string ScenePath = "Assets/X02Competition/Scenes/scene.unity";
        const string OutPath = "/tmp/wave_diag.tsv";

        [UnityTest]
        public IEnumerator WaveHands_ShoulderDiag()
        {
            LogAssert.ignoreFailingMessages = true;
            SceneManager.LoadScene(ScenePath, LoadSceneMode.Single);
            yield return null; // 新场景 Awake
            yield return new WaitForSeconds(1f); // 物理稳定

            var gp = Object.FindObjectOfType<GesturePlayer>();
            Assert.IsNotNull(gp, "场景里没有 GesturePlayer（robot tag 未命中?）");

            ArticulationBody pitch = null, roll = null, yaw = null, elbow = null;
            foreach (var b in gp.GetComponentsInChildren<ArticulationBody>())
            {
                var n = b.gameObject.name.ToLowerInvariant();
                if (!n.Contains("right")) continue;
                if (n.Contains("shoulder_pitch")) pitch = b;
                else if (n.Contains("shoulder_roll")) roll = b;
                else if (n.Contains("shoulder_yaw")) yaw = b;
                else if (n.Contains("elbow")) elbow = b;
            }
            Assert.IsNotNull(roll, "未找到右肩 roll 关节");
            Assert.IsNotNull(elbow, "未找到右肘关节");

            var sb = new StringBuilder();
            sb.AppendLine("# roll关节: " + roll.gameObject.name + " type=" + roll.jointType +
                " dof=" + roll.dofCount + " forceLimit=" + roll.xDrive.forceLimit +
                " 限位=[" + roll.xDrive.lowerLimit.ToString("F1") + "," +
                roll.xDrive.upperLimit.ToString("F1") + "]");
            sb.AppendLine("# t\tpitch°\troll°\tyaw°\telbow°\trollTarget\trollK\t" +
                "elbowPos\tarmEndPos");

            var armEnd = DeepestChild(elbow.transform);
            sb.AppendLine(Sample(-1f, pitch, roll, yaw, elbow, armEnd));

            Assert.IsTrue(gp.Play("wave_hands", null), "wave_hands 播放失败");
            for (float t = 0f; t < 3.5f; t += 0.2f)
            {
                yield return new WaitForSeconds(0.2f);
                sb.AppendLine(Sample(t, pitch, roll, yaw, elbow, armEnd));
            }

            File.WriteAllText(OutPath, sb.ToString());
            Debug.Log("[GestureDiag] 已写入 " + OutPath + "，末行: " +
                Sample(99f, pitch, roll, yaw, elbow, armEnd));
        }

        static string Sample(float t, ArticulationBody pitch, ArticulationBody roll,
            ArticulationBody yaw, ArticulationBody elbow, Transform armEnd)
        {
            var ep = elbow.transform.position;
            var ap = armEnd.position;
            return t.ToString("F1") + "\t" +
                Deg(pitch) + "\t" + Deg(roll) + "\t" + Deg(yaw) + "\t" + Deg(elbow) + "\t" +
                roll.xDrive.target.ToString("F1") + "\t" + roll.xDrive.stiffness.ToString("F0") + "\t" +
                "(" + ep.x.ToString("F3") + "," + ep.y.ToString("F3") + "," + ep.z.ToString("F3") + ")\t" +
                "(" + ap.x.ToString("F3") + "," + ap.y.ToString("F3") + "," + ap.z.ToString("F3") + ")";
        }

        static string Deg(ArticulationBody j)
        {
            return j == null ? "-" : (Mathf.Rad2Deg * j.jointPosition[0]).ToString("F1");
        }

        static Transform DeepestChild(Transform t)
        {
            while (t.childCount > 0) t = t.GetChild(0);
            return t;
        }
    }
}

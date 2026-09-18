using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace X02Competition.Tests.Editor
{
    /// <summary>
    /// 依赖闭包导出：枚举 X02Competition（含场景）引用到的全部资源路径，
    /// 用于裁剪交付工程（区分"X02Competition 依赖"与"ElderCare 独有"）。
    /// 用法：Unity -batchmode -executeMethod X02Competition.Tests.Editor.DependencyReportTool.Export
    /// 输出 /tmp/x02_asset_deps.txt（一行一个资产路径，均在 Assets/ 下）。
    /// </summary>
    public static class DependencyReportTool
    {
        const string OutPath = "/tmp/x02_asset_deps.txt";
        const string Root = "Assets/X02Competition";

        /// <summary>脚本编译依赖（Assembly-CSharp 链，GetDependencies 覆盖不到）。</summary>
        static readonly string[] CompileDeps =
        {
            "Assets/Scripts/X02newAgent.cs",
            "Assets/Scripts/X02TargetNavigationController.cs",
            "Assets/Scripts/CameraFollow.cs",
        };

        public static void Export()
        {
            // GetDependencies 对文件夹不展开内容（Unity 已知行为），先枚举全部资产
            var guids = AssetDatabase.FindAssets("", new[] { Root });
            var inputs = new string[guids.Length];
            for (var i = 0; i < guids.Length; i++)
                inputs[i] = AssetDatabase.GUIDToAssetPath(guids[i]);
            var deps = AssetDatabase.GetDependencies(inputs, true);
            var sb = new StringBuilder();
            foreach (var d in deps)
                if (d.StartsWith("Assets/"))
                    sb.AppendLine(d);
            foreach (var c in CompileDeps)
                sb.AppendLine(c);
            File.WriteAllText(OutPath, sb.ToString());
            Debug.Log("[DepReport] 输入 " + inputs.Length + " 个资产，闭包共 " +
                deps.Length + " 项，已写入 " + OutPath);
        }
    }
}

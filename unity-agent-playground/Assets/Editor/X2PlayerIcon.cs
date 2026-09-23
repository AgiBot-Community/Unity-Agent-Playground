using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

[InitializeOnLoad]
internal sealed class X2PlayerIcon : IPreprocessBuildWithReport
{
    private const string IconPath = "Assets/Branding/agibot-x2-icon.png";

    static X2PlayerIcon()
    {
        EditorApplication.delayCall += () => Apply(false);
    }

    public int callbackOrder => -1000;

    public void OnPreprocessBuild(BuildReport report)
    {
        if (report.summary.platform == BuildTarget.StandaloneWindows64 ||
            report.summary.platform == BuildTarget.StandaloneWindows)
        {
            Apply(true);
        }
    }

    private static void Apply(bool failIfMissing)
    {
        var icon = AssetDatabase.LoadAssetAtPath<Texture2D>(IconPath);
        var sizes = PlayerSettings.GetIconSizesForTargetGroup(BuildTargetGroup.Standalone);
        if (icon == null || sizes == null || sizes.Length == 0)
        {
            var message = $"Cannot configure Windows Player icon: {IconPath}";
            if (failIfMissing) throw new BuildFailedException(message);
            Debug.LogWarning(message);
            return;
        }

        var existing = PlayerSettings.GetIconsForTargetGroup(BuildTargetGroup.Standalone);
        if (existing != null && existing.Length == sizes.Length)
        {
            bool alreadySet = true;
            foreach (var texture in existing)
                alreadySet &= texture == icon;
            if (alreadySet) return;
        }

        var icons = new Texture2D[sizes.Length];
        for (int i = 0; i < icons.Length; i++)
            icons[i] = icon;
        PlayerSettings.SetIconsForTargetGroup(BuildTargetGroup.Standalone, icons);
        AssetDatabase.SaveAssets();
    }
}

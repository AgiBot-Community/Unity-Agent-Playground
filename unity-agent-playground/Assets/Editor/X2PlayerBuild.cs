using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>Batch Windows build: -executeMethod X2PlayerBuild.Windows -x2Output /absolute/player.exe.</summary>
internal static class X2PlayerBuild
{
    public static void Windows()
    {
        var arguments = Environment.GetCommandLineArgs();
        var index = Array.IndexOf(arguments, "-x2Output");
        if (index < 0 || index + 1 >= arguments.Length)
            throw new BuildFailedException("Provide -x2Output with an absolute Windows .exe path.");
        var output = arguments[index + 1];
        if (!Path.IsPathRooted(output) || !output.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            throw new BuildFailedException("-x2Output must be an absolute .exe path.");
        output = Path.GetFullPath(output);
        Directory.CreateDirectory(Path.GetDirectoryName(output));
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { "Assets/X02Competition/Scenes/scene.unity" },
            locationPathName = output,
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.None,
        });
        if (report.summary.result != BuildResult.Succeeded)
            throw new BuildFailedException("Windows build failed: " + report.summary.result);
        Debug.Log("X2_WINDOWS_BUILD_PASSED: " + output + " (" + report.summary.totalSize + " bytes)");
    }
}

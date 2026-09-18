namespace X02Competition.Tests.Editor
{
    /// <summary>
    /// 无头运行主场景（联调/冒烟用）：
    ///   Unity -batchmode -executeMethod X02Competition.Tests.Editor.HeadlessSceneRunner.Play
    /// 不加 -quit，编辑器保持运行（含网关 ws://localhost:9002），Ctrl+C 结束。
    /// </summary>
    public static class HeadlessSceneRunner
    {
        const string ScenePath = "Assets/X02Competition/Scenes/scene.unity";

        public static void Play()
        {
            UnityEditor.EditorSceneManager.OpenScene(ScenePath,
                UnityEditor.OpenSceneMode.Single);
            UnityEditor.EditorApplication.isPlaying = true;
        }
    }
}

#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using X02Competition.Robot;

// Run in a separate batch editor with graphics enabled, -quit and -executeMethod EmotionVerification.Run.
public static class EmotionVerification
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static readonly string[] Emotions = { "neutral", "happy", "sad", "surprised", "angry", "love" };

    static void Invoke(EmotionController face, string method) =>
        typeof(EmotionController).GetMethod(method, Private).Invoke(face, null);
    static void Set(EmotionController face, string field, object value) =>
        typeof(EmotionController).GetField(field, Private).SetValue(face, value);
    static T Get<T>(EmotionController face, string field) =>
        (T)typeof(EmotionController).GetField(field, Private).GetValue(face);
    static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    public static void Run()
    {
        Require(Application.isBatchMode, "Use a separate batch editor.");
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var root = new GameObject("Expression verification");
        var face = root.AddComponent<EmotionController>();
        face.Verbose = false;
        Invoke(face, "Start");
        var screen = root.transform.Find("EmotionScreen");
        Require(screen != null, "Missing expression board.");
        Require(screen.localPosition == new Vector3(0, 0.02f, 0.12f), "Board position changed.");
        Require(Quaternion.Angle(screen.localRotation, Quaternion.Euler(0, 180, 180)) < 0.01f,
            "Board rotation changed.");
        Require(face.ScreenSize == new Vector2(0.16f, 0.10f), "Board dimensions changed.");
        var texture = Get<Texture2D>(face, "_tex");
        Require(texture.width == 64 && texture.height == 40, "Board resolution changed.");

        var camera = new GameObject("Expression camera").AddComponent<Camera>();
        camera.transform.position = new Vector3(0, 0.02f, 0.5f);
        camera.transform.rotation = Quaternion.LookRotation(Vector3.back, Vector3.up);
        camera.orthographic = true;
        camera.orthographicSize = 0.055f;
        camera.nearClipPlane = 0.01f;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Color.black;
        var target = new RenderTexture(320, 200, 24);
        camera.targetTexture = target;
        var atlas = new Texture2D(320 * Emotions.Length, 200 * 2, TextureFormat.RGB24, false);
        var output = Path.GetFullPath(Path.Combine(Application.dataPath, "../../.diagnostics/emotions"));
        Directory.CreateDirectory(output);
        var stills = new List<Color32[]>();
        for (int column = 0; column < Emotions.Length; column++)
        {
            Require(face.SetEmotion(Emotions[column], 0), "Emotion rejected.");
            Color32[] still = null;
            for (int row = 0; row < 2; row++)
            {
                Set(face, "_mouthSmooth", row == 0 ? 0f : 1f);
                Invoke(face, "Redraw");
                var pixels = texture.GetPixels32();
                if (row == 0) { still = pixels; stills.Add(still); }
                else
                {
                    // Upper half (eyes/brows) must preserve emotion during TTS.
                    for (int y = 0; y < 20; y++)
                        for (int x = 0; x < 64; x++)
                            Require(pixels[y * 64 + x].Equals(still[y * 64 + x]),
                                Emotions[column] + ": speech replaced the eyes/brows.");
                }
                camera.Render();
                RenderTexture.active = target;
                var capture = new Texture2D(320, 200, TextureFormat.RGB24, false);
                capture.ReadPixels(new Rect(0, 0, 320, 200), 0, 0);
                capture.Apply();
                atlas.SetPixels(column * 320, (1 - row) * 200, 320, 200, capture.GetPixels());
                File.WriteAllBytes(Path.Combine(output, Emotions[column] + (row == 0 ? "-idle.png" : "-talk.png")),
                    capture.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(capture);
            }
        }
        for (int a = 0; a < stills.Count; a++)
            for (int b = a + 1; b < stills.Count; b++)
            {
                int difference = 0;
                for (int i = 0; i < stills[a].Length; i++)
                    if (!stills[a][i].Equals(stills[b][i])) difference++;
                Require(difference >= 80, Emotions[a] + "/" + Emotions[b] + ": insufficient silhouette difference.");
            }
        face.SetEmotion("angry", 0.1f);
        Set(face, "_holdUntil", -1f);
        Invoke(face, "Update");
        Require(Get<string>(face, "_emotion") == "neutral", "Timed expression did not return to neutral.");
        atlas.Apply();
        File.WriteAllBytes(Path.Combine(output, "atlas.png"), atlas.EncodeToPNG());
        RenderTexture.active = null;
        camera.targetTexture = null;
        target.Release();
        Debug.Log("EMOTION_VERIFICATION_PASSED: six emotions, TTS identity, timed reset, unchanged board; " + output);
    }
}
#endif

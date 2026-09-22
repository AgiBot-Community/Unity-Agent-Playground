#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using X02Competition.Bootstrap;
using X02Competition.Robot;

// Run with Unity -batchmode -projectPath ... -executeMethod DebugHudVerification.Run.
// Uses real Play Mode lifecycle without requiring an external Agent or microphone.
[InitializeOnLoad]
public static class DebugHudVerification
{
    const string Pending = "X02.DebugHudVerification.Pending";
    const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    static DebugHudVerification()
    {
        if (SessionState.GetBool(Pending, false)) EditorApplication.update += VerifyWhenPlaying;
    }

    public static void Run()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Run this check in a separate batch editor.");
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        SessionState.SetBool(Pending, true);
        EditorApplication.update -= VerifyWhenPlaying;
        EditorApplication.update += VerifyWhenPlaying;
        EditorApplication.EnterPlaymode();
    }

    static T Read<T>(DebugHud hud, string name) => (T)typeof(DebugHud).GetField(name, PrivateInstance).GetValue(hud);
    static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    static void VerifyWhenPlaying()
    {
        if (!EditorApplication.isPlaying || EditorApplication.isCompiling) return;
        EditorApplication.update -= VerifyWhenPlaying;
        SessionState.SetBool(Pending, false);
        GameObject root = null;
        GameObject replacement = null;
        var exitCode = 0;
        try
        {
            root = new GameObject("HUD lifecycle verification");
            root.SetActive(false);
            var launcher = root.AddComponent<CompetitionLauncher>();
            launcher.Mode = CompetitionLauncher.InputMode.TestClip;
            launcher.AutoBeginInput = false;
            launcher.Port = 0;
            root.SetActive(true); // Real Awake -> AddComponent<DebugHud> -> Initialize.
            var hud = launcher.Hud;
            var runtime = launcher.Runtime;
            Require(hud != null && runtime != null, "Launcher did not create HUD/runtime.");
            Require(!Read<bool>(hud, "_visible"), "HUD should initially be collapsed.");

            runtime.OnAgentAsrText("round-1", false, "你好");
            Require(Read<string>(hud, "_asrText") == "你好", "ASR middle missing before Start / while collapsed.");
            runtime.OnAgentAsrText("round-1", true, "你好，机器人");
            runtime.OnAgentLlmDelta("round-1", "item", "你好，");
            runtime.OnAgentLlmDelta("round-1", "item", "我在。");
            Require(Read<string>(hud, "_asrText") == "你好，机器人", "ASR final missing.");
            Require(Read<string>(hud, "_llmText") == "你好，我在。", "LLM streaming chunks not accumulated.");

            hud.Initialize(launcher);
            hud.Initialize(launcher);
            var count = Read<int>(hud, "_eventCount");
            runtime.OnAgentError("round-1", 400, "test error");
            Require(Read<int>(hud, "_eventCount") == count + 1, "Repeated initialization duplicated handlers.");
            runtime.OnAgentInterrupt("round-1", "new_response", "");
            runtime.OnAgentLlmDelta("round-2", "item", "新回答");
            Require(Read<string>(hud, "_llmText") == "新回答", "New response retained previous reply.");

            hud.enabled = false;
            runtime.OnAgentAsrText("disabled", true, "不应接收");
            Require(Read<string>(hud, "_asrText") == "你好，机器人", "Disabled HUD leaked subscriptions.");
            hud.enabled = true;
            runtime.OnAgentAsrText("round-3", true, "重新启用");
            Require(Read<string>(hud, "_asrText") == "重新启用", "Re-enabled HUD did not subscribe.");
            count = Read<int>(hud, "_eventCount");
            runtime.OnAgentDisconnected();
            Require(Read<int>(hud, "_eventCount") == count + 1, "Disconnect event missing.");

            for (var i = 0; i < 60; i++) runtime.OnAgentInterrupt("flood", "interrupt-" + i, "");
            var lines = Read<List<string>>(hud, "_lines");
            Require(lines.Count == 48 && lines[0].Contains("interrupt-59"), "Event history limit/order incorrect.");
            runtime.OnAgentLlmDelta("long", "item", new string('x', 20000));
            Require(Read<string>(hud, "_llmText").Length <= 16001, "Subtitle memory is unbounded.");

            replacement = new GameObject("Replacement runtime");
            var next = replacement.AddComponent<VirtualRobotRuntime>();
            launcher.Runtime = next;
            hud.Initialize(launcher);
            runtime.OnAgentAsrText("old", true, "旧运行时");
            Require(Read<string>(hud, "_asrText") == "重新启用", "Old runtime subscription leaked.");
            next.OnAgentAsrText("new", true, "新运行时");
            Require(Read<string>(hud, "_asrText") == "新运行时", "Replacement runtime did not subscribe.");
            UnityEngine.Object.DestroyImmediate(hud);
            next.OnAgentLlmDelta("after-destroy", "item", "safe");
            Debug.Log("DEBUG_HUD_VERIFICATION_PASSED: lifecycle, ASR, LLM, hidden updates, deduplication, re-enable, disconnect, history bounds, replacement and destruction.");
        }
        catch (Exception error)
        {
            Debug.LogException(error);
            exitCode = 1;
        }
        finally
        {
            if (root != null) UnityEngine.Object.DestroyImmediate(root);
            if (replacement != null) UnityEngine.Object.DestroyImmediate(replacement);
            EditorApplication.Exit(exitCode);
        }
    }
}
#endif

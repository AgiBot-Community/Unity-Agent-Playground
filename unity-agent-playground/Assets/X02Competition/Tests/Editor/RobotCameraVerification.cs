#if UNITY_EDITOR
using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using X02Competition.Bootstrap;

public static class RobotCameraVerification
{
    static readonly MethodInfo Frame = typeof(RobotCameraRig).GetMethod("UpdateFraming", BindingFlags.Instance | BindingFlags.NonPublic);

    public static void Run()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Use a separate batch editor.");
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var source = new GameObject("Main camera");
        source.tag = "MainCamera";
        source.AddComponent<Camera>();
        source.AddComponent<AudioListener>();
        source.transform.position = new Vector3(0, 1.5f, 5);
        var robot = GameObject.CreatePrimitive(PrimitiveType.Cube);
        robot.transform.localScale = new Vector3(1, 2, 0.7f);
        robot.transform.position = Vector3.up;
        var root = new GameObject("Camera rig test");
        var rig = root.AddComponent<RobotCameraRig>();
        rig.Initialize(robot.transform, null);
        if (!rig.Ready) throw new Exception("Camera rig not initialized.");
        var initialBounds = rig.RobotBounds;
        var checks = 0;
        for (var view = 0; view < 4; view++)
        foreach (var aspect in new[] { 0.75f, 4f / 3, 16f / 9, 21f / 9 })
        foreach (var occlusion in new[] { 0f, 0.25f, 0.6f })
        {
            rig.SetView(view);
            rig.ActiveCamera.aspect = aspect;
            foreach (var point in new[] { Vector3.up, new Vector3(1, 1, 2), new Vector3(-8, 1, 12), new Vector3(35, 3, -40), Vector3.up })
            {
                robot.transform.position = point;
                robot.transform.rotation = Quaternion.Euler(0, checks * 37, 0);
                Frame.Invoke(rig, new object[] { 1f / 60, occlusion });
                VerifyBounds(rig.ActiveCamera, rig.RobotBounds, rig.SafeFrame);
                if (view == 0) VerifyBounds(rig.ActiveCamera, initialBounds, rig.SafeFrame);
                checks++;
            }
            var active = 0;
            var listeners = 0;
            foreach (var camera in UnityEngine.Object.FindObjectsOfType<Camera>()) if (camera.enabled) active++;
            foreach (var listener in UnityEngine.Object.FindObjectsOfType<AudioListener>()) if (listener.enabled) listeners++;
            if (active != 1 || listeners != 1) throw new Exception("Multiple active cameras/audio listeners.");
        }
        robot.transform.SetPositionAndRotation(Vector3.up, Quaternion.identity);
        rig.SetView(1);
        Frame.Invoke(rig, new object[] { 0f, 0f });
        var before = rig.ActiveCamera.transform.position;
        robot.transform.position += Vector3.right * 0.1f;
        Frame.Invoke(rig, new object[] { 1f / 60, 0f });
        if (Vector3.Distance(before, rig.ActiveCamera.transform.position) >= 0.1f)
            throw new Exception("Follow dead zone did not retain visible robot movement.");
        Debug.Log("ROBOT_CAMERA_VERIFICATION_PASSED: " + checks + " movement/rotation/reset/framing scenarios, four views, portrait/wide aspects, HUD occlusion and single audio listener.");
    }

    static void VerifyBounds(Camera camera, Bounds bounds, Rect safe)
    {
        for (var i = 0; i < 8; i++)
        {
            var corner = bounds.center + Vector3.Scale(bounds.extents,
                new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
            var p = camera.WorldToViewportPoint(corner);
            const float tolerance = 0.0002f;
            if (p.x < safe.xMin - tolerance || p.x > safe.xMax + tolerance ||
                p.y < safe.yMin - tolerance || p.y > safe.yMax + tolerance ||
                p.z < camera.nearClipPlane || p.z > camera.farClipPlane)
                throw new Exception("Robot left safe camera frame: " + p + " / " + safe);
        }
    }
}
#endif

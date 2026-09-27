using UnityEngine;

namespace X02Competition.Robot
{
    public struct GestureArmPose
    {
        public float ShoulderPitch, ShoulderRoll, ShoulderYaw, Elbow;
        public float WristYaw, WristPitch, WristRoll;
    }

    public struct GesturePose
    {
        public GestureArmPose Left, Right;
        public float HeadYaw;
    }

    /// <summary>Upper-body offsets in degrees. No root, spine or leg commands.</summary>
    public static class GestureMotion
    {
        public const float DefaultWaveDuration = 6f;
        public const float DefaultOpenDuration = 6.5f;

        // Quintic easing: zero velocity and acceleration at both ends of a transition.
        public static float Ease(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * t * (t * (6f * t - 15f) + 10f);
        }

        static float Pulse(float p, float begin, float raised, float lower, float end) =>
            Ease((p - begin) / (raised - begin)) * (1f - Ease((p - lower) / (end - lower)));

        public static GesturePose Wave(float p, float duration, float shoulderLift,
            float elbowSwing, float swingHz, float elbowBend, float wristSwing)
        {
            p = Mathf.Clamp01(p);
            var lift = Pulse(p, 0.03f, 0.29f, 0.76f, 0.98f);
            var elbow = Pulse(p, 0f, 0.25f, 0.82f, 1f);
            var presentation = Pulse(p, 0.10f, 0.32f, 0.76f, 0.98f);
            var waving = Pulse(p, 0.31f, 0.40f, 0.68f, 0.79f);
            var progress = Mathf.Clamp01((p - 0.31f) / 0.48f);
            var swing = waving * (1f - 0.2f * progress) *
                        Mathf.Sin(2f * Mathf.PI * swingHz * duration * (p - 0.31f));
            var support = Pulse(p, 0.05f, 0.28f, 0.75f, 1f);
            return new GesturePose
            {
                Right = new GestureArmPose
                {
                    ShoulderPitch = -28f * lift,
                    ShoulderRoll = -shoulderLift * lift - 2f * swing,
                    ShoulderYaw = -12f * presentation,
                    Elbow = -elbowBend * elbow - elbowSwing * swing,
                    WristYaw = -20f * presentation + 3f * swing,
                    WristPitch = -5f * presentation + 2f * swing,
                    WristRoll = 8f * presentation + wristSwing * swing,
                },
                Left = new GestureArmPose
                {
                    ShoulderPitch = -3f * support,
                    ShoulderRoll = 5f * support,
                    Elbow = -8f * support,
                },
                HeadYaw = 3f * Pulse(p, 0f, 0.22f, 0.70f, 0.96f),
            };
        }

        public static GesturePose OpenArms(float p, float spread, float elbowBend)
        {
            p = Mathf.Clamp01(p);
            // About 130ms of lead at the default duration, rather than exact mirrored timing.
            var right = Pulse(p, 0.03f, 0.31f, 0.72f, 0.97f);
            var left = Pulse(p, 0.05f, 0.33f, 0.74f, 0.99f);
            var elbows = Pulse(p, 0f, 0.24f, 0.82f, 1f);
            var offer = Pulse(p, 0.23f, 0.41f, 0.64f, 0.79f);
            var settle = Mathf.Sin(Mathf.PI * Mathf.Clamp01((p - 0.34f) / 0.37f));
            settle *= settle; // A single soft expansion during the hold.
            return new GesturePose
            {
                Left = OpenArm(1f, left, elbows, offer, settle, spread, elbowBend),
                Right = OpenArm(-1f, right, elbows, offer, settle, spread, elbowBend),
                HeadYaw = 2f * Pulse(p, 0.06f, 0.30f, 0.52f, 0.84f),
            };
        }

        static GestureArmPose OpenArm(float side, float lift, float elbow, float offer,
            float settle, float spread, float elbowBend) => new GestureArmPose
        {
            ShoulderPitch = -16f * lift - 4f * offer,
            ShoulderRoll = side * (spread * lift + 2.5f * settle),
            ShoulderYaw = side * 10f * lift,
            Elbow = -elbowBend * elbow + 2f * settle,
            WristYaw = side * 30f * lift,
            WristPitch = -6f * lift,
            WristRoll = side * 8f * lift,
        };
    }
}

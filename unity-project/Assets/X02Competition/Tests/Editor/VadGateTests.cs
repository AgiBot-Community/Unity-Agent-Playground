using NUnit.Framework;
using X02Competition.Robot;

namespace X02Competition.Tests.Editor
{
    /// <summary>VAD 状态机测试：start/commit 事件序列、超长截断、半双工冻结、过短丢弃。</summary>
    public class VadGateTests
    {
        static float[] Loud(int n = 1600)
        {
            var f = new float[n];
            for (int i = 0; i < n; i++) f[i] = 0.5f;
            return f;
        }

        static float[] Quiet(int n = 1600)
        {
            return new float[n]; // 全零
        }

        [Test]
        public void Silence_ProducesNoEvents()
        {
            var v = new VadGate();
            for (int i = 0; i < 50; i++)
                Assert.AreEqual(VadGate.Event.None, v.Feed(Quiet(), i * 0.1));
            Assert.IsFalse(v.InSpeech);
        }

        [Test]
        public void LoudFrame_StartsSpeech()
        {
            var v = new VadGate();
            Assert.AreEqual(VadGate.Event.SpeechStarted, v.Feed(Loud(), 0.1));
            Assert.IsTrue(v.InSpeech);
        }

        [Test]
        public void SilenceAfterSpeech_EndsSpeech()
        {
            var v = new VadGate { SilenceMs = 500 };
            Assert.AreEqual(VadGate.Event.SpeechStarted, v.Feed(Loud(), 0.0));
            double t = 0.1;
            // 持续说话 1s
            while (t < 1.0)
            {
                Assert.AreEqual(VadGate.Event.None, v.Feed(Loud(), t));
                t += 0.1;
            }
            // 静音：不足 SilenceMs 不结束
            Assert.AreEqual(VadGate.Event.None, v.Feed(Quiet(), 1.3));
            Assert.IsTrue(v.InSpeech);
            // 超过 SilenceMs → commit
            Assert.AreEqual(VadGate.Event.SpeechEnded, v.Feed(Quiet(), 1.7));
            Assert.IsFalse(v.InSpeech);
        }

        [Test]
        public void MaxSpeech_ForcesEnd()
        {
            var v = new VadGate { MaxSpeechMs = 1000, SilenceMs = 600 };
            Assert.AreEqual(VadGate.Event.SpeechStarted, v.Feed(Loud(), 0.0));
            var t = 0.1;
            while (t < 1.0)
            {
                Assert.AreEqual(VadGate.Event.None, v.Feed(Loud(), t));
                t += 0.1;
            }
            Assert.AreEqual(VadGate.Event.SpeechEnded, v.Feed(Loud(), 1.1));
        }

        [Test]
        public void TooShortSpeech_StillEnds_ProtocolRequiresCommit()
        {
            // start 已上行 → 无论多短都必须 commit 闭合事件（协议时序）
            var v = new VadGate { SilenceMs = 100 };
            Assert.AreEqual(VadGate.Event.SpeechStarted, v.Feed(Loud(), 0.0));
            Assert.AreEqual(VadGate.Event.SpeechEnded, v.Feed(Quiet(), 0.2));
        }

        [Test]
        public void DisabledMidSpeech_ForcesEnd()
        {
            var v = new VadGate();
            Assert.AreEqual(VadGate.Event.SpeechStarted, v.Feed(Loud(), 0.0));
            v.Enabled = false; // TTS 开始播报
            Assert.AreEqual(VadGate.Event.SpeechEnded, v.Feed(Loud(), 0.1));
            // 冻结中保持静默
            Assert.AreEqual(VadGate.Event.None, v.Feed(Loud(), 0.2));
            Assert.AreEqual(VadGate.Event.None, v.Feed(Quiet(), 0.3));
        }

        [Test]
        public void AfterEnd_CanStartAgain()
        {
            var v = new VadGate { SilenceMs = 100 };
            Assert.AreEqual(VadGate.Event.SpeechStarted, v.Feed(Loud(), 0.0));
            Assert.AreEqual(VadGate.Event.SpeechEnded, v.Feed(Quiet(), 0.3));
            Assert.AreEqual(VadGate.Event.SpeechStarted, v.Feed(Loud(), 0.5));
        }

        [Test]
        public void ComputeRms_IsSane()
        {
            Assert.AreEqual(0f, VadGate.ComputeRms(Quiet()));
            Assert.Greater(VadGate.ComputeRms(Loud()), 0.4f);
            Assert.AreEqual(0f, VadGate.ComputeRms(new float[0]));
        }
    }
}

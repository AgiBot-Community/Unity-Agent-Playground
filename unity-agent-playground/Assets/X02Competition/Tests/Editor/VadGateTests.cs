using NUnit.Framework;
using X02Competition.Robot;

namespace X02Competition.Tests.Editor
{
    /// <summary>
    /// VAD 状态机单元测试。
    /// </summary>
    public class VadGateTests
    {
        VadGate _vad;

        [SetUp]
        public void SetUp()
        {
            _vad = new VadGate
            {
                StartRms = 0.02f,
                StopRms = 0.008f,
                SilenceMs = 600,
                MaxSpeechMs = 15000
            };
        }

        [Test]
        public void TestSpeechStart()
        {
            // 静音状态，高能量应触发 SpeechStarted
            var result = _vad.FeedRms(0.03f, 1.0);
            Assert.AreEqual(VadGate.Event.SpeechStarted, result);
            Assert.IsTrue(_vad.InSpeech);
        }

        [Test]
        public void TestSpeechContinue()
        {
            // 开始说话
            _vad.FeedRms(0.03f, 1.0);

            // 持续说话，应返回 None
            var result = _vad.FeedRms(0.03f, 1.1);
            Assert.AreEqual(VadGate.Event.None, result);
            Assert.IsTrue(_vad.InSpeech);
        }

        [Test]
        public void TestSpeechEndBySilence()
        {
            // 开始说话
            _vad.FeedRms(0.03f, 1.0);

            // 持续说话
            _vad.FeedRms(0.03f, 1.1);

            // 静音超过阈值，应触发 SpeechEnded
            var result = _vad.FeedRms(0.001f, 2.0);
            Assert.AreEqual(VadGate.Event.SpeechEnded, result);
            Assert.IsFalse(_vad.InSpeech);
        }

        [Test]
        public void TestSpeechEndByMaxDuration()
        {
            // 开始说话
            _vad.FeedRms(0.03f, 1.0);

            // 持续说话超过最大时长（15秒）
            var result = _vad.FeedRms(0.03f, 17.0);
            Assert.AreEqual(VadGate.Event.SpeechEnded, result);
            Assert.IsFalse(_vad.InSpeech);
        }

        [Test]
        public void TestGateDisabled()
        {
            // 开始说话
            _vad.FeedRms(0.03f, 1.0);
            Assert.IsTrue(_vad.InSpeech);

            // 禁用门控
            _vad.Enabled = false;

            // 应立即结束说话
            var result = _vad.FeedRms(0.03f, 1.1);
            Assert.AreEqual(VadGate.Event.SpeechEnded, result);
            Assert.IsFalse(_vad.InSpeech);
        }

        [Test]
        public void TestGateDisabledNoStart()
        {
            // 禁用门控
            _vad.Enabled = false;

            // 高能量也不应触发 SpeechStarted
            var result = _vad.FeedRms(0.03f, 1.0);
            Assert.AreEqual(VadGate.Event.None, result);
            Assert.IsFalse(_vad.InSpeech);
        }

        [Test]
        public void TestReset()
        {
            // 开始说话
            _vad.FeedRms(0.03f, 1.0);
            Assert.IsTrue(_vad.InSpeech);

            // 重置
            _vad.Reset();
            Assert.IsFalse(_vad.InSpeech);
        }

        [Test]
        public void TestComputeRms()
        {
            var samples = new float[] { 0.1f, -0.1f, 0.2f, -0.2f };
            var rms = VadGate.ComputeRms(samples);

            // RMS = sqrt((0.01 + 0.01 + 0.04 + 0.04) / 4) = sqrt(0.025) ≈ 0.158
            Assert.AreEqual(0.158f, rms, 0.001f);
        }

        [Test]
        public void TestComputeRmsEmpty()
        {
            var rms = VadGate.ComputeRms(new float[0]);
            Assert.AreEqual(0f, rms);
        }

        [Test]
        public void TestComputeRmsNull()
        {
            var rms = VadGate.ComputeRms(null);
            Assert.AreEqual(0f, rms);
        }

        [Test]
        public void TestSilenceDetection()
        {
            // 开始说话
            _vad.FeedRms(0.03f, 1.0);

            // 持续说话 0.5 秒
            _vad.FeedRms(0.03f, 1.5);

            // 开始静音 0.4 秒（未超过 0.6 秒阈值）
            var result1 = _vad.FeedRms(0.001f, 1.9);
            Assert.AreEqual(VadGate.Event.None, result1);
            Assert.IsTrue(_vad.InSpeech);

            // 继续静音 0.3 秒（累计 0.7 秒，超过阈值）
            var result2 = _vad.FeedRms(0.001f, 2.2);
            Assert.AreEqual(VadGate.Event.SpeechEnded, result2);
            Assert.IsFalse(_vad.InSpeech);
        }

        [Test]
        public void TestSilenceInterrupted()
        {
            // 开始说话
            _vad.FeedRms(0.03f, 1.0);

            // 静音 0.4 秒
            _vad.FeedRms(0.001f, 1.4);

            // 重新说话（静音计时器重置）
            _vad.FeedRms(0.03f, 1.5);

            // 再次静音 0.5 秒（未超过阈值）
            var result = _vad.FeedRms(0.001f, 2.0);
            Assert.AreEqual(VadGate.Event.None, result);
            Assert.IsTrue(_vad.InSpeech);
        }
    }
}

using System;
using NUnit.Framework;
using X02Competition.Protocol;

namespace X02Competition.Tests.Editor
{
    /// <summary>鉴权算法测试：与官方 SDK linksky_client._build_headers 的签名串完全对齐。</summary>
    public class AuthVerifierTests
    {
        const string Secret = "0123456789abcdef";
        const string Path = "/api/V1/open-portal/app/wss/agent-sdk";

        static string FreshTs() =>
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();

        [Test]
        public void Signature_MatchesSdkAlgorithm()
        {
            // 固定向量：payload = "GET\n<path>\n<ts>\n<nonce>"
            var sig = AuthVerifier.ComputeSignature(Secret, "GET", Path, "1700000000000", "aabbccdd");
            // 手工 HMAC-SHA256 参考值（用 .NET 独立计算）
            var expected = ComputeReference(Secret, "GET\n" + Path + "\n1700000000000\naabbccdd");
            Assert.AreEqual(expected, sig);
        }

        [Test]
        public void Verify_AcceptsValidHeaders()
        {
            var ts = FreshTs();
            var sig = AuthVerifier.ComputeSignature(Secret, "GET", Path, ts, "nonce-1");
            Assert.IsTrue(AuthVerifier.Verify(Path, "app", "key", ts, "nonce-1", sig,
                Secret, "app", "key", 300));
        }

        [Test]
        public void Verify_RejectsTamperedSignature()
        {
            var ts = FreshTs();
            var sig = AuthVerifier.ComputeSignature(Secret, "GET", Path, ts, "nonce-1");
            var tampered = sig == "0" ? "1" : "0" + sig.Substring(1);
            Assert.IsFalse(AuthVerifier.Verify(Path, "app", "key", ts, "nonce-1", tampered,
                Secret, "app", "key", 300));
        }

        [Test]
        public void Verify_RejectsWrongPath()
        {
            var ts = FreshTs();
            var sig = AuthVerifier.ComputeSignature(Secret, "GET", Path, ts, "nonce-1");
            Assert.IsFalse(AuthVerifier.Verify("/other/path", "app", "key", ts, "nonce-1", sig,
                Secret, "app", "key", 300));
        }

        [Test]
        public void Verify_RejectsExpiredTimestamp()
        {
            var old = (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - 3600_000).ToString();
            var sig = AuthVerifier.ComputeSignature(Secret, "GET", Path, old, "n");
            Assert.IsFalse(AuthVerifier.Verify(Path, "app", "key", old, "n", sig,
                Secret, "app", "key", 300));
        }

        [Test]
        public void Verify_RejectsWrongCredentials()
        {
            var ts = FreshTs();
            var sig = AuthVerifier.ComputeSignature(Secret, "GET", Path, ts, "n");
            Assert.IsFalse(AuthVerifier.Verify(Path, "app", "key", ts, "n", sig,
                Secret, "expected-app", "key", 300));
        }

        [Test]
        public void Verify_RejectsMissingHeaders()
        {
            var ts = FreshTs();
            Assert.IsFalse(AuthVerifier.Verify(Path, null, "key", ts, "n", "sig",
                Secret, null, null, 300));
        }

        static string ComputeReference(string secret, string payload)
        {
            using (var h = new System.Security.Cryptography.HMACSHA256(
                System.Text.Encoding.UTF8.GetBytes(secret)))
            {
                var bytes = h.ComputeHash(System.Text.Encoding.UTF8.GetBytes(payload));
                var sb = new System.Text.StringBuilder(bytes.Length * 2);
                foreach (var b in bytes) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }
    }
}

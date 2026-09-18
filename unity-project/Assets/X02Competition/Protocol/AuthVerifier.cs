using System;
using System.Security.Cryptography;
using System.Text;

namespace X02Competition.Protocol
{
    /// <summary>
    /// v1.4.0 HMAC-SHA256 五元组握手鉴权。
    /// 签名串：\"GET\n&lt;path&gt;\n&lt;timestamp&gt;\n&lt;nonce&gt;\"，密钥 appSecret，小写 hex 输出。
    /// 行为对齐官方 SDK linksky_client._build_headers：WS 握手即认证（无连接后 auth 应答消息），
    /// 网关在 HTTP Upgrade 阶段决定接受或拒绝（401）。
    /// </summary>
    public static class AuthVerifier
    {
        public const string HeaderAppId = "X-App-Id";
        public const string HeaderAppKey = "X-App-Key";
        public const string HeaderTimestamp = "X-Timestamp";
        public const string HeaderNonce = "X-Nonce";
        public const string HeaderSignature = "X-Signature";
        public const string HeaderCallbackTypes = "X-Callback-Types";

        /// <summary>计算签名（与 SDK 算法完全一致；测试用例用它生成期望值）。</summary>
        public static string ComputeSignature(string appSecret, string method, string path,
            string timestamp, string nonce)
        {
            var payload = $"{method}\n{path}\n{timestamp}\n{nonce}";
            using (var h = new HMACSHA256(Encoding.UTF8.GetBytes(appSecret ?? "")))
            {
                var bytes = h.ComputeHash(Encoding.UTF8.GetBytes(payload));
                var sb = new StringBuilder(bytes.Length * 2);
                foreach (var b in bytes) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }

        /// <summary>
        /// 校验握手请求。
        /// expectedAppId/expectedAppKey：配置了（非空）才比对；签名、时间戳偏移始终校验。
        /// timestamp 为毫秒 Unix 时间戳字符串。
        /// </summary>
        public static bool Verify(string path, string appId, string appKey, string timestamp,
            string nonce, string signature, string appSecret,
            string expectedAppId, string expectedAppKey, double maxClockSkewSec)
        {
            if (string.IsNullOrEmpty(appId) || string.IsNullOrEmpty(timestamp) ||
                string.IsNullOrEmpty(nonce) || string.IsNullOrEmpty(signature))
                return false;

            if (!string.IsNullOrEmpty(expectedAppId) && appId != expectedAppId) return false;
            if (!string.IsNullOrEmpty(expectedAppKey) && appKey != expectedAppKey) return false;

            long ts;
            if (!long.TryParse(timestamp, out ts)) return false;
            var skewMs = Math.Abs(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - ts);
            if (skewMs > maxClockSkewSec * 1000.0) return false;

            var expected = ComputeSignature(appSecret, "GET", path, timestamp, nonce);
            return string.Equals(expected, signature, StringComparison.Ordinal);
        }
    }
}

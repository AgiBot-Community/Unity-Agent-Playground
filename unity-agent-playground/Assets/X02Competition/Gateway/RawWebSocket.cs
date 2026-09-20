using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace X02Competition.Gateway
{
    /// <summary>
    /// 手写 WebSocket 传输层（服务端方向，TcpListener + RFC6455）。
    /// 背景：Unity Mono 的 HttpListenerRequest.IsWebSocketRequest 硬编码返回 false、
    /// HttpListenerContext.AcceptWebSocketAsync 抛 NotImplementedException（mcs 空存根），
    /// System.Net.WebSockets 服务端路径在 Unity 内完全不可用，故在此自行实现
    /// HTTP 请求解析、101 升级握手与 WS 帧编解码。
    /// 对上提供与 System.Net.WebSockets.WebSocket 兼容的子集 API（GatewaySession 直接使用）。
    /// </summary>
    public sealed class RawHttpRequest
    {
        public string Method;
        public string Target;                                       // 原始请求目标（含可能的 query）
        public readonly Dictionary<string, string> Headers =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>请求目标去掉 query 的 path 部分（= 签名所用 path）。</summary>
        public string Path
        {
            get
            {
                var t = Target ?? "";
                int q = t.IndexOf('?');
                return q >= 0 ? t.Substring(0, q) : t;
            }
        }
    }

    /// <summary>HTTP/WS 握手与纯 HTTP 应答的静态工具。</summary>
    public static class RawWs
    {
        public const string WsGuid = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";

        /// <summary>
        /// 阻塞读取 HTTP 请求头（至 \r\n\r\n）。
        /// buf 需由调用方持有；返回后 buf[0..leftover) 为头部之后已读到的字节（透传给连接层）。
        /// </summary>
        public static RawHttpRequest ReadRequest(NetworkStream io, byte[] buf, out int leftover)
        {
            int total = 0;
            while (true)
            {
                if (total == buf.Length) throw new IOException("http request head too large");
                int n = io.Read(buf, total, buf.Length - total);
                if (n <= 0) throw new IOException("connection closed before request head");
                total += n;
                int headEnd = FindHeaderEnd(buf, total);
                if (headEnd >= 0)
                {
                    var req = ParseHead(Encoding.ASCII.GetString(buf, 0, headEnd));
                    leftover = total - (headEnd + 4);
                    if (leftover > 0) Array.Copy(buf, headEnd + 4, buf, 0, leftover);
                    return req;
                }
            }
        }

        static int FindHeaderEnd(byte[] buf, int total)
        {
            for (int i = 0; i + 3 < total; i++)
            {
                if (buf[i] == 0x0D && buf[i + 1] == 0x0A && buf[i + 2] == 0x0D && buf[i + 3] == 0x0A)
                    return i;
            }
            return -1;
        }

        static RawHttpRequest ParseHead(string head)
        {
            var req = new RawHttpRequest();
            int lineEnd = head.IndexOf('\n');
            if (lineEnd < 0) throw new IOException("bad request line");
            var first = head.Substring(0, lineEnd).TrimEnd('\r');
            var parts = first.Split(' ');
            if (parts.Length < 3) throw new IOException("bad request line: " + first);
            req.Method = parts[0];
            req.Target = parts[1];

            int pos = lineEnd + 1;
            while (pos < head.Length)
            {
                lineEnd = head.IndexOf('\n', pos);
                if (lineEnd < 0) lineEnd = head.Length;
                var line = head.Substring(pos, lineEnd - pos).TrimEnd('\r');
                pos = lineEnd + 1;
                if (line.Length == 0) continue;
                int colon = line.IndexOf(':');
                if (colon <= 0) continue;
                req.Headers[line.Substring(0, colon).Trim()] = line.Substring(colon + 1).Trim();
            }
            return req;
        }

        /// <summary>RFC6455 §4.2.1：GET + Upgrade: websocket + Connection 含 Upgrade + Key + Version 13。</summary>
        public static bool IsWebSocketUpgrade(RawHttpRequest req)
        {
            if (!string.Equals(req.Method, "GET", StringComparison.OrdinalIgnoreCase)) return false;
            string upgrade, connection, key, version;
            if (!req.Headers.TryGetValue("Upgrade", out upgrade) ||
                upgrade.IndexOf("websocket", StringComparison.OrdinalIgnoreCase) < 0) return false;
            if (!req.Headers.TryGetValue("Connection", out connection) ||
                connection.IndexOf("upgrade", StringComparison.OrdinalIgnoreCase) < 0) return false;
            if (!req.Headers.TryGetValue("Sec-WebSocket-Key", out key) || key.Length == 0) return false;
            if (!req.Headers.TryGetValue("Sec-WebSocket-Version", out version)) return false;
            return version.Trim() == "13";
        }

        public static string ComputeAccept(string clientKey)
        {
            using (var sha1 = SHA1.Create())
                return Convert.ToBase64String(
                    sha1.ComputeHash(Encoding.ASCII.GetBytes(clientKey + WsGuid)));
        }

        public static void WriteSwitchingProtocols(NetworkStream io, string clientKey)
        {
            var sb = new StringBuilder();
            sb.Append("HTTP/1.1 101 Switching Protocols\r\n");
            sb.Append("Upgrade: websocket\r\n");
            sb.Append("Connection: Upgrade\r\n");
            sb.Append("Sec-WebSocket-Accept: ").Append(ComputeAccept(clientKey)).Append("\r\n");
            sb.Append("\r\n");
            var bytes = Encoding.ASCII.GetBytes(sb.ToString());
            io.Write(bytes, 0, bytes.Length);
        }

        /// <summary>普通 HTTP 拒绝应答（401/404/503/...），带 Connection: close。</summary>
        public static void WritePlainResponse(NetworkStream io, int code, string reason, string msg)
        {
            var body = Encoding.UTF8.GetBytes(msg ?? "");
            var sb = new StringBuilder();
            sb.Append("HTTP/1.1 ").Append(code).Append(' ').Append(reason).Append("\r\n");
            sb.Append("Content-Type: text/plain; charset=utf-8\r\n");
            sb.Append("Content-Length: ").Append(body.Length).Append("\r\n");
            sb.Append("Connection: close\r\n\r\n");
            var head = Encoding.ASCII.GetBytes(sb.ToString());
            io.Write(head, 0, head.Length);
            if (body.Length > 0) io.Write(body, 0, body.Length);
        }
    }

    /// <summary>
    /// 升级完成后的 WS 连接：RFC6455 帧收发。
    /// 线程模型：接收在会话接收线程（阻塞式）；发送用锁串行化（数据帧 + ping/pong/close）。
    /// Task 均为同步完成后立即返回（调用方用 GetAwaiter().GetResult()/.Wait() 阻塞消费）。
    /// </summary>
    public sealed class ServerWsConnection : IDisposable
    {
        const long MaxMessageBytes = 16 * 1024 * 1024;
        static readonly Task DoneTask = Task.FromResult<object>(null);

        readonly TcpClient _tcp;
        readonly NetworkStream _io;
        readonly object _sendLock = new object();
        readonly byte[] _buf = new byte[64 * 1024];   // 接收缓冲（仅接收线程使用）
        readonly byte[] _h = new byte[8];             // 帧头解析暂存（仅接收线程）
        readonly byte[] _ctl = new byte[125];          // 控制帧载荷（仅接收线程）
        readonly byte[] _maskKey = new byte[4];

        int _bufStart, _bufEnd;
        int _remain;                                  // 当前数据帧剩余载荷
        int _maskPos;
        bool _maskedFrame;
        bool _frameFin;
        WebSocketMessageType _msgType;
        bool _inMessage;
        volatile bool _closeSent;
        volatile bool _disposed;

        /// <summary>seed/seedCount：握手读缓冲中已越过头部的字节（先于首帧到达的数据）。</summary>
        public ServerWsConnection(TcpClient tcp, NetworkStream io, byte[] seed, int seedCount)
        {
            _tcp = tcp;
            _io = io;
            if (seedCount > 0)
            {
                Array.Copy(seed, 0, _buf, 0, seedCount);
                _bufEnd = seedCount;
            }
        }

        // ---------------- 接收 ----------------

        public Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer,
            CancellationToken ct)
        {
            if (_disposed) throw new ObjectDisposedException("ServerWsConnection");
            while (true)
            {
                if (_remain > 0)
                {
                    int want = Math.Min(buffer.Count, _remain);
                    if (want <= 0)
                        return Task.FromResult(new WebSocketReceiveResult(0, _msgType, false));
                    int n = ReadPartial(buffer.Array, buffer.Offset, want);
                    if (_maskedFrame) ApplyMask(buffer.Array, buffer.Offset, n);
                    _remain -= n;
                    bool eom = _remain == 0 && _frameFin;
                    if (eom) _inMessage = false;
                    return Task.FromResult(new WebSocketReceiveResult(n, _msgType, eom));
                }

                // ---- 解析下一帧头 ----
                ReadExact(_h, 0, 2);
                int b0 = _h[0], b1 = _h[1];
                bool fin = (b0 & 0x80) != 0;
                if ((b0 & 0x70) != 0)
                    return Fail(WebSocketCloseStatus.ProtocolError, "rsv bits set");
                int opcode = b0 & 0x0F;
                bool masked = (b1 & 0x80) != 0;
                long len = b1 & 0x7F;
                if (len == 126)
                {
                    ReadExact(_h, 0, 2);
                    len = ((long)_h[0] << 8) | _h[1];
                }
                else if (len == 127)
                {
                    ReadExact(_h, 0, 8);
                    len = 0;
                    for (int i = 0; i < 8; i++) len = (len << 8) | _h[i];
                    if (len < 0) return Fail(WebSocketCloseStatus.MessageTooBig, "bad length");
                }
                if (len > MaxMessageBytes)
                    return Fail(WebSocketCloseStatus.MessageTooBig, "frame too large");

                bool isControl = (opcode & 0x8) != 0;
                if (isControl && (!fin || len > 125))
                    return Fail(WebSocketCloseStatus.ProtocolError, "bad control frame");

                if (masked)
                {
                    ReadExact(_maskKey, 0, 4);
                    _maskPos = 0;
                }

                switch (opcode)
                {
                    case 0x1: _msgType = WebSocketMessageType.Text; _inMessage = true; break;
                    case 0x2: _msgType = WebSocketMessageType.Binary; _inMessage = true; break;
                    case 0x0:
                        if (!_inMessage)
                            return Fail(WebSocketCloseStatus.ProtocolError,
                                "unexpected continuation");
                        break;
                    case 0x8:
                        ReadExact(_ctl, 0, (int)len);
                        if (masked) ApplyMask(_ctl, 0, (int)len);
                        return HandleClose((int)len);
                    case 0x9: // ping → 立即回 pong（同载荷）
                        ReadExact(_ctl, 0, (int)len);
                        if (masked) ApplyMask(_ctl, 0, (int)len);
                        SendControl(0xA, _ctl, (int)len);
                        continue;
                    case 0xA: // pong，丢弃
                        ReadExact(_ctl, 0, (int)len);
                        continue;
                    default:
                        return Fail(WebSocketCloseStatus.ProtocolError, "bad opcode " + opcode);
                }

                // 客户端→服务端数据帧必须掩码（RFC6455 §5.1）
                if (!masked && len > 0)
                    return Fail(WebSocketCloseStatus.ProtocolError, "unmasked client frame");
                _maskedFrame = masked;
                _remain = (int)len;
                _frameFin = fin;
            }
        }

        Task<WebSocketReceiveResult> HandleClose(int len)
        {
            WebSocketCloseStatus status = WebSocketCloseStatus.NormalClosure;
            string reason = "";
            if (len >= 2)
            {
                int code = (_ctl[0] << 8) | _ctl[1];
                status = ToCloseStatus(code);
                if (len > 2) reason = Encoding.UTF8.GetString(_ctl, 2, len - 2);
            }
            TrySendClose(status, reason);
            return Task.FromResult(new WebSocketReceiveResult(0, WebSocketMessageType.Close,
                true, status, reason));
        }

        static WebSocketCloseStatus ToCloseStatus(int code)
        {
            switch (code)
            {
                case 1000: return WebSocketCloseStatus.NormalClosure;
                case 1001: return WebSocketCloseStatus.EndpointUnavailable;
                case 1002: return WebSocketCloseStatus.ProtocolError;
                case 1007: return WebSocketCloseStatus.InvalidMessageType;
                case 1008: return WebSocketCloseStatus.PolicyViolation;
                case 1009: return WebSocketCloseStatus.MessageTooBig;
                case 1011: return WebSocketCloseStatus.InternalServerError;
                default: return (WebSocketCloseStatus)code;
            }
        }

        /// <summary>协议错误：发 close 帧并返回 Close 结果（会话层随后关闭）。</summary>
        Task<WebSocketReceiveResult> Fail(WebSocketCloseStatus status, string reason)
        {
            TrySendClose(status, reason);
            return Task.FromResult(new WebSocketReceiveResult(0, WebSocketMessageType.Close,
                true, status, reason));
        }

        // ---------------- 发送 ----------------

        public Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType,
            bool endOfMessage, CancellationToken ct)
        {
            if (_disposed) throw new ObjectDisposedException("ServerWsConnection");
            if (buffer.Count == 0) return DoneTask;
            int op = messageType == WebSocketMessageType.Binary ? 0x2 : 0x1;
            lock (_sendLock)
            {
                if (_closeSent || _disposed) throw new IOException("connection closing");
                var header = new byte[10];
                int hl = WriteFrameHeader(header, op, buffer.Count, true);
                _io.Write(header, 0, hl);
                _io.Write(buffer.Array, buffer.Offset, buffer.Count);
            }
            return DoneTask;
        }

        public Task CloseAsync(WebSocketCloseStatus status, string reason, CancellationToken ct)
        {
            if (!_disposed) TrySendClose(status, reason);
            return DoneTask;
        }

        void SendControl(int opcode, byte[] payload, int count)
        {
            if (_closeSent || _disposed) return;
            lock (_sendLock)
            {
                if (_closeSent || _disposed) return;
                var header = new byte[10];
                int hl = WriteFrameHeader(header, opcode, count, true);
                _io.Write(header, 0, hl);
                if (count > 0) _io.Write(payload, 0, count);
            }
        }

        void TrySendClose(WebSocketCloseStatus status, string reason)
        {
            if (_closeSent || _disposed) return;
            lock (_sendLock)
            {
                if (_closeSent || _disposed) return;
                _closeSent = true;
                int code = (int)status;
                var reasonBytes = string.IsNullOrEmpty(reason)
                    ? new byte[0]
                    : Encoding.UTF8.GetBytes(reason);
                int reasonLen = Math.Min(reasonBytes.Length, 123);   // 控制帧载荷 ≤125（含 2B 状态码）
                var header = new byte[10];
                int hl = WriteFrameHeader(header, 0x8, 2 + reasonLen, true);
                var payload = new byte[2 + reasonLen];
                payload[0] = (byte)(code >> 8);
                payload[1] = (byte)(code & 0xFF);
                if (reasonLen > 0) Array.Copy(reasonBytes, 0, payload, 2, reasonLen);
                _io.Write(header, 0, hl);
                _io.Write(payload, 0, payload.Length);
            }
        }

        /// <summary>服务端→客户端帧头：不掩码。返回写入长度。</summary>
        static int WriteFrameHeader(byte[] header, int opcode, long count, bool fin)
        {
            int hl = 0;
            header[hl++] = (byte)((fin ? 0x80 : 0x00) | opcode);
            if (count < 126) header[hl++] = (byte)count;
            else if (count <= 0xFFFF)
            {
                header[hl++] = 126;
                header[hl++] = (byte)(count >> 8);
                header[hl++] = (byte)(count & 0xFF);
            }
            else
            {
                header[hl++] = 127;
                for (int i = 7; i >= 0; i--) header[hl++] = (byte)((count >> (8 * i)) & 0xFF);
            }
            return hl;
        }

        // ---------------- 底层读取 ----------------

        /// <summary>从缓存/流中读取 count 字节（阻塞直到读满或断连抛异常）。</summary>
        void ReadExact(byte[] outBuf, int offset, int count)
        {
            while (count > 0)
            {
                if (_bufStart < _bufEnd)
                {
                    int take = Math.Min(count, _bufEnd - _bufStart);
                    Buffer.BlockCopy(_buf, _bufStart, outBuf, offset, take);
                    _bufStart += take;
                    offset += take;
                    count -= take;
                    continue;
                }
                _bufStart = _bufEnd = 0;
                int n = _io.Read(_buf, 0, _buf.Length);
                if (n <= 0) throw new IOException("connection closed");
                _bufEnd = n;
            }
        }

        /// <summary>读取 1..maxCount 字节（至少 1 字节，阻塞）。优先消费缓存。</summary>
        int ReadPartial(byte[] outBuf, int offset, int maxCount)
        {
            if (_bufStart < _bufEnd)
            {
                int take = Math.Min(maxCount, _bufEnd - _bufStart);
                Buffer.BlockCopy(_buf, _bufStart, outBuf, offset, take);
                _bufStart += take;
                return take;
            }
            int n = _io.Read(outBuf, offset, maxCount);
            if (n <= 0) throw new IOException("connection closed");
            return n;
        }

        void ApplyMask(byte[] data, int offset, int count)
        {
            for (int i = 0; i < count; i++)
            {
                data[offset + i] ^= _maskKey[_maskPos & 3];
                _maskPos++;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { ((IDisposable)_tcp).Dispose(); } catch { }
        }
    }
}

using Microsoft.Extensions.Logging;
using System;
using System.Buffers.Binary;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SkillzBot.Services.Proxy
{
    /// <summary>
    /// Native VLESS client for the plain protocol variants: tcp or ws transport, tls or none,
    /// no flow. Used as the ConnectCallback of a SocketsHttpHandler, so HttpClient performs
    /// its own end-to-end TLS inside the tunnel. REALITY and XTLS-Vision need a real core.
    /// </summary>
    public sealed class VlessConnector
    {
        private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(10);

        private readonly ProxyEndpoint _endpoint;
        private readonly byte[] _uuid;
        private readonly ILogger _logger;

        public VlessConnector(ProxyEndpoint endpoint, ILogger logger)
        {
            if (endpoint.Kind != ProxyKind.Vless) throw new ArgumentException("Not a vless endpoint", nameof(endpoint));
            if (endpoint.RequiresCore) throw new ArgumentException("This vless link needs xray-core (reality/flow/transport not supported natively)", nameof(endpoint));
            _endpoint = endpoint;
            _uuid = ParseUuid(endpoint.Auth);
            _logger = logger;
        }

        public async ValueTask<Stream> ConnectAsync(string targetHost, int targetPort, CancellationToken ct)
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(ConnectTimeout);
            var token = timeoutCts.Token;

            Stream transport = _endpoint.Network == "ws"
                ? await ConnectWebSocketAsync(token).ConfigureAwait(false)
                : await ConnectTcpAsync(token).ConfigureAwait(false);

            try
            {
                var header = BuildRequestHeader(_uuid, targetHost, targetPort);
                await transport.WriteAsync(header, token).ConfigureAwait(false);
                await transport.FlushAsync(token).ConfigureAwait(false);
                return new VlessStream(transport);
            }
            catch
            {
                transport.Dispose();
                throw;
            }
        }

        private async Task<Stream> ConnectTcpAsync(CancellationToken ct)
        {
            var client = new TcpClient { NoDelay = true };
            try
            {
                await client.ConnectAsync(_endpoint.BareHost, _endpoint.Port, ct).ConfigureAwait(false);
                Stream stream = client.GetStream();
                if (!_endpoint.IsTls) return new OwnedStream(stream, client);

                var ssl = new SslStream(stream, leaveInnerStreamOpen: false, userCertificateValidationCallback: _endpoint.Insecure ? AcceptAny : null);
                await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
                {
                    TargetHost = _endpoint.Sni,
                    EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                    ApplicationProtocols = ParseAlpn(),
                }, ct).ConfigureAwait(false);
                return new OwnedStream(ssl, client);
            }
            catch
            {
                client.Dispose();
                throw;
            }
        }

        private System.Collections.Generic.List<SslApplicationProtocol> ParseAlpn()
        {
            var alpn = _endpoint.Param("alpn");
            if (string.IsNullOrEmpty(alpn)) return null;
            var list = new System.Collections.Generic.List<SslApplicationProtocol>();
            foreach (var p in alpn.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                list.Add(new SslApplicationProtocol(p));
            return list.Count > 0 ? list : null;
        }

        private async Task<Stream> ConnectWebSocketAsync(CancellationToken ct)
        {
            var ws = new ClientWebSocket();
            try
            {
                if (_endpoint.Insecure)
                    ws.Options.RemoteCertificateValidationCallback = AcceptAny;
                var hostHeader = _endpoint.WsHost;
                if (!string.IsNullOrEmpty(hostHeader))
                {
                    try { ws.Options.SetRequestHeader("Host", hostHeader); }
                    catch (ArgumentException) { _logger.LogDebug("Runtime refused custom Host header for websocket; using URI host."); }
                }
                ws.Options.KeepAliveInterval = TimeSpan.Zero;

                // The URI host doubles as SNI. If a separate sni is given, prefer it so the TLS
                // handshake matches the server certificate; DNS for it must resolve to the server.
                string connectHost = _endpoint.IsTls && !string.IsNullOrEmpty(_endpoint.Param("sni")) ? _endpoint.Sni : _endpoint.BareHost;
                var uri = new UriBuilder(_endpoint.IsTls ? "wss" : "ws", connectHost, _endpoint.Port, _endpoint.Path).Uri;
                await ws.ConnectAsync(uri, ct).ConfigureAwait(false);
                return new WebSocketStream(ws);
            }
            catch
            {
                ws.Dispose();
                throw;
            }
        }

        private static bool AcceptAny(object sender, System.Security.Cryptography.X509Certificates.X509Certificate certificate, System.Security.Cryptography.X509Certificates.X509Chain chain, SslPolicyErrors errors) => true;

        /// <summary>
        /// VLESS request: version(0) | uuid(16) | addons length(0) | command TCP(1) | port(2, BE) | address type + address.
        /// </summary>
        public static byte[] BuildRequestHeader(byte[] uuid, string host, int port)
        {
            if (uuid == null || uuid.Length != 16) throw new ArgumentException("uuid must be 16 bytes", nameof(uuid));
            if (port < 1 || port > 65535) throw new ArgumentOutOfRangeException(nameof(port));

            byte[] addr;
            byte addrType;
            if (IPAddress.TryParse(host, out var ip))
            {
                addr = ip.GetAddressBytes();
                addrType = ip.AddressFamily == AddressFamily.InterNetworkV6 ? (byte)0x03 : (byte)0x01;
            }
            else
            {
                var domain = Encoding.ASCII.GetBytes(host);
                if (domain.Length > 255) throw new ArgumentException("host too long", nameof(host));
                addr = new byte[domain.Length + 1];
                addr[0] = (byte)domain.Length;
                domain.CopyTo(addr, 1);
                addrType = 0x02;
            }

            var header = new byte[1 + 16 + 1 + 1 + 2 + 1 + addr.Length];
            int i = 0;
            header[i++] = 0x00;                       // version
            uuid.CopyTo(header, i); i += 16;          // user id
            header[i++] = 0x00;                       // addons length
            header[i++] = 0x01;                       // command: TCP
            BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(i, 2), (ushort)port); i += 2;
            header[i++] = addrType;
            addr.CopyTo(header, i);
            return header;
        }

        /// <summary>
        /// Parses a canonical UUID into its 16 bytes in textual order. Like xray, a short
        /// arbitrary string (1-30 chars) is mapped to a UUIDv5 with the nil namespace.
        /// </summary>
        public static byte[] ParseUuid(string id)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentException("empty uuid", nameof(id));
            var hex = id.Replace("-", "");
            if (hex.Length == 32)
            {
                var bytes = new byte[16];
                for (int i = 0; i < 16; i++)
                    bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
                return bytes;
            }
            if (id.Length > 30) throw new ArgumentException("invalid uuid", nameof(id));

            using var sha1 = SHA1.Create();
            var input = new byte[16 + Encoding.UTF8.GetByteCount(id)];
            Encoding.UTF8.GetBytes(id, 0, id.Length, input, 16);
            var hash = sha1.ComputeHash(input);
            var uuid = new byte[16];
            Array.Copy(hash, uuid, 16);
            uuid[6] = (byte)((uuid[6] & 0x0F) | 0x50); // version 5
            uuid[8] = (byte)((uuid[8] & 0x3F) | 0x80); // RFC 4122 variant
            return uuid;
        }

        /// <summary>Strips the VLESS response header (version, addons) from the first read.</summary>
        private sealed class VlessStream : Stream
        {
            private readonly Stream _inner;
            private bool _headerConsumed;

            public VlessStream(Stream inner) { _inner = inner; }

            private async ValueTask ConsumeHeaderAsync(CancellationToken ct)
            {
                if (_headerConsumed) return;
                var head = new byte[2];
                await ReadFullyAsync(head, 0, 2, ct).ConfigureAwait(false);
                if (head[0] != 0x00) throw new IOException($"Unexpected VLESS response version {head[0]}");
                int addons = head[1];
                if (addons > 0)
                {
                    var skip = new byte[addons];
                    await ReadFullyAsync(skip, 0, addons, ct).ConfigureAwait(false);
                }
                _headerConsumed = true;
            }

            private async ValueTask ReadFullyAsync(byte[] buffer, int offset, int count, CancellationToken ct)
            {
                while (count > 0)
                {
                    int n = await _inner.ReadAsync(buffer.AsMemory(offset, count), ct).ConfigureAwait(false);
                    if (n <= 0) throw new IOException("VLESS server closed the connection before sending a response header");
                    offset += n;
                    count -= n;
                }
            }

            public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
            {
                await ConsumeHeaderAsync(ct).ConfigureAwait(false);
                return await _inner.ReadAsync(buffer, ct).ConfigureAwait(false);
            }

            public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) =>
                ReadAsync(buffer.AsMemory(offset, count), ct).AsTask();

            public override int Read(byte[] buffer, int offset, int count) =>
                ReadAsync(buffer, offset, count, CancellationToken.None).GetAwaiter().GetResult();

            public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default) => _inner.WriteAsync(buffer, ct);
            public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken ct) => _inner.WriteAsync(buffer, offset, count, ct);
            public override void Write(byte[] buffer, int offset, int count) => _inner.Write(buffer, offset, count);
            public override Task FlushAsync(CancellationToken ct) => _inner.FlushAsync(ct);
            public override void Flush() => _inner.Flush();

            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => true;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();

            protected override void Dispose(bool disposing)
            {
                if (disposing) _inner.Dispose();
                base.Dispose(disposing);
            }
        }

        /// <summary>Keeps the TcpClient alive for as long as its stream is in use.</summary>
        private sealed class OwnedStream : Stream
        {
            private readonly Stream _inner;
            private readonly IDisposable _owner;
            public OwnedStream(Stream inner, IDisposable owner) { _inner = inner; _owner = owner; }

            public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) => _inner.ReadAsync(buffer, ct);
            public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) => _inner.ReadAsync(buffer, offset, count, ct);
            public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
            public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default) => _inner.WriteAsync(buffer, ct);
            public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken ct) => _inner.WriteAsync(buffer, offset, count, ct);
            public override void Write(byte[] buffer, int offset, int count) => _inner.Write(buffer, offset, count);
            public override Task FlushAsync(CancellationToken ct) => _inner.FlushAsync(ct);
            public override void Flush() => _inner.Flush();
            public override bool CanRead => _inner.CanRead;
            public override bool CanSeek => false;
            public override bool CanWrite => _inner.CanWrite;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            protected override void Dispose(bool disposing)
            {
                if (disposing) { _inner.Dispose(); _owner.Dispose(); }
                base.Dispose(disposing);
            }
        }

        /// <summary>Presents a binary websocket as a byte stream.</summary>
        private sealed class WebSocketStream : Stream
        {
            private readonly ClientWebSocket _ws;
            public WebSocketStream(ClientWebSocket ws) { _ws = ws; }

            public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
            {
                if (buffer.Length == 0) return 0;
                var result = await _ws.ReceiveAsync(buffer, ct).ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close) return 0;
                return result.Count;
            }

            public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) =>
                ReadAsync(buffer.AsMemory(offset, count), ct).AsTask();
            public override int Read(byte[] buffer, int offset, int count) =>
                ReadAsync(buffer, offset, count, CancellationToken.None).GetAwaiter().GetResult();

            public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default) =>
                _ws.SendAsync(buffer, WebSocketMessageType.Binary, endOfMessage: true, ct);
            public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken ct) =>
                WriteAsync(buffer.AsMemory(offset, count), ct).AsTask();
            public override void Write(byte[] buffer, int offset, int count) =>
                WriteAsync(buffer, offset, count, CancellationToken.None).GetAwaiter().GetResult();

            public override Task FlushAsync(CancellationToken ct) => Task.CompletedTask;
            public override void Flush() { }
            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => true;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    try { _ws.Abort(); } catch { }
                    _ws.Dispose();
                }
                base.Dispose(disposing);
            }
        }
    }
}

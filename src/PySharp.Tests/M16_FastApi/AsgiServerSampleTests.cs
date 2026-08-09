// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using PySharpLib;

namespace PySharp.Tests.M16_FastApi;

/// <summary>
/// Verifies samples/asgi_server.py's demo ASGI app without opening a real socket — the same
/// hand-built scope/receive/send triple technique used throughout FASTAPI_PLAN.md's probing,
/// exercising the exact ASGI-callable logic `serve()` drives over a real socket (verified
/// separately, manually, over real HTTP via curl — see FASTAPI_PLAN.md Phase 3.2). Also confirms
/// the module imports cleanly without starting the server (the `if __name__ == "__main__"` guard).
/// <c>[Collection("asyncio-run")]</c>: PyEventLoop._running is a process-wide static (see
/// Runtime/Async.cs), so tests that each drive their own event loop via `asyncio.run` must never
/// run concurrently with each other or with any other asyncio.run-calling test class. This class
/// was missing the tag — found via a real, reproduced intermittent full-suite hang, root-caused
/// with VSTest's --blame-hang-dump-type full to a race on that static between two asyncio.run
/// calls in different, concurrently-scheduled test classes.
/// </summary>
[Collection("asyncio-run")]
public class AsgiServerSampleTests
{
    // bin/Debug/net10.0 -> Debug -> bin -> PySharp.Tests -> src -> repo root -> samples
    private static readonly string SamplesDir =
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "samples"));

    private static string Run(string body)
    {
        var writer = new StringWriter();
        var engine = new PyEngine(writer);
        engine.Importer.SearchPaths.Add(SamplesDir);
        engine.Run(body);
        return writer.ToString().TrimEnd('\n');
    }

    [Fact]
    public void Sample_imports_as_a_module_without_starting_the_server()
        => Assert.Equal("True\nTrue", Run("""
            import asgi_server
            print(callable(asgi_server.serve))
            print(callable(asgi_server.demo_app))
            """));

    [Fact]
    public void Demo_app_serves_the_index_route_via_a_real_ASGI_scope_receive_send_triple()
        => Assert.Equal(
            "[{'type': 'http.response.start', 'status': 200, " +
            "'headers': [(b'content-type', b'text/plain; charset=utf-8'), (b'content-length', b'44')]}, " +
            "{'type': 'http.response.body', 'body': b'hello from a real ASGI app served by PySharp'}]",
            Run("""
            import asgi_server, asyncio

            def make_scope(path, method="GET"):
                return {
                    "type": "http", "method": method, "path": path, "headers": [], "query_string": b"",
                    "server": ("127.0.0.1", 8000), "client": ("testclient", 123), "state": {},
                }

            async def run_request(path, method="GET"):
                scope = make_scope(path, method)
                msgs_in = [{"type": "http.request", "body": b"", "more_body": False}]
                msgs_out = []
                async def receive():
                    return msgs_in.pop(0)
                async def send(m):
                    msgs_out.append(m)
                await asgi_server.demo_app(scope, receive, send)
                return msgs_out

            print(asyncio.run(run_request("/")))
            """));

    [Fact]
    public void Demo_app_returns_a_path_parameter_and_a_real_404()
        => Assert.Equal("item_id=42\nnot found: /nope", Run("""
            import asgi_server, asyncio

            def make_scope(path, method="GET"):
                return {
                    "type": "http", "method": method, "path": path, "headers": [], "query_string": b"",
                    "server": ("127.0.0.1", 8000), "client": ("testclient", 123), "state": {},
                }

            async def run_request(path, method="GET"):
                scope = make_scope(path, method)
                msgs_in = [{"type": "http.request", "body": b"", "more_body": False}]
                msgs_out = []
                async def receive():
                    return msgs_in.pop(0)
                async def send(m):
                    msgs_out.append(m)
                await asgi_server.demo_app(scope, receive, send)
                body = b"".join(m["body"] for m in msgs_out if m["type"] == "http.response.body")
                return body.decode()

            async def main():
                print(await run_request("/items/42"))
                print(await run_request("/nope"))

            asyncio.run(main())
            """));

    [Fact]
    public void Demo_app_echoes_a_posted_body()
        => Assert.Equal("echo: hello world", Run("""
            import asgi_server, asyncio

            async def run_echo():
                scope = {
                    "type": "http", "method": "POST", "path": "/echo", "headers": [], "query_string": b"",
                    "server": ("127.0.0.1", 8000), "client": ("testclient", 123), "state": {},
                }
                msgs_in = [{"type": "http.request", "body": b"hello world", "more_body": False}]
                msgs_out = []
                async def receive():
                    return msgs_in.pop(0)
                async def send(m):
                    msgs_out.append(m)
                await asgi_server.demo_app(scope, receive, send)
                body = b"".join(m["body"] for m in msgs_out if m["type"] == "http.response.body")
                return body.decode()

            print(asyncio.run(run_echo()))
            """));

    // ---------------------------------------------------------- WebSocket (Phase 4.3)

    [Fact]
    public void Ws_accept_key_matches_RFC_6455s_own_worked_example()
        // The exact canonical Sec-WebSocket-Key/Sec-WebSocket-Accept pair from RFC 6455 §1.3 —
        // a real, independently-known-correct cross-check for the SHA1+base64 handshake
        // computation, not just an internally-consistent round trip.
        => Assert.Equal("s3pPLMBiTxaQ9kYGzzhZRbK+xOo=", Run("""
            import asgi_server
            print(asgi_server._ws_accept_key("dGhlIHNhbXBsZSBub25jZQ=="))
            """));

    [Fact]
    public void Build_ws_frame_produces_the_real_RFC_6455_bytes_for_a_short_text_frame()
        => Assert.Equal("[129, 5, 72, 101, 108, 108, 111]", Run("""
            import asgi_server
            frame = asgi_server._build_ws_frame(0x1, b"Hello")
            print(list(frame))
            """));

    [Fact]
    public void Build_ws_frame_uses_the_extended_16_bit_length_form_past_125_bytes()
        // Real RFC 6455: a payload of 126+ bytes uses the 7-bit length field's two reserved
        // values (126) plus a real big-endian 16-bit extended length, not the 7-bit length
        // itself. Found via this project's own real websocket server needing exactly this path
        // for any message a real client might reasonably send.
        => Assert.Equal("True\nTrue\nTrue", Run("""
            import asgi_server, struct
            payload = b"x" * 200
            frame = asgi_server._build_ws_frame(0x1, payload)
            print(frame[0] == 0x81)
            print(frame[1] == 126)
            print(struct.unpack("!H", frame[2:4])[0] == 200)
            """));

    [Fact]
    public void Demo_app_accepts_a_real_websocket_connection_and_echoes_text_and_bytes()
        // The real ASGI websocket scope/receive/send protocol, driven the same hand-built-triple
        // way as the http tests above (the actual RFC 6455 wire framing/handshake this same
        // demo_app runs over a real socket is verified separately, live, via curl-equivalent
        // manual probing against `asgi_server.serve` — FASTAPI_PLAN.md Phase 4.3). Order matters
        // here: demo_app sends `websocket.accept` immediately, *before* ever calling receive()
        // (unlike real starlette's own WebSocketRoute, which consumes the `websocket.connect`
        // event first) — the connect event is only picked up afterward, as the first iteration
        // of demo_app's own receive loop (and produces no echo, since it has no text/bytes).
        => Assert.Equal(
            "accept\nconnect\nrecv\necho: hi\nrecv\necho bytes: True\nrecv\ndisconnect",
            Run("""
                import asgi_server, asyncio

                async def run_ws():
                    scope = {
                        "type": "websocket", "path": "/ws", "headers": [], "query_string": b"",
                        "server": ("127.0.0.1", 8000), "client": ("testclient", 123), "state": {},
                    }
                    msgs_in = [
                        {"type": "websocket.connect"},
                        {"type": "websocket.receive", "text": "hi"},
                        {"type": "websocket.receive", "bytes": b"\x01\x02"},
                        {"type": "websocket.disconnect", "code": 1000},
                    ]
                    events = []
                    async def receive():
                        m = msgs_in.pop(0)
                        events.append("connect" if m["type"] == "websocket.connect" else "recv")
                        return m
                    async def send(m):
                        if m["type"] == "websocket.accept":
                            events.append("accept")
                        elif m["type"] == "websocket.send":
                            if m.get("text") is not None:
                                events.append(m["text"])
                            else:
                                events.append("echo bytes: " + str(m["bytes"] == b"\x01\x02"))
                    await asgi_server.demo_app(scope, receive, send)
                    events.append("disconnect")
                    return events

                print("\n".join(asyncio.run(run_ws())))
                """));

    [Fact]
    public void Demo_app_rejects_a_websocket_connection_to_an_unknown_path()
        => Assert.Equal("1008", Run("""
            import asgi_server, asyncio

            async def run_ws():
                scope = {
                    "type": "websocket", "path": "/nope", "headers": [], "query_string": b"",
                    "server": ("127.0.0.1", 8000), "client": ("testclient", 123), "state": {},
                }
                closes = []
                async def receive():
                    return {"type": "websocket.connect"}
                async def send(m):
                    if m["type"] == "websocket.close":
                        closes.append(m["code"])
                await asgi_server.demo_app(scope, receive, send)
                return closes[0]

            print(asyncio.run(run_ws()))
            """));

    [Fact]
    public void Real_websocket_handshake_and_echo_round_trip_over_an_actual_socket()
        // The live counterpart to the hand-built-triple tests above: a real TCP connection, a
        // real RFC 6455 HTTP Upgrade handshake, and real masked/unmasked wire framing — driven
        // by `asgi_server.serve(asgi_server.demo_app)` on its own background thread, exactly
        // like `AsyncServerTests`'s own plain-HTTP live-socket test. This is the C# side of the
        // same manual verification originally done via a standalone Python WebSocket client
        // probe (FASTAPI_PLAN.md Phase 4.3) — every value there matched by hand before this test
        // was written.
    {
        using var client = StartServerAndHandshake(out var stream, out _);

        WriteMaskedTextFrame(stream, "hello from the real test");
        var (opcode, payload) = ReadServerFrame(stream);
        Assert.Equal(0x1, opcode);
        Assert.Equal("echo: hello from the real test", Encoding.UTF8.GetString(payload));

        // a second message on the same connection, and a clean close
        WriteMaskedTextFrame(stream, "second message");
        (opcode, payload) = ReadServerFrame(stream);
        Assert.Equal("echo: second message", Encoding.UTF8.GetString(payload));

        WriteMaskedFrame(stream, 0x8, new byte[] { 0x03, 0xE8 }); // close, code 1000
    }

    [Fact]
    public void Real_websocket_reassembles_a_fragmented_message_and_answers_a_ping_sent_mid_fragment()
        // Real RFC 6455 fragmentation (opcode 0x0 continuation frames, FIN=0 on every frame but
        // the last of a message) — the same real client-side building blocks a browser sending a
        // sufficiently large message would use. A ping is sent *between* the two fragments to
        // confirm control frames interleaved mid-fragmentation (explicitly legal per RFC 6455,
        // and required so a fragmented message doesn't block keepalive pings) are still answered
        // immediately without disturbing the in-progress reassembly.
    {
        using var client = StartServerAndHandshake(out var stream, out _);

        WriteMaskedFrame(stream, 0x1, Encoding.UTF8.GetBytes("Hello, "), fin: false);
        WriteMaskedFrame(stream, 0x9, Encoding.UTF8.GetBytes("ping-mid-frag"));
        var (pingOpcode, pingPayload) = ReadServerFrame(stream);
        Assert.Equal(0xA, pingOpcode);
        Assert.Equal("ping-mid-frag", Encoding.UTF8.GetString(pingPayload));

        WriteMaskedFrame(stream, 0x0, Encoding.UTF8.GetBytes("World!"), fin: true);
        var (opcode, payload) = ReadServerFrame(stream);
        Assert.Equal(0x1, opcode);
        Assert.Equal("echo: Hello, World!", Encoding.UTF8.GetString(payload));
    }

    [Fact]
    public void Real_websocket_closing_handshake_echoes_the_code_then_actually_closes()
        // Real RFC 6455: a server that receives a close frame must echo one back (completing the
        // "closing handshake") before actually tearing down the TCP connection — not just record
        // the disconnect internally and stay silent.
    {
        using var client = StartServerAndHandshake(out var stream, out _);

        WriteMaskedFrame(stream, 0x8, new byte[] { 0x03, 0xE8 }); // close, code 1000
        var (opcode, payload) = ReadServerFrame(stream);
        Assert.Equal(0x8, opcode);
        Assert.Equal(1000, (payload[0] << 8) | payload[1]);

        // the server should now actually end the connection: a further read hits real EOF
        byte[] probe = new byte[1];
        int n = stream.Read(probe, 0, 1);
        Assert.Equal(0, n);
    }

    [Fact]
    public void Real_websocket_handshake_without_a_key_gets_a_real_400()
    {
        int port = FreeTcpPort();
        StartServer(port);
        using var client = ConnectWithRetry("127.0.0.1", port, TimeSpan.FromSeconds(20));
        using var stream = client.GetStream();
        stream.ReadTimeout = 5000;

        byte[] request = Encoding.ASCII.GetBytes(
            "GET /ws HTTP/1.1\r\n" +
            "Host: 127.0.0.1\r\n" +
            "Upgrade: websocket\r\n" +
            "Connection: Upgrade\r\n" +
            "Sec-WebSocket-Version: 13\r\n\r\n");
        stream.Write(request);

        string head = ReadHeadUntil(stream, "\r\n\r\n");
        Assert.Contains("400 Bad Request", head);
    }

    private static void StartServer(int port)
    {
        string script = $$"""
            import sys
            sys.path.insert(0, {{System.Text.Json.JsonSerializer.Serialize(SamplesDir)}})
            import asgi_server, asyncio
            asyncio.run(asgi_server.serve(asgi_server.demo_app, "127.0.0.1", {{port}}))
            """;
        var server = new Thread(() => new PyEngine(TextWriter.Null).Run(script))
        { IsBackground = true, Name = "pysharp-asgi-ws-server" };
        server.Start();
    }

    private static TcpClient StartServerAndHandshake(out NetworkStream stream, out string acceptValue)
    {
        int port = FreeTcpPort();
        StartServer(port);

        var client = ConnectWithRetry("127.0.0.1", port, TimeSpan.FromSeconds(20));
        stream = client.GetStream();
        stream.ReadTimeout = 5000;

        byte[] keyBytes = new byte[16];
        RandomNumberGenerator.Fill(keyBytes);
        string key = Convert.ToBase64String(keyBytes);
        string expectedAccept = Convert.ToBase64String(SHA1.HashData(
            Encoding.ASCII.GetBytes(key + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11")));

        byte[] request = Encoding.ASCII.GetBytes(
            "GET /ws HTTP/1.1\r\n" +
            "Host: 127.0.0.1\r\n" +
            "Upgrade: websocket\r\n" +
            "Connection: Upgrade\r\n" +
            "Sec-WebSocket-Key: " + key + "\r\n" +
            "Sec-WebSocket-Version: 13\r\n\r\n");
        stream.Write(request);

        string head = ReadHeadUntil(stream, "\r\n\r\n");
        if (!head.Contains("101 Switching Protocols"))
            throw new InvalidOperationException("handshake failed: " + head);
        string acceptLine = head.Split("\r\n").First(l => l.StartsWith("Sec-WebSocket-Accept", StringComparison.OrdinalIgnoreCase));
        acceptValue = acceptLine.Split(':', 2)[1].Trim();
        if (acceptValue != expectedAccept)
            throw new InvalidOperationException("Sec-WebSocket-Accept mismatch");
        return client;
    }

    private static int FreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static TcpClient ConnectWithRetry(string host, int port, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        Exception? last = null;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var client = new TcpClient();
                client.Connect(host, port);
                return client;
            }
            catch (SocketException ex)
            {
                last = ex;
                Thread.Sleep(50);
            }
        }
        throw new TimeoutException("server never accepted a connection", last);
    }

    private static string ReadHeadUntil(NetworkStream stream, string delimiter)
    {
        var sb = new StringBuilder();
        var buf = new byte[1];
        while (!sb.ToString().Contains(delimiter))
        {
            int n = stream.Read(buf, 0, 1);
            if (n == 0)
                throw new IOException("connection closed before the delimiter was seen");
            sb.Append((char)buf[0]);
        }
        return sb.ToString();
    }

    private static void WriteMaskedTextFrame(NetworkStream stream, string text)
        => WriteMaskedFrame(stream, 0x1, Encoding.UTF8.GetBytes(text));

    private static void WriteMaskedFrame(NetworkStream stream, byte opcode, byte[] payload, bool fin = true)
    {
        byte[] mask = new byte[4];
        RandomNumberGenerator.Fill(mask);
        var masked = new byte[payload.Length];
        for (int i = 0; i < payload.Length; i++)
            masked[i] = (byte)(payload[i] ^ mask[i % 4]);

        using var ms = new MemoryStream();
        ms.WriteByte((byte)((fin ? 0x80 : 0x00) | opcode));
        if (payload.Length < 126)
        {
            ms.WriteByte((byte)(0x80 | payload.Length));
        }
        else
        {
            ms.WriteByte(0x80 | 126);
            ms.WriteByte((byte)(payload.Length >> 8));
            ms.WriteByte((byte)(payload.Length & 0xFF));
        }
        ms.Write(mask);
        ms.Write(masked);
        var bytes = ms.ToArray();
        stream.Write(bytes, 0, bytes.Length);
    }

    private static (int opcode, byte[] payload) ReadServerFrame(NetworkStream stream)
    {
        byte[] head = ReadExact(stream, 2);
        int opcode = head[0] & 0x0F;
        int length = head[1] & 0x7F;
        if (length == 126)
        {
            byte[] ext = ReadExact(stream, 2);
            length = (ext[0] << 8) | ext[1];
        }
        byte[] payload = ReadExact(stream, length);
        return (opcode, payload);
    }

    private static byte[] ReadExact(NetworkStream stream, int n)
    {
        var buf = new byte[n];
        int offset = 0;
        while (offset < n)
        {
            int read = stream.Read(buf, offset, n - offset);
            if (read == 0)
                throw new IOException("connection closed before n bytes were read");
            offset += read;
        }
        return buf;
    }
}

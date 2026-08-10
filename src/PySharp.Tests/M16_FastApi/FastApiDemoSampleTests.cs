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
/// Verifies samples/fastapi_demo.py's real, unmodified FastAPI app without opening a real socket —
/// the same hand-built scope/receive/send triple technique AsgiServerSampleTests uses for the
/// dependency-free demo app, here driving a real fastapi/starlette/pydantic ASGI callable instead.
/// Every scenario here was also verified separately, manually, over a real HTTP/1.1 connection via
/// curl against `asgi_server.serve(app)` (FASTAPI_PLAN.md Phase 4.2) — GET/POST/PUT/DELETE, a typed
/// path parameter, query parameters, a real pydantic request body (including the real 422 validation
/// shape on a missing field), and a real `HTTPException` 404, all round-tripped correctly with zero
/// new bugs found.
///
/// [Collection("asyncio-run")]: every test here calls `asyncio.run`. See EventLoopThreadingTests's
/// own doc comment for why that must never run concurrently with another such test.
/// </summary>
[Collection("asyncio-run")]
public class FastApiDemoSampleTests : IClassFixture<FastApiInstallFixture>
{
    // bin/Debug/net10.0 -> Debug -> bin -> PySharp.Tests -> src -> repo root -> samples
    private static readonly string SamplesDir =
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "samples"));

    private readonly FastApiInstallFixture _fixture;

    public FastApiDemoSampleTests(FastApiInstallFixture fixture) => _fixture = fixture;

    private string Run(string body)
    {
        var writer = new StringWriter();
        var engine = new PyEngine(writer);
        engine.Importer.SearchPaths.Add(SamplesDir);
        engine.Importer.SearchPaths.Add(_fixture.SitePackages);
        engine.Run(body);
        return writer.ToString().TrimEnd('\n');
    }

    // Shared ASGI-driving helper: builds a scope/receive/send triple for one request, invokes
    // fastapi_demo's real `app`, and prints "<status> <json-decoded body>" — mirroring exactly how
    // the TestClient-based tests in FastApiSmokeTests report a response, just over the raw ASGI
    // protocol instead of through httpx.
    private const string Harness = """
        import fastapi_demo, asyncio, json

        async def request(method, path, body=b""):
            scope = {
                "type": "http", "method": method, "path": path, "headers": [
                    (b"content-type", b"application/json"),
                ], "query_string": b"",
                "server": ("127.0.0.1", 8000), "client": ("testclient", 123), "state": {},
            }
            if "?" in path:
                p, _, q = path.partition("?")
                scope["path"] = p
                scope["query_string"] = q.encode()
            msgs_in = [{"type": "http.request", "body": body, "more_body": False}]
            msgs_out = []
            async def receive():
                return msgs_in.pop(0) if msgs_in else {"type": "http.request", "body": b"", "more_body": False}
            async def send(m):
                msgs_out.append(m)
            await fastapi_demo.app(scope, receive, send)
            status = next(m["status"] for m in msgs_out if m["type"] == "http.response.start")
            resp_body = b"".join(m["body"] for m in msgs_out if m["type"] == "http.response.body")
            return status, json.loads(resp_body)

        """;

    [Fact]
    public void Sample_imports_as_a_module_without_starting_the_server()
        => Assert.Equal("True", Run("""
            import fastapi_demo
            print(callable(fastapi_demo.app))
            """));

    [Fact]
    public void Index_route_returns_the_real_greeting()
        => Assert.Equal("200 {'message': 'hello from a real FastAPI app served by PySharp'}", Run(
            Harness + """
            async def main():
                print(*await request("GET", "/"))
            asyncio.run(main())
            """));

    [Fact]
    public void Full_item_lifecycle_round_trips_through_the_real_app_put_get_delete_get_again()
        => Assert.Equal(
            "200 {'item_id': 1, 'item': {'name': 'widget', 'price': 9.5}}\n" +
            "200 {'name': 'widget', 'price': 9.5}\n" +
            "200 {'deleted': 1}\n" +
            "404 {'detail': 'Item not found'}",
            Run(Harness + """
                async def main():
                    print(*await request("PUT", "/items/1", b'{"name": "widget", "price": 9.5}'))
                    print(*await request("GET", "/items/1"))
                    print(*await request("DELETE", "/items/1"))
                    print(*await request("GET", "/items/1"))
                asyncio.run(main())
                """));

    [Fact]
    public void Post_with_a_valid_pydantic_body_computes_the_real_field()
        => Assert.Equal("200 {'name': 'widget', 'price': 9.5, 'total': 19.0}", Run(
            Harness + """
            async def main():
                print(*await request("POST", "/items", b'{"name": "widget", "price": 9.5}'))
            asyncio.run(main())
            """));

    [Fact]
    public void Post_with_a_missing_required_field_returns_the_real_422_validation_shape()
        => Assert.Equal(
            "422 {'detail': [{'loc': ['body', 'price'], 'msg': 'field required', " +
            "'type': 'value_error.missing'}]}",
            Run(Harness + """
                async def main():
                    print(*await request("POST", "/items", b'{"name": "bad"}'))
                asyncio.run(main())
                """));

    [Fact]
    public void Query_parameters_parse_with_real_defaults()
        => Assert.Equal(
            "200 {'q': 'hello', 'limit': 5}\n200 {'q': '', 'limit': 10}",
            Run(Harness + """
                async def main():
                    print(*await request("GET", "/search?q=hello&limit=5"))
                    print(*await request("GET", "/search"))
                asyncio.run(main())
                """));

    [Fact]
    public void Real_websocket_route_echoes_over_a_real_socket_through_the_real_starlette_stack()
        // The WebSocket counterpart to the HTTP live-socket milestone: a real
        // `@app.websocket("/ws")` route (real starlette's own `WebSocket`/`WebSocketDisconnect`,
        // not asgi_server.py's dependency-free demo_app) driven over a real TCP connection and
        // asgi_server.py's now-hardened RFC 6455 handshake/framing/closing-handshake (FASTAPI_
        // PLAN.md Phase 4.3.1/4.3.2). Verified by hand first: two sequential echoed messages, then
        // a client-initiated close correctly echoed back with the same code — zero bugs found on
        // the real fastapi/starlette WebSocket implementation's first run over the real socket
        // server. See FASTAPI_PLAN.md Phase 4.3.3.
    {
        int port = FreeTcpPort();
        string script = $$"""
            import sys
            sys.path.insert(0, {{System.Text.Json.JsonSerializer.Serialize(SamplesDir)}})
            sys.path.insert(0, {{System.Text.Json.JsonSerializer.Serialize(_fixture.SitePackages)}})
            import fastapi_demo, asyncio
            from asgi_server import serve
            asyncio.run(serve(fastapi_demo.app, "127.0.0.1", {{port}}))
            """;
        var server = new Thread(() => new PyEngine(TextWriter.Null).Run(script))
        { IsBackground = true, Name = "pysharp-fastapi-ws-server" };
        server.Start();

        using var client = ConnectWithRetry("127.0.0.1", port, TimeSpan.FromSeconds(20));
        using var stream = client.GetStream();
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
        Assert.Contains("101 Switching Protocols", head);
        string acceptLine = head.Split("\r\n").First(l => l.StartsWith("Sec-WebSocket-Accept", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(expectedAccept, acceptLine.Split(':', 2)[1].Trim());

        WriteMaskedTextFrame(stream, "hello real fastapi");
        var (opcode, payload) = ReadServerFrame(stream);
        Assert.Equal(0x1, opcode);
        Assert.Equal("echo: hello real fastapi", Encoding.UTF8.GetString(payload));

        WriteMaskedTextFrame(stream, "second message");
        (opcode, payload) = ReadServerFrame(stream);
        Assert.Equal("echo: second message", Encoding.UTF8.GetString(payload));

        WriteMaskedFrame(stream, 0x8, new byte[] { 0x03, 0xE8 }); // close, code 1000
        (opcode, payload) = ReadServerFrame(stream);
        Assert.Equal(0x8, opcode);
        Assert.Equal(1000, (payload[0] << 8) | payload[1]);
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

    private static void WriteMaskedFrame(NetworkStream stream, byte opcode, byte[] payload)
    {
        byte[] mask = new byte[4];
        RandomNumberGenerator.Fill(mask);
        var masked = new byte[payload.Length];
        for (int i = 0; i < payload.Length; i++)
            masked[i] = (byte)(payload[i] ^ mask[i % 4]);

        using var ms = new MemoryStream();
        ms.WriteByte((byte)(0x80 | opcode));
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

// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using System.Net;
using System.Net.Sockets;
using System.Text;
using PySharpLib;

namespace PySharp.Tests.M10_Async;

/// <summary>
/// Every test that drives a real asyncio event loop (<c>asyncio.run()</c>/<c>PyEventLoop.RunForever</c>)
/// must live in this collection: <see cref="PySharpLib.Runtime.PyEventLoop.Running"/> is a single
/// process-wide static (a coroutine's own background thread needs to see it too, so it can't be
/// <c>[ThreadStatic]</c>), so two `asyncio.run()` calls from different test classes running in
/// parallel — xUnit's default across collections — can stomp on each other's "current loop" and
/// deadlock. Also keeps live-socket tests from being starved by parallel CPU load.
/// </summary>
[CollectionDefinition("asyncio-run", DisableParallelization = true)]
public class AsyncioRunCollection { }

/// <summary>
/// Scenario 2 end-to-end: a real asynchronous HTTP server written in Python and run by
/// PySharp on the .NET-backed asyncio loop, answering an actual TCP request. Bounded to a
/// single request so the loop terminates on its own.
/// </summary>
[Collection("asyncio-run")]
public class AsyncServerTests
{
    [Fact]
    public void Async_server_answers_a_real_http_request()
    {
        int port = FreeTcpPort();
        string script = $$"""
            import asyncio, socket, json

            async def handle(loop, conn):
                await loop.sock_recv(conn, 4096)
                body = json.dumps({"engine": "PySharp", "async": True}).encode("utf-8")
                head = ("HTTP/1.1 200 OK\r\n"
                        "Content-Type: application/json\r\n"
                        "Content-Length: " + str(len(body)) + "\r\n"
                        "Connection: close\r\n\r\n")
                await loop.sock_sendall(conn, head.encode("utf-8") + body)
                conn.close()

            async def main():
                loop = asyncio.get_running_loop()
                srv = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
                srv.bind(("127.0.0.1", {{port}}))
                srv.listen(8)
                srv.setblocking(False)
                conn, addr = await loop.sock_accept(srv)
                conn.setblocking(False)
                await handle(loop, conn)
                srv.close()

            asyncio.run(main())
            """;

        Exception? serverError = null;
        var server = new Thread(() =>
        {
            try
            {
                new PyEngine(TextWriter.Null).Run(script);
            }
            catch (Exception ex)
            {
                serverError = ex;
            }
        })
        { IsBackground = true, Name = "pysharp-async-server" };
        server.Start();

        string response = GetWithRetry("127.0.0.1", port, timeout: TimeSpan.FromSeconds(20));

        Assert.Contains("200 OK", response);
        Assert.Contains("\"engine\": \"PySharp\"", response);
        Assert.Contains("\"async\": true", response);

        Assert.True(server.Join(TimeSpan.FromSeconds(15)), "server loop did not terminate after one request");
        Assert.Null(serverError);
    }

    private static int FreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static string GetWithRetry(string host, int port, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        Exception? last = null;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using var client = new TcpClient();
                client.Connect(host, port);
                using var stream = client.GetStream();
                stream.ReadTimeout = 5000;
                var request = Encoding.ASCII.GetBytes(
                    "GET / HTTP/1.1\r\nHost: localhost\r\nConnection: close\r\n\r\n");
                stream.Write(request, 0, request.Length);

                using var reader = new StreamReader(stream, Encoding.UTF8);
                return reader.ReadToEnd();
            }
            catch (SocketException ex)
            {
                last = ex;
                Thread.Sleep(100); // server not listening yet
            }
        }
        throw new Xunit.Sdk.XunitException($"could not reach async server on port {port}: {last?.Message}");
    }
}

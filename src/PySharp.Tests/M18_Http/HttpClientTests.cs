// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using System.Net;
using System.Net.Sockets;
using System.Text;
using PySharpLib;

namespace PySharp.Tests.M18_Http;

/// <summary>Real http.client (scenario 4): HTTPConnection/HTTPResponse driven over a real local
/// TCP socket — a hand-rolled minimal HTTP/1.1 server in this test file plays the "far end", so
/// these tests exercise this project's own request/response framing and parsing without any
/// external network dependency. The real integration target — a genuine, unmodified `requests`
/// package (→ `urllib3` → this module) making real HTTPS GET/POST/redirect/session/cookie round
/// trips against httpbin.org — was verified separately, by hand, live (see HTTP_PLAN.md); nothing
/// here re-tests `requests`/`urllib3` themselves, only the interpreter module they depend on.</summary>
public class HttpClientTests
{
    [Fact]
    public void Get_reads_a_real_status_reason_headers_and_body()
    {
        int port = FreeTcpPort();
        var server = StartOneShotServer(port, req =>
            "HTTP/1.1 200 OK\r\nContent-Type: text/plain\r\nContent-Length: 5\r\n\r\nhello");

        string result = Run($$"""
            import http.client
            conn = http.client.HTTPConnection("127.0.0.1", {{port}})
            conn.request("GET", "/")
            resp = conn.getresponse()
            print(resp.status, resp.reason)
            print(resp.getheader("Content-Type"))
            print(resp.read())
            conn.close()
            """);

        server.Join(TimeSpan.FromSeconds(5));
        Assert.Equal("200 OK\ntext/plain\nb'hello'", result);
    }

    [Fact]
    public void Post_sends_a_real_body_with_an_auto_content_length_header()
    {
        int port = FreeTcpPort();
        string? seenRequest = null;
        var server = StartOneShotServer(port, req =>
        {
            seenRequest = req;
            return "HTTP/1.1 200 OK\r\nContent-Length: 2\r\n\r\nok";
        });

        string result = Run($$"""
            import http.client
            conn = http.client.HTTPConnection("127.0.0.1", {{port}})
            conn.request("POST", "/items", body="hello world", headers={"Content-Type": "text/plain"})
            resp = conn.getresponse()
            print(resp.status, resp.read())
            conn.close()
            """);

        server.Join(TimeSpan.FromSeconds(5));
        Assert.Equal("200 b'ok'", result);
        Assert.NotNull(seenRequest);
        Assert.Contains("POST /items HTTP/1.1", seenRequest);
        Assert.Contains("Content-Length: 11", seenRequest);
        Assert.Contains("Content-Type: text/plain", seenRequest);
        Assert.EndsWith("hello world", seenRequest);
    }

    [Fact]
    public void Chunked_transfer_encoding_response_is_decoded_correctly()
    {
        int port = FreeTcpPort();
        var server = StartOneShotServer(port, req =>
            "HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\n\r\n" +
            "5\r\nhello\r\n6\r\n world\r\n0\r\n\r\n");

        string result = Run($$"""
            import http.client
            conn = http.client.HTTPConnection("127.0.0.1", {{port}})
            conn.request("GET", "/")
            resp = conn.getresponse()
            print(resp.read())
            conn.close()
            """);

        server.Join(TimeSpan.FromSeconds(5));
        Assert.Equal("b'hello world'", result);
    }

    [Fact]
    public void Putrequest_putheader_endheaders_send_low_level_sequence_works()
    {
        int port = FreeTcpPort();
        string? seenRequest = null;
        var server = StartOneShotServer(port, req =>
        {
            seenRequest = req;
            return "HTTP/1.1 204 No Content\r\n\r\n";
        });

        string result = Run($$"""
            import http.client
            conn = http.client.HTTPConnection("127.0.0.1", {{port}})
            conn.putrequest("PUT", "/x")
            conn.putheader("X-Custom", "value1")
            conn.putheader("Content-Length", "3")
            conn.endheaders()
            conn.send(b"abc")
            resp = conn.getresponse()
            print(resp.status)
            conn.close()
            """);

        server.Join(TimeSpan.FromSeconds(5));
        Assert.Equal("204", result);
        Assert.NotNull(seenRequest);
        Assert.Contains("PUT /x HTTP/1.1", seenRequest);
        Assert.Contains("X-Custom: value1", seenRequest);
        Assert.EndsWith("abc", seenRequest);
    }

    [Fact]
    public void Cursor_like_reuse_two_requests_on_the_same_keepalive_connection()
    {
        int port = FreeTcpPort();
        var server = StartServer(port, requestCount: 2, req =>
            "HTTP/1.1 200 OK\r\nContent-Length: 2\r\n\r\nok");

        string result = Run($$"""
            import http.client
            conn = http.client.HTTPConnection("127.0.0.1", {{port}})
            conn.request("GET", "/one")
            r1 = conn.getresponse()
            print(r1.status, r1.read())
            conn.request("GET", "/two")
            r2 = conn.getresponse()
            print(r2.status, r2.read())
            conn.close()
            """);

        server.Join(TimeSpan.FromSeconds(5));
        Assert.Equal("200 b'ok'\n200 b'ok'", result);
    }

    [Fact]
    public void Getresponse_on_a_connection_the_server_closed_immediately_raises_RemoteDisconnected()
    {
        int port = FreeTcpPort();
        var listener = new TcpListener(IPAddress.Loopback, port);
        listener.Start();
        var server = new Thread(() =>
        {
            using var client = listener.AcceptTcpClient();
            using var stream = client.GetStream();
            stream.ReadTimeout = 5000;
            // Fully drain the incoming request before closing — closing with unread inbound data
            // still buffered triggers a real TCP RST (ConnectionResetError, a different, equally
            // real exception) rather than the graceful FIN this test means to exercise.
            ReadOneRequest(stream);
            listener.Stop();
        })
        { IsBackground = true, Name = "http-close-immediately-server" };
        server.Start();

        string result = Run($$"""
            import http.client
            conn = http.client.HTTPConnection("127.0.0.1", {{port}})
            conn.request("GET", "/")
            try:
                conn.getresponse()
                print("no error")
            except http.client.RemoteDisconnected:
                print("RemoteDisconnected caught")
            conn.close()
            """);

        server.Join(TimeSpan.FromSeconds(5));
        Assert.Equal("RemoteDisconnected caught", result);
    }

    [Fact]
    public void Real_HTTPMessage_headers_support_get_and_case_insensitive_lookup()
    {
        int port = FreeTcpPort();
        var server = StartOneShotServer(port, req =>
            "HTTP/1.1 200 OK\r\nX-Foo: bar\r\nContent-Length: 0\r\n\r\n");

        string result = Run($$"""
            import http.client
            conn = http.client.HTTPConnection("127.0.0.1", {{port}})
            conn.request("GET", "/")
            resp = conn.getresponse()
            print(resp.headers.get("x-foo"))
            print(resp.headers["X-FOO"])
            print(("x-foo" in resp.headers))
            conn.close()
            """);

        server.Join(TimeSpan.FromSeconds(5));
        Assert.Equal("bar\nbar\nTrue", result);
    }

    // ---------------------------------------------------------------- helpers

    private static string Run(string body)
    {
        var writer = new StringWriter();
        var engine = new PyEngine(writer);
        engine.Run(body);
        return writer.ToString().TrimEnd('\n');
    }

    private static int FreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    /// <summary>Accepts exactly one connection, reads one full HTTP/1.1 request (headers + any
    /// Content-Length body), hands the raw request text to <paramref name="respond"/>, writes back
    /// whatever raw HTTP text it returns, then closes.</summary>
    private static Thread StartOneShotServer(int port, Func<string, string> respond)
        => StartServer(port, 1, respond);

    private static Thread StartServer(int port, int requestCount, Func<string, string> respond)
    {
        var listener = new TcpListener(IPAddress.Loopback, port);
        listener.Start();
        var thread = new Thread(() =>
        {
            using var client = listener.AcceptTcpClient();
            using var stream = client.GetStream();
            stream.ReadTimeout = 5000;
            for (int i = 0; i < requestCount; i++)
            {
                string request = ReadOneRequest(stream);
                byte[] response = Encoding.ASCII.GetBytes(respond(request));
                stream.Write(response);
            }
            listener.Stop();
        })
        { IsBackground = true, Name = $"http-test-server-{port}" };
        thread.Start();
        return thread;
    }

    private static string ReadOneRequest(NetworkStream stream)
    {
        var head = new List<byte>();
        var one = new byte[1];
        while (true)
        {
            int n = stream.Read(one, 0, 1);
            if (n == 0)
                break;
            head.Add(one[0]);
            if (head.Count >= 4
                && head[^4] == (byte)'\r' && head[^3] == (byte)'\n'
                && head[^2] == (byte)'\r' && head[^1] == (byte)'\n')
                break;
        }
        string headText = Encoding.ASCII.GetString(head.ToArray());
        int contentLength = 0;
        foreach (var line in headText.Split("\r\n"))
        {
            if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                contentLength = int.Parse(line.Split(':', 2)[1].Trim());
        }
        if (contentLength == 0)
            return headText;
        var body = new byte[contentLength];
        int read = 0;
        while (read < contentLength)
        {
            int n = stream.Read(body, read, contentLength - read);
            if (n == 0)
                break;
            read += n;
        }
        return headText + Encoding.ASCII.GetString(body, 0, read);
    }
}

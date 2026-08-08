// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

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
}

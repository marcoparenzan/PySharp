// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

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
}

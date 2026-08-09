// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using PySharpLib;

namespace PySharp.Tests.M16_FastApi;

/// <summary>
/// Phase 4.1 (FASTAPI_PLAN.md): `import fastapi` succeeds against the real, pinned
/// fastapi==0.99.1/starlette==0.27.0/pydantic==1.10.13 combination. Getting here required a real
/// deadlock fix (Importer.ImportAbsolute holding a lock across arbitrary module code execution — a
/// module-level generator expression during an import could spawn a real OS thread that blocked
/// forever on that same lock), a second real concurrency bug (GenericAliasModule's origin-mapping
/// dictionaries and its Generic-placeholder identity weren't safe under real parallel test/script
/// execution), a real `eval()` (expression evaluation, scoped the same way real CPython's own
/// eval() is), a real `typing.ForwardRef` (auto-wrapping a bare string type argument, `_evaluate`
/// resolving it via that real eval()), real `typing_extensions._AnnotatedAlias`, and several
/// smaller real stdlib gaps (`email.message.Message`, `binascii.Error`, `http.client.responses`,
/// `typing.TypeGuard`/`AsyncGenerator`, `Morsel._reserved` as a real class attribute,
/// `inspect.Parameter.replace`, `NoneType`/`issubclass` identity fixes for typing generics). Full
/// blow-by-blow in FASTAPI_PLAN.md Phase 4.
///
/// [Collection("asyncio-run")]: several tests here (the real anyio task group, and the real
/// TestClient — which itself drives `anyio.from_thread.start_blocking_portal`, a genuine background
/// thread running its own `asyncio.run`) touch PyEventLoop._running's process-wide bookkeeping, so —
/// like every other asyncio.run-touching test class — this must never run concurrently with another
/// one. See EventLoopThreadingTests's own doc comment for the full story.
/// </summary>
[Collection("asyncio-run")]
public class FastApiSmokeTests : IClassFixture<FastApiInstallFixture>
{
    private readonly FastApiInstallFixture _fixture;

    public FastApiSmokeTests(FastApiInstallFixture fixture) => _fixture = fixture;

    [Fact]
    public void Import_fastapi_succeeds()
    {
        var writer = new StringWriter();
        var engine = new PyEngine(writer);
        engine.Importer.SearchPaths.Add(_fixture.SitePackages);

        engine.Run("import fastapi\nprint(fastapi.__name__)");
        Assert.Equal("fastapi\n", writer.ToString());
    }

    [Fact]
    public void FastAPI_class_itself_is_importable()
    {
        var writer = new StringWriter();
        var engine = new PyEngine(writer);
        engine.Importer.SearchPaths.Add(_fixture.SitePackages);

        engine.Run("from fastapi import FastAPI\nprint(FastAPI.__name__)");
        Assert.Equal("FastAPI\n", writer.ToString());
    }

    [Fact]
    public void FastAPI_app_can_be_constructed()
    {
        // Regression for inspect.isroutine (starlette's routing.py's get_name(endpoint) calls it
        // while constructing every real Route — FastAPI() builds its default docs/openapi/redoc
        // routes at construction time, so this previously failed before the __init__ ran at all).
        var writer = new StringWriter();
        var engine = new PyEngine(writer);
        engine.Importer.SearchPaths.Add(_fixture.SitePackages);

        engine.Run("from fastapi import FastAPI\napp = FastAPI()\nprint(type(app).__name__)");
        Assert.Equal("FastAPI\n", writer.ToString());
    }

    [Fact]
    public void Real_routes_with_path_parameters_can_be_registered()
    {
        // Regression for inspect.Parameter's constructor not accepting name/kind as keywords — real
        // fastapi's get_typed_signature (dependencies/utils.py) calls
        // `inspect.Parameter(name=..., kind=..., default=..., annotation=...)` entirely by keyword
        // while building the typed signature for every route handler. The default docs/openapi/redoc
        // routes FastAPI() itself registers, plus our two, add up to 6.
        var writer = new StringWriter();
        var engine = new PyEngine(writer);
        engine.Importer.SearchPaths.Add(_fixture.SitePackages);

        engine.Run("""
            from fastapi import FastAPI

            app = FastAPI()

            @app.get("/")
            async def index():
                return {"message": "hello from real fastapi"}

            @app.get("/items/{item_id}")
            async def get_item(item_id: int):
                return {"item_id": item_id}

            print(len(app.routes))
            print(app.routes[-2].path)
            print(app.routes[-1].path)
            """);
        Assert.Equal("6\n/\n/items/{item_id}\n", writer.ToString());
    }

    [Fact]
    public void Real_anyio_task_group_spawns_a_worker_and_exits_cleanly()
        // Integration point for this round's asyncio.Task._must_cancel/_fut_waiter/cancel()/
        // get_coro(), BaseExceptionGroup/ExceptionGroup, Coroutine/Generator/Awaitable ABC
        // duck-typing, and loop.get_task_factory()/set_task_factory() fixes together: real
        // anyio.create_task_group() (an httpx transitive dependency) spawning a real worker task via
        // start_soon and tearing down without raising. Previously failed first with `AttributeError:
        // 'Task' object has no attribute '_must_cancel'`, then (after that fix) with
        // `NameError: name 'BaseExceptionGroup' is not defined`, then (after that fix) with a real
        // `TypeError: Expected worker() to return a coroutine, but the return value (...) is not a
        // coroutine object` from anyio's own isinstance(coro, Coroutine) check, then finally
        // `AttributeError: 'AbstractEventLoop' object has no attribute 'get_task_factory'`. See
        // FASTAPI_PLAN.md Phase 4.
    {
        var writer = new StringWriter();
        var engine = new PyEngine(writer);
        engine.Importer.SearchPaths.Add(_fixture.SitePackages);

        engine.Run("""
            import asyncio
            import anyio

            async def main():
                async with anyio.create_task_group() as tg:
                    async def worker():
                        print("worker running")
                    tg.start_soon(worker)
                print("task group exited")

            asyncio.run(main())
            """);
        Assert.Equal("worker running\ntask group exited\n", writer.ToString());
    }

    [Fact]
    public void Real_TestClient_get_request_round_trips_through_the_full_stack()
    {
        // The milestone this whole phase has been chasing: a real, unmodified
        // starlette.testclient.TestClient (backed by real httpx/httpcore/h11) driving a real
        // FastAPI app's ASGI callable end-to-end, through real anyio task groups/cancel scopes and
        // this project's own async runtime. Getting here required, in order: a thread-local
        // PyEventLoop._running fix (a genuine blocking-portal deadlock — see
        // EventLoopThreadingTests), real asyncio.Task._must_cancel/_fut_waiter/cancel()/get_coro()
        // (anyio's _deliver_cancellation), real PEP 654 BaseExceptionGroup/ExceptionGroup, real
        // Coroutine/Generator/Awaitable ABC duck-typing for isinstance(), loop.get_task_factory()/
        // set_task_factory(), real io.BytesIO position tracking (seek/read/truncate — starlette's
        // TestClient buffers the ASGI response body through one), the dict()/dict.update()
        // `keys()`-mapping-protocol (httpx's `dict(request.headers)` cookie compat shim), real
        // generator.send(value) (httpx's sync auth flow protocol), a generator's `return value`
        // actually reaching StopIteration.value, email.message.Message.get_content_charset, and
        // codecs.BOM_* constants (Response.json()'s charset auto-detection). Full blow-by-blow in
        // FASTAPI_PLAN.md Phase 4.
        var writer = new StringWriter();
        var engine = new PyEngine(writer);
        engine.Importer.SearchPaths.Add(_fixture.SitePackages);

        engine.Run("""
            from fastapi import FastAPI
            from starlette.testclient import TestClient

            app = FastAPI()

            @app.get("/")
            async def index():
                return {"message": "hello from real fastapi"}

            @app.get("/items/{item_id}")
            async def get_item(item_id: int):
                return {"item_id": item_id}

            client = TestClient(app)
            r = client.get("/")
            print(r.status_code, r.json())
            r2 = client.get("/items/42")
            print(r2.status_code, r2.json())
            """);
        Assert.Equal(
            "200 {'message': 'hello from real fastapi'}\n200 {'item_id': 42}\n",
            writer.ToString());
    }
}

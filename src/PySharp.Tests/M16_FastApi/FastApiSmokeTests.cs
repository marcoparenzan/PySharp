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
/// </summary>
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
}

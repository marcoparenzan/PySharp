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
        // Constructing FastAPI() itself is the next frontier (inspect.isroutine, found while
        // probing this — not yet fixed), deliberately not asserted here yet.
        var writer = new StringWriter();
        var engine = new PyEngine(writer);
        engine.Importer.SearchPaths.Add(_fixture.SitePackages);

        engine.Run("from fastapi import FastAPI\nprint(FastAPI.__name__)");
        Assert.Equal("FastAPI\n", writer.ToString());
    }
}

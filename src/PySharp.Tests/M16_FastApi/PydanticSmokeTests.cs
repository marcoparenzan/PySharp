// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using PySharpLib;
using PySharpLib.Runtime;

namespace PySharp.Tests.M16_FastApi;

/// <summary>
/// Tracks the gap between PySharp and real pydantic v1 (see FASTAPI_PLAN.md). Phase 0 first saw
/// this fail with a misleading `ImportError: cannot import name 'dataclasses' from 'pydantic'` —
/// looked like an import-resolution bug (a package submodule losing to the identically-named
/// stdlib module), but a minimal repro ([ImportTests.cs](../M5_Imports/ImportTests.cs)-style)
/// proved that path resolves correctly. The real cause: `pydantic/__init__.py`'s
/// `from . import dataclasses` fallback-imports `pydantic/dataclasses.py`, which itself needs
/// `typing_extensions` (a separate PyPI package, not installed) — but PySharp's `from pkg import
/// name` handling caught *any* failure of that fallback and replaced it with the generic
/// "cannot import name" message, discarding the real underlying error. Fixed in
/// `Interp.cs` (`IsMissingExactly`): only that generic message is used when the submodule itself
/// doesn't exist; any other failure (like this one) now propagates unchanged. This test now
/// documents the real next gap: `typing_extensions` isn't installed.
/// </summary>
public class PydanticSmokeTests : IClassFixture<PydanticInstallFixture>
{
    private readonly PydanticInstallFixture _fixture;

    public PydanticSmokeTests(PydanticInstallFixture fixture) => _fixture = fixture;

    [Fact]
    public void Import_fails_on_missing_pathlib_module()
    {
        // Progress so far (see FASTAPI_PLAN.md Phase 1 — long blow-by-blow list there): dozens of
        // real gaps closed in one long probe-driven session, including real generic-alias tracking
        // (typing.get_origin/get_args now work for real: List[int]/Dict[str,int]/Optional[int]/etc.
        // build an object with __origin__/__args__ instead of subscripting being a no-op), itertools,
        // collections.Counter/ChainMap, functools.partialmethod, and a real decimal.Decimal (backed
        // by System.Decimal — 128-bit, not arbitrary-precision, an explicit author-approved scope
        // tradeoff). Import now reaches all the way past typing_extensions.py and deep into
        // pydantic's own modules. Current frontier: `pathlib` — a whole separate module matching
        // ROADMAP.md's own scenario 8 ("File system API"), out of scope for this scenario's probe
        // loop; stopped here deliberately rather than starting a different scenario mid-probe.
        var writer = new StringWriter();
        var engine = new PyEngine(writer);
        engine.Importer.SearchPaths.Add(_fixture.SitePackages);

        var ex = Assert.Throws<PyRaise>(() => engine.Run("import pydantic"));

        Assert.Equal("ModuleNotFoundError", ex.Value.Class.Name);
    }
}

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
    public void Import_pydantic_succeeds()
    {
        // Phase 1 (see FASTAPI_PLAN.md) is done: `import pydantic` runs clean, no exception. Real,
        // not stubbed, cross-cutting stdlib gaps closed to get here (across several long
        // probe-driven sessions, every one with its own regression test, suite green throughout):
        // `pathlib.Path`, `complex`, `weakref`, `datetime`, `ipaddress` (incl. the real
        // `_BaseAddress`/`_BaseNetwork` base classes pydantic's IPvAny* types subclass), `re` (a
        // full System.Text.RegularExpressions-backed engine), `colorsys`, `pickle`, plus the
        // `typing`/`types` metaprogramming surface pydantic's own class machinery leans on
        // (`typing.Match`/`Pattern`, `types.new_class`/`resolve_bases`/`prepare_class`, the 3-arg
        // `type(name, bases, ns)` dynamic-class-creation form, `dataclasses.is_dataclass`). Two
        // real, generically important interpreter bugs were found and fixed along the way (not
        // pydantic-specific): a `from pkg import name` error-masking bug, and globals()/locals() at
        // true module top level silently leaking writes into the shared builtins module instead of
        // the actual currently-executing one. The `__mro_entries__` protocol was generalized (from
        // a generic-alias-only special case to the real CPython mechanism), letting `TypedDict` as
        // a base class work correctly.
        var writer = new StringWriter();
        var engine = new PyEngine(writer);
        engine.Importer.SearchPaths.Add(_fixture.SitePackages);

        engine.Run("import pydantic\nprint(pydantic.BaseModel)");
        Assert.Equal("<class 'BaseModel'>\n", writer.ToString());
    }

    [Fact]
    public void Defining_and_instantiating_a_BaseModel_subclass_is_the_current_frontier()
    {
        // Phase 2 (pydantic's own model machinery, not cross-cutting stdlib) starts here. Real
        // CPython pydantic relies on its `ModelMetaclass.__new__` running during the `class
        // User(BaseModel): ...` statement to build `__config__`/`__fields__`/validators — PySharp's
        // `ExecClassDef` already ignores custom metaclasses everywhere (see TypeConstructorMethods'
        // docs), so none of that setup runs and `BaseModel.__init__` (real pydantic source, not
        // stubbed) fails looking up `self.__config__`. This is a real architectural gap, not a
        // missing-name gap like everything fixed in Phase 1 — deliberately left for a dedicated
        // look (custom-metaclass support in ExecClassDef) rather than guessed at inline.
        var writer = new StringWriter();
        var engine = new PyEngine(writer);
        engine.Importer.SearchPaths.Add(_fixture.SitePackages);

        Assert.Throws<PyRaise>(() => engine.Run("""
            from pydantic import BaseModel
            class User(BaseModel):
                id: int
                name: str = "anon"
            User(id=1)
            """));
    }
}

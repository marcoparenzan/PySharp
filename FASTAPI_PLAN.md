# FastAPI support — a long, step-by-step plan

**Goal.** Run a **real FastAPI app**, unmodified from PyPI, on PySharp — the "key scenario" (2) in
ROADMAP.md. Same method as every other scenario: run the real package, fix what breaks, repeat. This
plan exists because the gap is large enough (stdlib + a real third-party validation library + an ASGI
stack) to need the same phased, checkbox-driven treatment as `NUMPY_PLAN.md` and `AIOMQTT_PLAN.md`.

**pydantic decision (asked of, and made by, the author on 2026-08-05): real pydantic v1.** Not a C#
shim, not a minimal ad-hoc validator. `pydantic==1.10.13` is a pure-Python wheel (no Rust
`pydantic-core`, unlike v2) and installs cleanly through the existing mini-pip. It will exercise real
stdlib depth (`abc`, `copy`, `typing` internals, `re`) that a shim would let us skip — that's the
point: it makes PySharp more Python, not just more FastAPI-shaped. Revisit only if v1's stdlib
surface turns out to be a wall as deep as v2's Rust core (unlikely — it's the same era of pydantic that
ran on plain CPython 3.6+).

---

## Key architecture decisions (read first)

- **Dependency chain, verified by real installs from PyPI** (`dotnet run --project src/PySharp --
  install <pkg>`, 2026-08-05): `fastapi` (pure wheel) → `starlette`, `pydantic`, `typing_extensions`,
  `annotated_doc` → `starlette` also pulls `anyio`. None of these rejected by the mini-pip's
  pure-wheel check — the wall people expect here (Rust) only exists for **pydantic v2**, which this
  plan avoids by pinning v1.
- **Stdlib gaps confirmed missing today** (grepped `StdlibModules.RegisterAll`): `re`, `datetime`,
  `inspect`, `abc`, `importlib`. Plus two `contextlib` gaps beyond what scenario 1b built:
  `asynccontextmanager` (async generator-backed context managers — `fastapi.concurrency` imports it
  directly) and `AbstractContextManager`.
- **First real error importing pydantic v1** (verified): `ImportError: cannot import name
  'dataclasses' from 'pydantic'`. Pydantic's `__init__.py` does `from . import dataclasses` (its own
  submodule, `pydantic/dataclasses.py`) — but PySharp's importer resolves it to the **stdlib**
  `dataclasses` module instead of the package-local submodule file, so the attribute never lands on
  the `pydantic` package object. This is a real bug in the import system's submodule-vs-stdlib name
  resolution, not a missing feature — first thing Phase 1 needs to fix, since nothing under
  `pydantic/` (or `starlette/`, which has the same `starlette/formparsers.py` shape) will import until
  it does. **Needs its own root-cause pass** (read `Importing/Importer.cs`'s resolution order) before
  guessing at a fix.
- **Phasing mirrors ROADMAP's own 2a–2e breakdown** (2a async/await and 2b asyncio are already done —
  scenario 2 itself, then extended by 1b's aiomqtt work). This plan covers 2c (stdlib), 2d (pydantic,
  decided above), and 2e (starlette + ASGI + fastapi).
- **Only Phase 1 is scoped in real detail below.** Phases 2–4 (pydantic internals, starlette/anyio,
  fastapi + an ASGI server) cannot be honestly catalogued yet — pydantic v1 alone is dozens of files
  deep. Per the project's own method, they get filled in with the same "run → next error → fix → test"
  loop once Phase 1 unblocks real probing, not guessed upfront. Treat their bullet points below as
  placeholders, not commitments.
- **Test milestone folder**: `src/PySharp.Tests/M16_FastApi/` (`M15_Aiomqtt` is scenario 1b).
- **No target sample script yet.** Unlike scenario 1b (which had `iothub_device_aiomqtt.py` from the
  start), scenario 2 doesn't get a "real FastAPI app" sample until pydantic + starlette actually
  import — writing one earlier would just be inventing a script to fail against, which isn't the
  method. The existing `samples/http_api.py`/`async_api.py` stay as the pre-FastAPI walking skeleton.

## Execution rules (how to run this plan)

1. **One step at a time**, each a small change with a visible deliverable (a passing test).
2. **Ground every phase in a real run.** Re-run the relevant probe against the real package after
   each fix and record the *next* error before planning further — do not guess ahead of what's been
   observed (see AIOMQTT_PLAN.md's own experience: several real gaps only showed up this way, and
   some predicted gaps never materialized at all).
3. **Keep the suite green** after every step (`dotnet test`). Never leave it red between steps.
4. MIT header on every new file; match surrounding code style.
5. Commit per step or per small group; bump the package version once per phase and re-pack to the
   local NuGet feed when a phase completes ([[nuget-local-feed]]).
6. Update this file: tick the box, add a one-line note if a decision was made or a prediction here
   turned out wrong.
7. When the scenario lands, update ROADMAP.md (scenario 2 status + gap analysis) and
   RELEASE_NOTES.md, same as every other scenario.

---

## Phase 0 — Groundwork ✅ (done while writing this plan, 2026-08-05)

- [x] 0.1 Confirmed `fastapi`/`starlette`/`anyio`/`pydantic==1.10.13` all install cleanly from real
  PyPI (pure wheels); pydantic v2's default install also "succeeds" at the wheel level but its
  `pydantic_core` dependency is the Rust wall the author chose to avoid by pinning v1.
- [x] 0.2 Confirmed current stdlib gaps by reading `StdlibModules.RegisterAll`: no `re`, `datetime`,
  `inspect`, `abc`, `importlib`.
- [x] 0.3 Confirmed the first real error importing pydantic v1: the `from . import dataclasses`
  submodule-resolution bug described above.
- [ ] 0.4 Add `PydanticInstallFixture` in `src/PySharp.Tests/M16_FastApi/` mirroring
  `AiomqttInstallFixture` ([AiomqttInstallFixture.cs](src/PySharp.Tests/M15_Aiomqtt/AiomqttInstallFixture.cs)):
  installs `pydantic==1.10.13` (and, once needed, `starlette`/`fastapi`/`typing_extensions`/
  `annotated_doc`) into a temp site-packages dir. One smoke test documenting the current import
  failure (flip once fixed).

## Phase 1 — cross-cutting stdlib + the import-error-masking bug

- [x] 1.1 **The "submodule-vs-stdlib" bug wasn't real — root-caused to something else, and fixed.**
  A minimal repro (`pkg/__init__.py` doing `from . import dataclasses` + a real `pkg/dataclasses.py`)
  worked correctly, proving `Importer.cs`'s resolution order was never the problem. The real cause,
  found by temporarily swapping pydantic's `from . import dataclasses` for a plain
  `import pydantic.dataclasses` in a scratch install (plain `import` doesn't have the masking
  try/catch `from...import` does, so it let the *real* exception through): `pydantic/dataclasses.py`
  itself needs `typing_extensions` (a separate, not-yet-installed PyPI package) — nothing to do with
  submodule resolution at all. The actually-real bug: `Interp.cs`'s `FromImportStmt` handling caught
  **any** failure of the `from pkg import name` submodule-import fallback and replaced it with a
  generic `ImportError: cannot import name`, discarding the real underlying exception — so a missing
  transitive dependency three files deep was misreported as if the submodule itself didn't exist.
  Fixed with `IsMissingExactly` (`Interp.cs`): the generic message now only fires when the submodule
  genuinely doesn't exist (`ModuleNotFoundError` whose message is exactly
  `"No module named '<the submodule>'"`); anything else propagates unchanged. Regression tests in
  `M5_Imports/ImportTests.cs`: `From_package_import_submodule_that_does_not_exist_raises_ImportError`
  (old behavior preserved) and `From_package_import_submodule_that_fails_for_another_reason_...`
  (new behavior: the real nested error survives). **Lesson for future phases**: verify a hypothesis
  with a minimal repro before trusting a surface-level error message — this one pointed at entirely
  the wrong subsystem.
- [x] 1.1b Installed `typing_extensions` in `PydanticInstallFixture`; re-probed. Next real gap:
  `ModuleNotFoundError: No module named 'abc'`.
- [x] 1.3 `abc`: `ABC`/`ABCMeta` as plain subclassable classes, `abstractmethod` as a passthrough
  decorator (`AbcModule.cs`). No real enforcement (can't-instantiate-with-unimplemented-abstracts) —
  nothing has needed it yet. **Flagging a real risk for Phase 2**: pydantic's `main.py` defines
  `class ModelMetaclass(ABCMeta)` and presumably `class BaseModel(metaclass=ModelMetaclass)`; PySharp
  already ignores `metaclass=...` entirely (v1 scope, pre-existing). If `ModelMetaclass.__new__` is
  where pydantic actually builds `__fields__` from class annotations (likely), `BaseModel` subclasses
  may not get populated fields at all under PySharp today. Not addressed now — Phase 2's first real
  test (`class M(BaseModel): x: int`) will show immediately whether this is real.
- [x] (unplanned) `builtins`: `import builtins` now returns the same module every name already
  falls back to (`Importer.BuiltinsModule`, exposed via `StdlibModules.cs`) — no new module needed.
- [x] (unplanned) `collections.abc`: plain placeholder ABCs (`Callable`/`Mapping`/`Sequence`/…),
  mirroring `typing`'s existing stub philosophy — added to `CollectionsModule.cs`. No isinstance
  duck-typing (`isinstance({}, Mapping)` is not `True` here) unless something needs it.
- [x] (unplanned, partial) `inspect`: only `cleandoc` (ported verbatim from CPython) — the one name
  actually demanded so far. `signature`/`Signature`/`Parameter` (item 1.6 as originally planned,
  FastAPI's actual need) deliberately **not** added yet — nothing has called for it in a real run;
  add it the same probe-driven way once something does, likely in Phase 4.
- [x] (unplanned) `keyword`: `iskeyword`/`issoftkeyword`/`kwlist`/`softkwlist` (`MiscModules.cs`).
- [x] (unplanned) `operator`: arithmetic/comparison/bitwise ops as thin wrappers over `Interp.BinaryOp`/
  `UnaryOp` (so dunder-dispatch semantics match exactly), plus `itemgetter`/`attrgetter`/`methodcaller`
  (`OperatorModule.cs`).
- [x] (unplanned) `typing.ForwardRef`: added to the existing placeholder list in `CreateTyping`.
- [ ] 1.2 `importlib`: not yet reached by a real probe.
- [ ] 1.4 `re`: not yet reached by a real probe.
- [ ] 1.5 `datetime`: not yet reached by a real probe.
- [ ] 1.6 `inspect.signature`/`Signature`/`Parameter`: not yet reached by a real probe (see above).
- [ ] 1.7 `contextlib.asynccontextmanager`/`AbstractContextManager`: not yet reached by a real probe.
- [x] 1.8 Tests added alongside each fix above (not batched at the end): regression tests for the
  import-masking fix in `M5_Imports/ImportTests.cs`; the pydantic smoke test in
  `M16_FastApi/PydanticSmokeTests.cs` updated at each round to assert the *current* frontier error
  (see its own doc comment for the blow-by-blow). No dedicated per-module unit tests for
  abc/builtins/collections.abc/inspect/keyword/operator yet — they're only exercised transitively
  through the pydantic import chain so far; add direct tests if/when a scenario calls them directly
  enough to warrant it.
- [x] 1.9 **Long probe-driven chain, 2026-08-05/06, one real gap fixed per round** — each found by
  re-running the same `import pydantic` probe after every fix and reading the *next* error, exactly
  per the execution rules. In order:
  - `AttributeError: 'type' object has no attribute '__slots__'` — not a general `__slots__` gap:
    user-defined classes declaring `__slots__ = (...)` already just worked (it's ordinary
    class-attribute assignment, verified with a standalone repro). The real cause was narrower: our
    `typing.ForwardRef` *stub* never set `__slots__`, and `typing_extensions.py` checks
    `"__forward_is_class__" in typing.ForwardRef.__slots__` at module level. Fixed by giving that one
    stub class a `__slots__` tuple matching real CPython's.
  - `AttributeError: 'PySuper' object has no attribute '__setattr__'` — `super().__setattr__(...)`
    (a common defensive pattern) had no fallback when nothing in the MRO overrides it. Real bug,
    generically useful: added `object`-default fallbacks for `__setattr__`/`__delattr__`/`__init__`/
    `__new__` on `PySuper` attribute resolution (`Interp.cs`).
  - `AttributeError: 'module' object has no attribute '_Final'` then a cluster of further
    `typing._xxx` internals (`_SpecialForm`, `_GenericAlias`, `_BaseGenericAlias`,
    `_SpecialGenericAlias`, `_AnnotatedAlias`, `_UnionGenericAlias`, `_ConcatenateGenericAlias`,
    `_ProtocolMeta`, `_TypedDictMeta`) — batch-added as plain placeholders (same shape as the ~70
    already-stubbed `typing` names), *not* the function-shaped internals, which stayed probe-driven.
  - `_tp_cache` (decorator, passthrough), `final`/`runtime_checkable`/`no_type_check` (decorators,
    passthrough, added together since they're always-identity companions to `overload`),
    `_overload_dummy` (raises `NotImplementedError`, ported from CPython).
  - `AttributeError: 'PySuper' object has no attribute '__init__'` — same fallback mechanism as
    `__setattr__` above, added `__init__`/`__new__` too.
  - `AttributeError: 'module' object has no attribute 'AbstractContextManager'` — implemented for
    real (not a stub): `contextlib.AbstractContextManager`/`AbstractAsyncContextManager`, base
    classes to subclass (`__enter__` returns self, `__exit__` a no-op that doesn't suppress; the
    async pair return an already-resolved `Future` so `await`ing them works without a running
    reactor). Also added `typing.ContextManager`/`AsyncContextManager` placeholders.
  - `AttributeError: 'module' object has no attribute 'EXCLUDED_ATTRIBUTES'` — a real CPython data
    constant (the dunders `Protocol` excludes from structural checks); added verbatim.
  - `TypeError: unsupported operand type(s) for |: 'frozenset' and 'set'` — a **real, generically
    useful bug**: `Interp.BinaryOp`'s `|`/`&`/`-`/`^` only handled same-type set/frozenset pairs.
    Fixed to accept any combination (`SetItems` helper already existed for `<`/`<=` subset
    comparisons but wasn't reused here — now it is), result type follows the left operand like
    CPython. Also fixed the `|=` augmented-assignment path the same way. Regression tests:
    `M3_Evaluator/CollectionTests.cs`.
  - `AttributeError: 'type' object has no attribute '__doc__'` — classes never exposed `__doc__` at
    all (confirmed function docstrings have the same latent gap — nothing captures a leading
    string-literal statement as a docstring anywhere yet). Scoped narrowly: added a `None` fallback
    for class `__doc__` (matches what `SomeClass.__doc__ or default` patterns expect); real
    docstring-capture is a separate, not-yet-needed feature.
  - `AttributeError: 'module' object has no attribute 'SupportsComplex'` — three more `typing.
    Supports*` placeholders (`SupportsComplex`/`SupportsBytes`/`SupportsIndex`).
  - `AttributeError: 'module' object has no attribute 'signature'` — implemented **`inspect.
    signature`/`Signature`/`Parameter` for real** (`InspectModule.cs`), the FastAPI-shaped need
    ROADMAP.md already flagged, now built because pydantic's dependency chain (not FastAPI itself
    yet) actually called for it: parameters (positional/`*args`/keyword-only/`**kwargs`) with
    `.default`/`.annotation`/`.kind`, `Parameter.empty` sentinel, bound-method `self`-dropping. Built
    directly on `PyFunction.Params`/`.Defaults`, reusing the same annotation-evaluation approach
    `__annotations__` already uses. This in turn needed `typing._type_check` to exist as a **real
    Python function** (not a `PyBuiltinFunction`) so its signature could be introspected — added by
    parsing and running a small literal Python snippet into the `typing` module at creation time
    (`CreateTyping` now takes `Interp`), the first stdlib module to use this technique. Tests:
    `M6_Stdlib/StdlibTests.cs` (`InspectTests`, 4 tests).
  - `AttributeError: 'function' object has no attribute '__new__'` — `type.__new__(metaclass, name,
    bases, namespace)`, the dynamic-class-creation path (used by `TypedDict` machinery). Implemented
    as a real feature, not a stub: builds a genuine `PyClass` from the 3 arguments (metaclass
    accepted but not used as one, consistent with metaclasses being ignored everywhere else). Lives
    in `TypeMethods.cs` as `TypeConstructorMethods`, wired through the existing "unbound method on a
    builtin type" dispatch (`str.upper`-style) that already handled `str`/`list`/`dict`/etc.
  - `AttributeError: 'module' object has no attribute 'get_origin'` — paused here, asked the author
    how to handle it (stub vs. real tracking), got a clear answer: **build the real thing.**
  - [x] **`typing.get_origin`/`get_args` implemented for real** (`GenericAliasModule.cs`, new file).
    `PyClass[index]` (`Interp.GetItem`'s `case PyClass:`) now builds a real alias object carrying
    `__origin__`/`__args__` instead of the old no-op (`return obj` unchanged) — the actual structural
    fix, not just adding the two functions. Known typing containers map to their real runtime
    counterpart (`List`→`list`, `Dict`→`dict`, `Tuple`→`tuple`, `Set`→`set`, `FrozenSet`→`frozenset`,
    `Type`→`type`, via `interp.BuiltinsModule`, so `get_origin(List[int]) is list` matches CPython
    exactly); unmapped names (`Union`, arbitrary user `Generic[T]` subclasses) default to
    origin = the class itself, which is also correct. `Optional[X]` is special-cased to produce
    `Union` as origin and `(X, NoneType)` as args, matching real semantics
    (`Optional[X] is sugar for Union[X, None]`) — added `types.NoneType` for this.
    `typing._GenericAlias` (previously a bare placeholder) now *is* the real alias class, so
    `isinstance(List[int], typing._GenericAlias)` is correct too.
    - **Found and fixed a real regression this introduced**: `class Foo(Generic[T]):` broke —
      `TypeError: class base must be a class, got _GenericAlias`, because `Generic[T]` used as a
      base class now returns a real alias instead of a no-op-return-self. Fixed in `ExecClassDef`:
      an alias used as a base class unwraps to its `__origin__`, mirroring CPython's
      `__mro_entries__` protocol (simplified — the alias itself was never meant to end up in the
      MRO). This is exactly the kind of regression risk a broad `GetItem` change carries; caught by
      running the full suite after the change, not just the pydantic probe.
    - Regression tests: `M6_Stdlib` (none added directly for aliases yet — covered transitively by
      the pydantic import chain continuing to progress; add direct tests if a later scenario
      exercises `get_origin`/`get_args` more directly).
  - Continued the probe loop with the real tracking in place, several more real gaps:
    `typing._SpecialForm` — not just missing, structurally wrong as a bare placeholder: real code
    (`_ExtensionsSpecialForm(typing._SpecialForm, _root=True)`) subclasses it for real and
    instantiates it via `@_SpecialForm def foo(self, parameters): ...`, which needs actual
    `__init__`/`__getitem__`/`__call__` behavior. Implemented as a **real Python class** (like
    `_type_check` before it), parsed and run into the module — ported from CPython's actual
    `_SpecialForm` implementation.
  - `typing.Unpack`, `typing.TypeVarTuple` (callable like `TypeVar`/`ParamSpec`), `typing.Annotated`
    (plain placeholder — subscripting already produces a real alias via the new machinery),
    `typing.dataclass_transform` (a decorator *factory* — the call returns an identity decorator),
    `typing._prohibited` (a real CPython constant: names `NamedTuple` refuses as field names).
  - `ImportError: cannot import name 'ChainMap' from 'collections'` — **past `typing_extensions.py`
    entirely and into pydantic's own `class_validators.py`** at this point. Added real
    `collections.Counter` (counts from an iterable/mapping — simplification: missing keys raise
    `KeyError` like a plain dict rather than returning 0, no `.most_common()` yet, both left for a
    real need to surface) and `collections.ChainMap` (simplification: a merged snapshot, first-map-
    wins on collisions, rather than a live view over independently-mutable maps — correct for the
    observed use, `ChainMap(*[cls.__dict__ for cls in mro])` used for read-only lookup).
  - `ImportError: cannot import name 'partialmethod' from 'functools'` — added as a real descriptor
    class (same func/args/keywords shape as the existing `partial`, plus `__get__` for the bound-
    method-with-preset-args behavior), even though pydantic only `isinstance`-checks against it —
    worth doing properly since it's a general-purpose primitive, not pydantic-specific.
  - `ModuleNotFoundError: No module named 'itertools'` — new module (`ItertoolsModule.cs`):
    `chain`, `islice` (both 2-arg `(stop)` and 4-arg `(start, stop, step)` forms), `zip_longest`
    (with `fillvalue`). Tests: `M6_Stdlib/StdlibTests.cs` (`ItertoolsAndCollectionsTests`, 5 tests
    covering itertools + the Counter/ChainMap additions above).
  - `ModuleNotFoundError: No module named 'decimal'` — paused here, asked the author how to back
    it; answer: **build it on `System.Decimal`** (128-bit fixed-point — a deliberate, explicit scope
    tradeoff vs. real CPython's arbitrary-precision `decimal.Decimal`, sufficient for the money-
    amount-shaped scenarios that actually reach for `Decimal`).
  - [x] **`decimal.Decimal` implemented for real** (`DecimalModule.cs`, new file). A `PyInstance`
    wrapping a boxed `System.Decimal`; all arithmetic (`+ - * / // %`, unary `- + abs()`) and
    comparison (`== != < <= > >=`) dunders, both directions (`__add__`/`__radd__` etc., so
    `Decimal + int` and `int + Decimal` both work) — implemented as plain dunders needing **zero
    interpreter changes**, since `Interp.BinaryOp` already dispatches generically to `PyInstance`
    dunders (the same mechanism every user-defined class's operator overloading already rides).
    `__str__`/`__repr__`/`__bool__`/`__float__`/`__int__`/`__hash__`, plus `is_finite`/`is_zero`/
    `as_tuple`/`quantize`. Real exception hierarchy: `DecimalException(ArithmeticError)`,
    `InvalidOperation`/`DivisionByZero`/`Overflow` subclasses (`DivisionByZero` also derives from
    `ZeroDivisionError`, matching CPython, so existing `except ZeroDivisionError` code still catches
    it). Construction from string/int/bool/float/another Decimal; invalid strings raise
    `InvalidOperation`, matching real CPython. Manually verified against a hand-written probe script
    covering all of the above before writing tests (every result matched real Python's), then 7
    tests added: `M6_Stdlib/StdlibTests.cs` (`DecimalTests`).
  - `ModuleNotFoundError: No module named 'pathlib'` — paused here (see below), author said
    continue: pydantic actually needs `Path` as a real, validated field type (`Path(v)` in
    `validators.py`), not just an importable name, so it was worth building for real rather than
    stubbed.
  - [x] **`pathlib.Path`/`PurePath` implemented for real** (`PathlibModule.cs`, new file), backed by
    `System.IO.Path` for the pure string operations (join, `.name`/`.stem`/`.suffix`/`.parent`/
    `.parts`, `__truediv__`) and `System.IO.File`/`Directory` for the real filesystem ones
    (`.exists()`/`.is_file()`/`.is_dir()`/`.mkdir()`/`.open()`/`.read_text()`/`.write_text()`).
    `Path` subclasses `PurePath` subclasses the new `os.PathLike` (a real base, not a bare
    placeholder — `isinstance(Path(...), os.PathLike)` is correct). v1 scope: no globbing, no
    symlink-resolution edge cases. This also partially closes ROADMAP.md scenario 8's `pathlib`
    item — noted there when scenario 8 starts for real. Verified manually against a probe script
    (every result matched real Python) before writing 6 tests: `M6_Stdlib/StdlibTests.cs`
    (`PathlibTests`).
  - `ImportError: cannot import name '_eval_type' from 'typing'` — real CPython resolves
    `ForwardRef`/string annotations against namespaces here; PySharp evaluates annotation
    expressions eagerly already (see the class-annotation fix below), so by the time anything
    reaches `_eval_type` it's already a real object — implemented as a passthrough (correct for
    that case; a genuine string-sourced `ForwardRef` would need `eval()`, which PySharp's own
    `eval()` doesn't support yet and nothing has hit that path in a real run).
  - `ImportError: cannot import name 'get_type_hints' from 'typing'` — implemented for real (not a
    stub): merges `__annotations__` across the whole MRO (base classes first, subclasses override),
    matching CPython, as a parsed-Python-source function (same technique as `_type_check`/
    `_SpecialForm`). Manual testing surfaced a **real, separately-important bug** it depends on:
  - **Found and fixed: class-body `x: int` annotations were never evaluated.** Only the *name* was
    recorded in `__annotations__`, with a bare `None` placeholder — unlike function parameter
    annotations, which already evaluated the real expression. This directly breaks `get_type_hints`
    on classes, and matters enormously for Phase 2: a pydantic `BaseModel` field is exactly a
    class-body annotated assignment (`class M(BaseModel): x: int`). Fixed in `Interp.cs`
    (`AnnAssignStmt` case): evaluates the annotation the same best-effort way function parameters
    already do (unresolvable forward refs fall back to `None` rather than failing the assignment).
    Tests: `M4_Functions/IntrospectionTests.cs` (class-annotation evaluation + `get_type_hints`
    merging across the MRO).
  - `os.PathLike` needed adding too (`OsModule.cs`) — `pathlib.PurePath` subclasses it for real.
  - `ImportError: cannot import name 'GenericAlias' from 'types'` — added, and aliased to the
    *same* class `GenericAliasModule.GenericAliasClass` already backs `List[int]`/etc. (not a
    separate placeholder), so `isinstance(tp, (typing._GenericAlias, types.GenericAlias, ...))` —
    a real pattern pydantic's own `typing.py` uses — is correct. `types.UnionType` and a few
    companion names (`MethodType`, `CodeType`, `FrameType`, …) added alongside, matching the
    already-established "batch cheap identity placeholders" approach.
  - `AttributeError: 'NoneType' object has no attribute '__class__'` (from the real `NoneType =
    None.__class__` idiom) — **found a real, generically important gap**: `.__class__` was only
    handled for `PyInstance` objects; every other value (`None`, `str`, `int`, `list`, …) had no
    `.__class__` at all. Added a universal fallback in `TypeMethods.TryGetBuiltinAttr` reusing the
    same `TypeNamePseudoClass` logic the `type()` builtin already uses, so `x.__class__` now works
    the same as `type(x)` for any value.
  - `ModuleNotFoundError: No module named 'weakref'` — new module (`WeakrefModule.cs`). v1 scope is
    explicitly "not actually weak": PySharp has no exposed GC hooks to make entries disappear when
    their referent is collected, and .NET's GC semantics differ enough from CPython's refcounting
    that faithfully replicating eviction timing isn't worth chasing. `WeakKeyDictionary`/
    `WeakValueDictionary`/`WeakSet` are real dicts/sets that just never evict (correct for every
    normal operation, the only difference is no early memory reclaim); `ref(obj)` is callable and
    always returns `obj` (real `weakref.ref` returns `None` once the referent is gone — not
    replicated). Tests: `M6_Stdlib/StdlibTests.cs` (`WeakrefTests`).
  - `ImportError: cannot import name 'CodeType' from 'types'` — see the `types.GenericAlias` bullet
    above (added together).
  - `NameError: name 'complex' is not defined` — **`complex` implemented for real** (new file
    `Builtins/ComplexType.cs`), backed by `System.Numerics.Complex`, same dunder-dispatch approach
    as `decimal.Decimal` (arithmetic/comparison ride `Interp.BinaryOp`'s existing generic
    `PyInstance` dunder path — no interpreter changes needed for the type itself, just registering
    `d["complex"] = ComplexType.ComplexClass` in `Builtins.cs` next to `int`/`float`/`bool`).
    `.real`/`.imag`/`.conjugate()`, `repr`/`str` matching CPython's `(a+bj)` formatting, mixes with
    `int`/`float`/`bool` on either side. Verified manually against known values (`(3+4j)*(1+2j) =
    (-5+10j)`, `abs(3+4j) = 5.0`) before writing 12 tests: `M3_Evaluator/ArithmeticTests.cs`
    (`ComplexTests`).
  - `TypeError: unsupported operand type(s) for |: ...` **did not recur** — the set/frozenset fix
    from the previous round held up through everything above; noted only because it's exactly the
    kind of thing worth confirming stayed fixed as more real code exercises it.
  - `ImportError: cannot import name '_AnnotatedAlias' from 'typing_extensions'` — looked like a
    simple missing-name gap, **was actually a serious, previously-invisible interpreter bug** found
    via careful bisection (adding temporary diagnostic prints *inside* the real `typing_extensions.py`
    copy, comparing `globals()` state inside the module against `hasattr()` from outside — see the
    method note below). Root cause: **`globals()`/`locals()` called at true module top level (no
    active function call on the stack) fell back to whichever module happened to be the enclosing
    C# closure's own `module` variable in `BuiltinsFactory.Create()` — the *builtins* module —
    instead of the actual currently-executing module.** `typing_extensions.py`'s own
    `globals().update({...})` (a completely standard, common Python idiom for bulk-copying names)
    was silently writing into the shared builtins namespace instead of its own, so `_AnnotatedAlias`
    "existed" as a bare name inside `typing_extensions.py` (found via the builtins fallback in name
    resolution) but was never actually an *attribute* of the `typing_extensions` module object —
    exactly why `from typing_extensions import _AnnotatedAlias` failed while everything else looked
    fine. This is about as high-value and dangerous a bug as anything found this session: any
    top-level `globals()`/`locals()` call in *any* module, ever, was affected — not pydantic-
    specific at all. Fixed with a new `Interp.InnermostFrame` (the module frame itself, which the
    existing `CurrentFrame` deliberately skips for other reasons — its own doc comment already said
    it's "used by super()/locals()/globals()", so the intent was always right, the top-level
    fallback case was just wrong) and removed a dead, shadowed duplicate `globals()` registration
    that had been silently masking real behavior for who knows how long. Regression test:
    `M5_Imports/ImportTests.cs` (`Globals_at_module_top_level_targets_that_module_not_builtins` —
    uses two real separate modules to prove no leak, not just that a write "worked").
    - **Method note, worth repeating for future sessions**: the failing name genuinely existed
      (`hasattr(typing_extensions, name)` for every intermediate check looked right), so the bug
      hid behind a wall of correct-looking evidence until the bisection got specific enough — check
      `globals()` state *from inside* the suspect module's own execution, not just from outside.
      Same lesson as the `from . import dataclasses` bug at the start of this plan: verify with a
      minimal, targeted repro rather than trusting where the traceback points.
  - `TypeError: class base must be a class, got _TypedDictSpecialForm` — paused, resumed with
    `procedi con la roadmap` — and turned into the best fix of the whole plan so far:
  - [x] **Generalized the base-class-substitution mechanism to CPython's actual `__mro_entries__`
    protocol**, instead of the earlier `GenericAliasModule`-specific special case. `ExecClassDef`
    (`Interp.cs`) now evaluates every base first (needed: the real protocol passes the *whole*
    original bases tuple to each `__mro_entries__` call), then for any base that's a `PyInstance`
    exposing `__mro_entries__`, calls it and splices in whatever classes it returns — exactly what
    real CPython does for `class Foo(Generic[T]):`, `class Foo(SomeGenericAlias):`, and now
    `class Foo(TypedDict):` (`_TypedDictSpecialForm.__mro_entries__` returns `(_TypedDict,)` in the
    real `typing_extensions.py` source — nothing pydantic-specific needed on our end once the
    general mechanism existed). `GenericAliasModule.GenericAliasClass` got its own real
    `__mro_entries__` returning `(origin,)`, replacing the old ad-hoc `IsAlias` check it used to
    need. Full suite stayed green through the refactor, including the earlier `Generic[T]`
    regression test — confirms the generalization is a strict superset of the special case, not a
    parallel path that could drift from it.
  - `ModuleNotFoundError: No module named 'datetime'` — the other item flagged as substantial scope
    from the very start of Phase 1 (alongside `re`, tackled later in this same round). Implemented
    for real (new file `DateTimeModule.cs`): `date`/`time`/`datetime`/`timedelta`/`timezone`, backed
    by `System.DateTime`/`TimeSpan`, same generic-dunder-dispatch approach as `Decimal`/`complex`.
    Construction, arithmetic (`date`/`datetime` ± `timedelta`, `date`/`datetime` − `date`/`datetime`
    → `timedelta`), comparisons, `isoformat`/`strftime` (a real format-code translator, not a
    lookup-table stub), `now`/`today`/`utcnow`, `.replace()`, `timezone.utc`. v1 scope: no full
    `strptime` parsing, no non-UTC timezone arithmetic beyond storing `tzinfo`.
    - **Found and fixed a real, general bug via manual verification** (not the pydantic probe —
      this class of bug wouldn't have surfaced there for a while): `date.min`/`max` and
      `datetime.min`/`max` are built *inside* `BuildDateClass()`/`BuildDateTimeClass()`, which are
      themselves the initializers for the `DateClass`/`DateTimeClass` **static fields**. Building
      them via the shared `MakeDate`/`MakeDateTime` helpers — which construct a `PyInstance`
      against that *same static field* — meant referencing a field that was still `null` mid-
      assignment, producing instances attached to a null class and crashing
      (`NullReferenceException`) the moment anything touched them. Fixed by constructing those two
      instances directly against the local `cls` variable inside each `Build*Class()` instead.
      Regression test: `isinstance(date.min, date)`/`isinstance(datetime.max, datetime)` — this
      exact shape of bug (a class's own constant instances referencing its not-yet-assigned static
      field) is the kind of thing worth checking for whenever a class needs to expose "instances of
      itself" as class-level constants.
    - Also fixed `timedelta.__str__`'s formatting while verifying manually: it was zero-padding the
      hours field (`"1 day, 02:00:00"`); real CPython doesn't (`"1 day, 2:00:00"`). Rewritten
      cleanly (the original had dead code — a pointless replace-and-undo round trip).
    - Verified manually against known values before writing tests (every result matched real
      Python's exactly); 10 tests: `M6_Stdlib/StdlibTests.cs` (`DateTimeTests`).
  - `ModuleNotFoundError: No module named 'ipaddress'` — new module (`IpAddressModule.cs`):
    `IPv4Address`/`IPv6Address`/`IPv4Network`/`IPv6Network`/`IPv4Interface`/`IPv6Interface`, backed
    by `System.Net.IPAddress` for the address family and hand-rolled CIDR bit-math for network/
    broadcast-address computation and containment (`__contains__`). v1 scope: construction/
    validation, string formatting, containment, comparison — no address arithmetic, no subnet-
    splitting helpers. Manual verification caught one bug before it became a test: `IPv4Interface`'s
    `__str__`/`__repr__` interpolated a wrapped address `PyInstance` directly into a C# string,
    which calls `PyInstance.ToString()` (`"<IPv4Address object>"`) instead of dispatching to the
    Python-level `__str__` — fixed by storing the raw `System.Net.IPAddress` alongside for
    formatting, sidestepping the dispatch question entirely rather than threading `interp`/`PyOps.Str`
    through. 6 tests: `M6_Stdlib/StdlibTests.cs` (`IpAddressTests`).
  - `ModuleNotFoundError: No module named 're'` — the other originally-flagged-as-substantial item
    (1.4, from the very start of Phase 1), now closed too. New module (`ReModule.cs`), backed by
    **`System.Text.RegularExpressions`** — "a real backtracking engine, not a hand-rolled subset",
    exactly as the plan called for from day one. Python and .NET regex syntax agree on nearly
    everything that matters; only named-group syntax needed translating (`(?P<name>...)` →
    `(?<name>...)`, `(?P=name)` → `\k<name>`, both via two `Regex.Replace` calls — an earlier,
    much messier hand-rolled string-splicing version was rewritten clean once it was working, since
    correctness first / clarity after is the right order but not the right place to stop).
    `compile`/`match`/`fullmatch`/`search`/`findall`/`finditer`/`sub`/`subn`/`split`/`escape`, the
    common flags (`I`/`M`/`S`/`X`/`A`/`U`), a real `Pattern` class (compiled-regex reuse) and `Match`
    class (`group`/`groups`/`groupdict`/`start`/`end`/`span`). Verified line-by-line against real
    CPython output for every function before writing tests (all matched exactly) — 8 tests:
    `M6_Stdlib/StdlibTests.cs` (`ReTests`).
  - `ModuleNotFoundError: No module named 'colorsys'` — small, self-contained pure math (RGB↔YIQ/
    HLS/HSV conversions), so implemented in full rather than just the two functions
    (`rgb_to_hls`/`hls_to_rgb`) the probe actually needed — ported directly from CPython's own
    algorithms. Verified against known values (pure red/green round-trip through HLS/HSV correctly,
    gray has zero saturation) before writing 3 tests: `M6_Stdlib/StdlibTests.cs` (`ColorSysTests`).
  - `ImportError: cannot import name '_BaseAddress' from 'ipaddress'` — pydantic's `networks.py`
    subclasses `ipaddress._BaseAddress`/`_BaseNetwork` directly (`class IPvAnyAddress(_BaseAddress)`
    etc.) purely to hang its own classmethods off, the same way real CPython's `IPv4Address`/
    `IPv6Address` and `IPv4Network`/`IPv6Network` do. Fixed by giving `IpAddressModule.cs` real
    (if otherwise-empty) `_BaseAddress`/`_BaseNetwork` marker classes and making the concrete
    classes actually subclass them — matching CPython's real hierarchy instead of a special case.
    1 regression test: `M6_Stdlib/StdlibTests.cs` (`IpAddressTests`, `issubclass` check).
  - `ImportError: cannot import name 'Match' from 'typing'` — real CPython's `typing.Match`/
    `Pattern` are (deprecated) generic aliases over `re.Match`/`re.Pattern`, used in pydantic's
    `networks.py` regex-returning helpers (`Pattern[str]`, `Match[str]`). Added as bare-placeholder
    names mapped to `ReModule`'s real `MatchClass`/`PatternClass` via `GenericAliasModule.MapOrigin`
    — any `PyClass` is already subscriptable through the existing generic-alias machinery, so this
    was a one-line-per-name wiring job, not new machinery. 1 regression test:
    `M4_Functions/IntrospectionTests.cs`.
  - `ImportError: cannot import name 'new_class' from 'types'` — pydantic's `conlist`/`conset`/
    `confrozenset` (constrained-collection field types) use `types.new_class` to attach per-call
    constraint attributes (`min_items` etc.) to a fresh subclass, the same way a real `class`
    statement would but from data instead of syntax. Implemented for real: executes `exec_body(ns)`
    then builds the class from `(name, bases, ns)`. Immediately followed by `ImportError: cannot
    import name 'prepare_class' from 'types'` (used only inside pydantic's `create_model()`, not at
    import time, but implemented for real anyway rather than half-built) — `resolve_bases` (real:
    applies `__mro_entries__` to non-class bases, preserving object identity when nothing changes,
    since callers rely on that identity check) and `prepare_class` (simplified but real: PySharp
    ignores custom metaclasses everywhere already, so `meta` is always `type`). Getting
    `prepare_class`'s returned `meta(name, bases, ns)` to actually work exposed a real, standing
    gap: **`type(name, bases, namespace)` — the 3-arg dynamic-class-creation call — was never
    implemented**, only `type.__new__(metaclass, name, bases, namespace)` (the 4-arg unbound-method
    form) was. Fixed for real in `Builtins.cs`'s `type` builtin, sharing the actual class-building
    logic with `type.__new__` via a new `TypeConstructorMethods.BuildClass` helper (also reused by
    `types.new_class`, removing that earlier duplicate). 3 regression tests:
    `M4_Functions/IntrospectionTests.cs`.
  - `ModuleNotFoundError: No module named 'pickle'` — pydantic's `parse.py` supports a pickle
    protocol for `load_str_bytes` (called at runtime, not import time). New module
    (`PickleModule.cs`): `dumps`/`loads`/`dump`/`load`, `PickleError`/`PicklingError`/
    `UnpicklingError`. v1 scope, same descoping pattern as other modules this round: real,
    round-trip-correct serialization for the common built-in scalar/container types (None/bool/int/
    float/str/bytes/bytearray/list/tuple/dict/set/frozenset) via a simple tagged binary format
    PySharp controls end to end — not CPython's actual pickle byte protocol (a large surface of its
    own), no object/instance pickling. Manually verified every case against expected round-trip
    values before writing tests — caught a real, general, pickle-unrelated bug this way:
    **`bytearray(b"ba") == bytearray(b"ba")` came back `False`** — `PyOps.PyEquals` had no
    `PyByteArray` case at all, silently falling through to `return false` for any bytearray
    comparison. Fixed (also added `bytes`-vs-`bytearray` cross-type equality, matching real
    CPython). 4 tests for pickle (`M6_Stdlib/StdlibTests.cs`, `PickleTests`) + 1 regression test for
    the bytearray bug (`M3_Evaluator/CollectionTests.cs`).
  - `ImportError: cannot import name 'is_dataclass' from 'dataclasses'` — pydantic's own
    `dataclasses.py` submodule (imported as part of the package, whether or not the user touches
    pydantic dataclasses) needs this at import time. Real check (not a stub): `ApplyDataclass` now
    stamps `__dataclass_fields__` on every `@dataclass`-decorated class (a real `PyDict` of field
    names — not yet full `Field` objects, since nothing has needed `dataclasses.fields()` itself
    yet), and `is_dataclass(obj)` mirrors CPython's own `hasattr(cls, '__dataclass_fields__')` test
    for both classes and instances. 1 regression test: `M6_Stdlib/DataclassesTests.cs`.
  - **`import pydantic` succeeds. Phase 1 is done.** Verified beyond the bare import too:
    `from pydantic import BaseModel; print(BaseModel)` → `<class 'BaseModel'>`. The **actual next
    frontier is Phase 2, not another stdlib gap**: constructing a `BaseModel` subclass instance
    fails (`AttributeError: 'type' object has no attribute '__config__'`) because real pydantic's
    `ModelMetaclass.__new__` — which builds `__config__`/`__fields__`/validators while the `class
    User(BaseModel): ...` statement executes — never runs; `ExecClassDef` ignores custom metaclasses
    everywhere in PySharp today (a deliberate, documented simplification, not an oversight). This is
    a real architectural gap (custom-metaclass support), not a missing-name gap like everything else
    in this list — deliberately left for a dedicated look rather than guessed at inline. Captured as
    its own smoke test (`PydanticSmokeTests.Defining_and_instantiating_a_BaseModel_subclass_is_the_current_frontier`)
    so Phase 2 has a concrete, real, currently-failing starting point.
  - Full suite green after every single step above (759/759 by the end of this round, up from 721
    at the start of it — 38 new tests this round alone); `git status` clean of scratch installs
    after each round.

## Phase 2 — pydantic v1 (real scope now known — Phase 1 is done)

- [x] 2.1 Get `import pydantic` to succeed. Done — see Phase 1.9's final entries.
- [x] 2.2 Get a minimal `BaseModel` subclass to construct and validate simple fields (str/int/bool),
  raising `ValidationError` on bad input. Done — required building real (simplified) custom-metaclass
  support into `ExecClassDef`, since real pydantic's `ModelMetaclass.__new__` must run during the
  `class User(BaseModel): ...` statement to build `__config__`/`__fields__`/validators. Full
  blow-by-blow below (2.2.1).
- [ ] 2.3 Expand field types/validators as real usage in Phase 4's target app demands. Do not attempt
  full pydantic v1 API parity — same non-goal discipline as NUMPY_PLAN.md's "not full API parity".
  **Current known gap**: `BaseModel.dict()` includes a spurious `__fields_set__` key — real pydantic
  keeps it out of `self.__dict__` (and so out of `.dict()`) via a `__slots__` entry giving it storage
  separate from the instance's regular attribute dict; PySharp doesn't implement real
  `__slots__`-backed separate storage (every instance attribute lives in the same `PyInstance.Dict`,
  slotted or not — a deliberate simplification up to now, first surfaced here). Captured as
  `PydanticSmokeTests.Basemodel_dict_output_is_the_current_frontier`, a concrete starting point for
  whoever picks up real `__slots__` support next. Not attempted inline: implementing real per-slot
  storage is its own architectural undertaking, same category of decision as 2.2's metaclass work,
  not a quick fix.

### 2.2.1 — custom-metaclass support: the blow-by-blow

Real, simplified (not full-generality) support for `class X(Y, metaclass=M): ...`, landed in one
long session once the author said "procedi" to continue past Phase 1's completion:

- **Core plumbing** (`Interp.cs`): `PyClass` gained a `Metaclass` field (null = default `type`).
  `ExecClassDef` now evaluates an explicit `metaclass=` keyword argument (previously silently
  dropped, "ignored in v1") and determines a winning metaclass — the explicit kwarg if it's a real
  class, else the first base that already carries one (subclasses inherit their base's metaclass
  without redeclaring it — real CPython computes the most-derived metaclass across every base for
  multi-metaclass conflicts; not needed for anything in scope so far, single custom-metaclass chains
  only). When a metaclass is found, `ExecClassDef` builds the class body's namespace as a plain
  `PyDict` (not immediately a `PyClass`) and **calls the metaclass's own `__new__`** with it — the
  same thing `type.__call__(mcs, name, bases, namespace)` does for a real `class` statement in
  CPython — instead of always allocating a plain `PyClass`. Metaclass `__init__` is deliberately not
  dispatched (no metaclass encountered defines one).
- For `super().__new__(...)`/a stub base's `.__new__` called directly to actually build something:
  added a real `type.__new__`-shaped fallback (`ObjectNewFallback`) reachable both via `super()` (the
  `PySuper` GetAttr case) and via **direct** attribute access on a class that doesn't override it
  (the plain `PyClass` GetAttr case) — needed because typing_extensions' real `_ProtocolMeta.__new__`
  calls `abc.ABCMeta.__new__(mcls, name, bases, namespace, **kwargs)` **directly**, not through
  `super()` (its own comment explains why: avoiding slow real-CPython ABCMeta machinery on old
  versions) — our bare `ABCMeta` stub had no `__new__` of its own, so this was a real, previously
  unreachable gap. Same PyClass-direct-access fallback added for `__init__` and `__setattr__`.
- **`object.__setattr__(obj, '__dict__', newdict)` bulk-namespace-replace**: real pydantic's
  `BaseModel.__init__` sets every validated field at once via `object_setattr(self, '__dict__',
  values)` (a real, documented CPython idiom: assigning `obj.__dict__` replaces the whole instance
  namespace). The pre-existing `object.__setattr__` implementation (already in `Builtins.cs`, predates
  this round) didn't know about this special case — it just set a literal key named `"__dict__"`.
  This was the single most confusing bug of the round: `f.__dict__` printed the *correct* merged
  contents (since `__dict__` access already special-cases returning `inst.Dict` directly) while
  `f.x` failed, because the real per-key entries were never actually written — found only by
  instrumenting `TryGetAttr` itself with a raw dict-hash/count dump, not by reading source, after a
  plain source read strongly suggested the fix should have worked.
- **`issubclass()`/`isinstance()` didn't accept builtin types as arguments** on either side:
  `issubclass(int, X)` (arg 1) and `issubclass(X, dict)` (arg 2) both raised `TypeError`, since
  `int`/`dict`/etc. are `PyBuiltinFunction`, not `PyClass` — `IsSubclass` (new shared helper,
  mirroring the existing `IsInstance`) now resolves a builtin-type-named `PyBuiltinFunction` to the
  same singleton pseudo-base-class `class Foo(int): ...` already uses, on **both** the class-being-
  checked and the class-being-checked-against side. Also fixed `isinstance(dict, type)` (a builtin
  type object IS an instance of `type`) the same way. Found via pydantic's real
  `isinstance(cls, type) and issubclass(cls, class_or_tuple)` idiom (`utils.lenient_issubclass`).
- **`dict.keys()` couldn't be used with the set operators**: was a plain `PyList` (order-preserving,
  but not set-like). New `PyDictKeysView` type: still order-preserving for iteration (`list(d.keys())`
  stays correct), but now real dict_keys-shaped — usable with `&`/`|`/`-`/`^`. Also made a **plain
  dict** set-like over its own keys for those same operators when the other side isn't also a dict
  (matching real CPython's `dict_keys.__ror__` etc. treating a dict as its keys) — `dict | dict`
  still merges (checked first, explicitly, ahead of the generic set-union path). Found via pydantic's
  real `kwargs.keys() & allowed_config_kwargs` and `fields | private_attributes.keys() |
  {'__slots__'}` (`ModelMetaclass.__new__`).
- **`v.__class__` for a builtin container/scalar value used to be a bare, non-constructible
  pseudo-class** — fine for identity comparisons (`type(x) == type(y)`) but broken for the common
  `v.__class__(new_items)` clone-in-concrete-type idiom, since the pseudo-class had no `__init__`.
  `TypeNamePseudoClass` now takes the interpreter and returns the **real builtin constructor
  function** (`d["set"]`, `d["list"]`, etc.) when one exists for the type name, falling back to the
  bare singleton pseudo-class only for genuinely non-constructible types (function/method/module/
  NoneType/...). Found via pydantic's real `v.__class__(seq_args)` (`BaseModel._get_value`, used by
  `model.dict()`).
- Smaller, self-contained gaps closed the same way (real fix, not a stub) along the way: a class's
  own namespace dict never carried a real `__module__` (always fell back to a hardcoded `"builtins"`)
  — this one **actively caused an infinite loop**, not just a wrong answer: pydantic's own
  `ModelMetaclass.__new__` skips its field-processing block specifically for `BaseModel`'s own
  definition by checking `namespace.get('__module__') == 'pydantic.main'`; with `__module__` always
  missing, that check silently failed to fire, so `BaseModel`'s own definition ran the *full* field-
  processing logic on itself, recursing into pydantic's own self-referential type machinery. Found by
  bisecting with `Console.Error` trace prints around every metaclass build (after several dead-end
  attempts to guess the cause from source alone — the traceback's line numbers were themselves
  misleading, `"<string>"` filenames throughout meaning line numbers don't correlate with the real
  installed `.py` files at all). `module.__dict__` (a module's own namespace) wasn't handled at all —
  found via pydantic's real `sys.modules[model.__module__].__dict__` idiom
  (`typing.update_model_forward_refs`). `inspect.Signature`/`inspect.Parameter` only existed via the
  internal `signature()`-builder path (bare classes, no real `__init__`) — real pydantic's own
  `generate_model_signature` constructs them directly (`Signature(parameters=[...],
  return_annotation=...)`, `Parameter(name, kind, default=..., annotation=...)`); given real
  `__init__`s. `itertools.chain.from_iterable` (the alternate-constructor classmethod real CPython's
  `chain` exposes) didn't exist at all, since `chain` was a plain `PyBuiltinFunction` with no
  attribute surface — added via the same unbound-method dispatch table pattern `type.__new__`/
  `dict.get` already use.
- Full suite green after every single step (776/776 by the end of this round, up from 759 at the
  start of it — 17 new tests); `git status` clean of scratch installs after each round.

## Phase 3 — starlette + anyio 🟡 in progress

- [ ] 3.1 Get `import starlette` to succeed. **In progress** — real starlette 1.4.1 (from PyPI,
  unmodified) + its real `anyio` dependency drove a long probe-driven round (triggered by the
  author's "puoi proseguire" after Phase 2's BaseModel milestone), closing ~20 real gaps: full
  blow-by-blow in 3.1.1 below. That round's frontier — `match`/`case` structural pattern matching
  (PEP 634) — is now **done**: real parser + interpreter support landed (3.1.2 below), verified
  directly against the anyio statement that originally blocked this phase. A further probe-driven
  round past that (3.1.3 below) closed 6 more real gaps — `concurrent.futures`, `stat`, `os.chmod`,
  real `abc.ABC.register()` virtual-subclass support, a `typing.Generic[T]` MRO-deduplication bug,
  and `typing.override`. A fourth round (3.1.4 below), triggered by the author's "match/case parte
  2" commit followed by "prosegui", pushed `import starlette` all the way through
  `starlette.applications`/`routing`/`responses`/`requests` — closing 12 more real gaps
  (`subprocess`, `tempfile`, `io.TextIOWrapper`, `http`/`http.cookies`, `email.utils`, a
  `re.Pattern.search` pos/endpos bug, generic-alias re-subscription with TypeVar substitution,
  `html`, `traceback`/`sys.exc_info`, `contextlib.asynccontextmanager`, real `object.__eq__`/
  `__ne__`/`__hash__`/`__repr__`/`__str__` defaults, and — found chasing a regression from that last
  one — a real recursion-depth guard, since this interpreter had never had one at all before).
  **Current frontier**: `mimetypes` (no module named `mimetypes`) — used by starlette's real
  `staticfiles.py`. Not started.
- [ ] 3.2 Minimal ASGI app + routing working, driven by PySharp's `asyncio` (scenario 1b's reactor —
  `add_reader`/`add_writer`/`run_in_executor` — is exactly the machinery an ASGI server needs; this is
  where that investment pays off for scenario 2). Whether `anyio` gets its own real support or a thin
  asyncio-backed shim (it supports multiple backends upstream; only the asyncio backend matters here)
  is a decision to make once its actual usage surface from starlette is visible.

### 3.1.1 — the blow-by-blow (real starlette 1.4.1 + anyio probe)

Every fix below was found by running real, unmodified `starlette`/`anyio` (both from PyPI) and
fixing the next real error — same discipline as every phase before this one. Every fix has its own
regression test; suite stayed green throughout.

- **New stdlib modules, built for real** (not stubbed): `shlex` (a POSIX-aware tokenizer ported from
  CPython's own algorithm — found via starlette's real `shlex(value, posix=True)` comma-splitting a
  header value while respecting quoted commas); `contextvars` (`ContextVar`/`Token`/`Context`/
  `copy_context`, scoped to a single current value per `ContextVar` rather than true per-task context
  isolation — PySharp's coroutines already run cooperatively one at a time, so nothing observed needs
  real forked-context propagation); `importlib` (`import_module`, delegating to the same real
  `Importer` real `import` statements use); `textwrap` (`dedent`, ported faithfully); `signal`
  (`Signals`, a real IntEnum built via real parsed Python source — no actual OS signal delivery/
  handling attempted, nothing's called `signal.signal`/`getsignal` yet).
- **`urllib.parse` grew real `SplitResult`/`urlsplit`/`parse_qsl`** — the pre-existing `urlparse`
  only ever returned a raw positional tuple; starlette's own `URL` class relies on `urlsplit()`
  returning a real tuple-like object with named fields (`.scheme`/`.netloc`/...), a `.geturl()`
  method, and derived `.hostname`/`.port`/`.username`/`.password` properties parsed out of `netloc`
  — all implemented for real, ported from CPython's own algorithm, verified against known-correct
  values before writing tests.
- **`inspect` grew real predicates** (`isfunction`/`ismethod`/`isclass`/`ismodule`/`isbuiltin`/
  `isgeneratorfunction`/`iscoroutinefunction`/`isgenerator`/`iscoroutine`/`isawaitable`, plus
  `isasyncgenfunction`/`isasyncgen` which correctly always report `False` since PySharp can't produce
  async generators at all — see ROADMAP.md) — found via starlette's own route-handler introspection
  and anyio's real `from inspect import isasyncgen` (transitively, via a nested import).
- **`threading.local`**: real per-OS-thread attribute storage backed by `System.Threading.ThreadLocal`,
  not a single shared dict — routed through the interpreter's existing real `__getattr__`/
  `__setattr__` class-override dispatch (the native `ThreadLocal<PyDict>` lives in the instance's own
  dict under a key Python code never asks for, so there's no recursion back through that dispatch).
  Verified with real concurrent `threading.Thread`s each seeing independent values. Found via anyio's
  real `threadlocals = threading.local()` (_core/_eventloop.py).
- **`socket.AddressFamily`/`SocketKind`**: real IntEnum classes (built the same real-parsed-Python-
  source way as `signal.Signals`) alongside the pre-existing plain-int `AF_INET`/`SOCK_STREAM`
  constants (left untouched — real IntEnum values compare equal to plain ints, so nothing needed to
  change for the two to stay consistent). Found via anyio's real `from socket import AddressFamily`.
- **`io.IOBase`**: a real (if bare) base class, with `StringIO`/`BytesIO` now actually subclassing it
  (previously unrelated classes) — real CPython's whole `io` hierarchy descends from it, so
  `isinstance(f, IOBase)` is a common real check. Found via anyio's real `from io import IOBase`.
- **`contextlib.ExitStack`/`AsyncExitStack`**: real LIFO callback-stack semantics
  (`enter_context`/`push`/`callback`/`pop_all`/`close`, unwound in LIFO order on `__exit__`, matching
  real CPython) — not a stub. The async variant's async-specific entry points
  (`enter_async_context`/`push_async_exit`/`push_async_callback`/`aclose`) support context managers
  whose `__aenter__`/`__aexit__` resolve immediately (an already-resolved `Future`, matching every
  async context manager written in this codebase so far); one whose `__aenter__`/`__aexit__` is a
  real *suspending* coroutine raises `NotImplementedError` rather than silently hanging or
  misbehaving — a real, clearly-scoped limitation (driving an arbitrary inner coroutine to completion
  from a plain builtin function, outside the calling coroutine's own suspension loop, isn't supported
  yet), not attempted blind. Found via anyio's real `AsyncExitStack()` usage — referenced but not yet
  exercised beyond import.
- **PEP 604 union operator (`X | Y` between types)**: `str | bytes`-style expressions between two
  type-like objects (real classes, builtin type constructors, `None`, or an existing union/generic
  alias for chaining `X | Y | Z`) previously raised `TypeError: unsupported operand type(s) for |`,
  since neither operand is a `PyInstance` (the only case the existing dunder-dispatch fallback
  handled) — now builds a real generic-alias union (`types.UnionType` as origin), so
  `get_origin`/`get_args` work. Found via anyio's real module-level `StrOrBytesPath: TypeAlias = str
  | bytes | PathLike[str] | PathLike[bytes]` (abc/_eventloop.py) — a genuine **value expression**,
  not just a type-hint comment, and evaluated *eagerly*, since PySharp doesn't defer annotations
  under `from __future__ import annotations` the way real CPython does (a known, standing difference
  — real CPython would never evaluate this expression at all under that future import; nothing in
  scope has needed true deferred-annotation semantics yet, so this wasn't attempted).
- **PEP 585 builtin generic subscripting (`tuple[int, str]`, `list[int]`, ...)**: subscripting a
  builtin type *directly* (not just `typing.Tuple[...]`) raised `TypeError: 'function' object is not
  subscriptable`, since builtin types are `PyBuiltinFunction`, not `PyClass` (the only case
  `GetItem`'s generic-alias handling covered). Now builds the same real `GenericAliasModule` alias
  `List[int]` etc. already build, with the builtin function itself as `__origin__` (matching real
  `get_origin(tuple[int, str]) is tuple`). Found via real modern
  (`from __future__ import annotations`-era) type hints in typing_extensions/anyio using this syntax
  directly.
- **The single most consequential fix of this round: `Instantiate()` now calls a class's own real
  `__new__`, not just `__init__`.** Previously, constructing ANY instance always did `new
  PyInstance(cls)` directly, completely ignoring a class's own `__new__` if it defined one — a real
  gap for the common `def __new__(cls, ...): ...; return obj` idiom (sometimes returning an object
  that ISN'T even an instance of the wrapper class at all). Now implements real CPython's
  `type.__call__` protocol: call `cls.__new__(cls, *args, **kwargs)` if the class (or an ancestor)
  defines one; only call `__init__` afterward if the result actually is an instance of `cls` (real
  Python skips `__init__` entirely otherwise). Verified safe for every pre-existing class: `PyClass
  .TryLookup` is a raw MRO dict-scan, so it never picks up the synthetic `object.__new__` fallback
  `GetAttr` exposes for classes that don't define their own (added earlier in Phase 2) — meaning this
  is a strict no-op for the overwhelming majority of classes that never define `__new__`, exactly as
  before. Found via typing_extensions' real backported `class TypeVar(metaclass=_TypeVarLikeMeta):
  def __new__(cls, name, ...): ...` (needed on any Python version without PEP 696, i.e. everywhere
  PySharp reports itself as being) — calling `TypeVar('T')` raised `TypeError: TypeVar() takes no
  arguments`, the exact same message the *unrelated* `Signature`/`Parameter` gap produced back in
  Phase 1, both symptoms of the identical root cause finally fixed here for real, everywhere.
- Full suite green after every single step above (799/799 by the end of this round, up from 776 at
  the start of it — 23 new tests); `git status` clean of scratch installs after each round.

### 3.1.2 — `match`/`case` structural pattern matching (PEP 634)

Real parser + interpreter support for `match`/`case` (not a stub or partial subset), triggered by the
author's direct "match/case" follow-up request once this was identified as Phase 3's frontier.

- **Parser**: `match`/`case` are soft keywords in real Python — never reserved — so PySharp's lexer
  already tokenized them as plain `Name`s (they were never added to the hard `Keywords` set). A
  non-backtracking lookahead (`LooksLikeMatchStatement`: does `match <expr>:` end in
  `NEWLINE INDENT "case"`?) disambiguates `match x:` (a statement) from `match(1, 2)`/`match = 5`/
  `match + 1` (plain uses of the name `match`) the same way real CPython's own PEG grammar does. A
  full pattern grammar was added: literal, capture, wildcard (`_`), value (dotted-name), sequence
  (list/tuple, with `*rest` star-capture), mapping (dict, with `**rest`), class (`Point(0, y=y)`),
  or- (`|`), and as- (`as name`) patterns, plus guards (`if cond`).
- **Interpreter**: real matching semantics, not just parsing — `ExecMatch`/`TryMatchPattern`
  (`Interp.cs`) implement each pattern kind for real: literal patterns use `is` for the `None`/`True`/
  `False` singletons specifically (so `case True:` does NOT match `1`, even though `1 == True` in
  Python — a real, easy-to-get-wrong CPython semantic); sequence patterns explicitly exclude `str`/
  `bytes`/`bytearray` (PEP 634's own carve-out, since those are iterable but matching them
  character-by-character is never what real code wants); class patterns use `__match_args__` for
  positional sub-patterns and real attribute lookup for keyword ones, with a builtin-type special case
  (`int(n)`, `str(s)`, ...) matching the whole subject value against the single positional pattern,
  since builtins have no real `__match_args__`.
- Verified against three hand-written probe scripts (covering every pattern kind) BEFORE writing any
  formal tests — every result matched real Python's output on the first try, catching zero semantic
  bugs. Verified the soft-keyword heuristic doesn't false-positive: `match` used as a variable name, a
  function parameter name, a dict key, and in a real `re.match(...)` call all continued to work.
- **Directly verified against the real original blocker**: re-ran the exact anyio probe that had hit
  `SyntaxError: expected end of line, got 'self'` on `match self.status: case TaskHandle.Status
  .PENDING: ...` — confirmed the error is gone and the probe progresses further.
- One small fix alongside it: **`typing.Never`** (PEP 654-adjacent bare placeholder, like the
  pre-existing `NoReturn`/`Text`/etc.) was missing — trivial one-line addition, found immediately after
  the `match` fix when the probe advanced past it.
- 22 tests added (`M2_Parser/MatchParsingTests.cs`: parser/AST-dump coverage for every pattern kind and
  the soft-keyword disambiguation cases; `M17_Match/MatchExecutionTests.cs`: execution semantics,
  including a test reproducing the exact real anyio `TaskHandle.Status.PENDING` scenario). Full suite
  green throughout: 825/825 by the end of this round, up from 799 at the start of it.

### 3.1.3 — past `match`/`case`: 6 more real gaps (concurrent.futures, stat, chmod, ABC.register, Generic MRO, typing.override)

Continuing the same starlette+anyio probe past the now-fixed `match` statement, same discipline as
every round before it: run the real, unmodified packages, fix the next real error, verify manually
against known-correct behavior, write a regression test, keep the suite green.

- **`concurrent.futures.Future`**: a genuinely new kind of Future, distinct from asyncio's
  `PyFuture` — that one is cooperative and single-threaded, driven by PySharp's own event loop, but
  `concurrent.futures.Future` is meant to be set from one real OS thread and awaited from another
  (anyio's `BlockingPortal` bridges a worker thread into the event-loop thread with exactly this).
  Implemented for real with a new native `ConcurrentFuture` class backed by a real .NET `Monitor`
  (matching CPython's own `threading.Condition`-based implementation): `result()`/`exception()`
  genuinely block the calling OS thread until resolved (with real timeout support), `cancel()`/
  `set_running_or_notify_cancel()`/`done()`/`running()`/`cancelled()` follow real CPython's state
  machine, `add_done_callback` invokes immediately if already done, exceptions raised by a callback
  are swallowed rather than propagating (matching real CPython), and calling `set_result`/
  `set_exception` twice raises a real `InvalidStateError`. Verified against 4 hand-written probe
  scenarios (normal resolution, exception, cancellation, double-set) before writing tests — every
  result matched real CPython's documented behavior. Found via anyio's real `from concurrent.futures
  import Future` (`from_thread.py`/`_backends/_asyncio.py`).
- **`stat`**: the `S_IF*`/`S_IS*` file-mode bitmask constants and predicates (`S_ISREG`/`S_ISDIR`/
  `S_ISLNK`/`S_ISSOCK`/`S_IMODE`/`S_IFMT`), ported faithfully from CPython's own `Lib/stat.py` (pure
  bit arithmetic, so a straightforward direct port, not a guess). Found via starlette's real
  `stat.S_ISREG(mode)`/`S_ISDIR`/`S_ISLNK` (`responses.py`/`staticfiles.py`) and anyio's `S_ISSOCK`.
- **`os.chmod`**: real file-permission changes, not a no-op — `File.SetUnixFileMode` on non-Windows,
  and on Windows (where this whole suite runs) the read-only-attribute toggle real CPython itself
  falls back to there (Windows has no POSIX permission bits at all; CPython's own `os.chmod` on
  Windows only honors the user-write bit for exactly this reason — a real, documented CPython
  platform limitation, not a PySharp shortcut). Verified end to end against a real file (not just
  "doesn't throw"). Found via anyio's real `from os import PathLike, chmod` (`_core/_sockets.py`).
- **Real `abc.ABC`/`ABCMeta.register()`**: virtual-subclass registration, not previously implemented
  at all. `PyClass` gained a `VirtualSubclasses` registry (`RegisterVirtualSubclass`), consulted by
  `IsSubclassOf` alongside the real MRO — so `isinstance`/`issubclass` recognize a registered class
  (and its own subclasses, transitively, exactly like real CPython) without it ever joining the
  registering class's actual MRO. `os.PathLike` now derives from `abc.ABC` (matching real CPython's
  `class PathLike(abc.ABC)`) purely to inherit `register` for free. Found via anyio's real
  `PathLike.register(...)`-style usage reachable from `_core/_fileio.py`.
- **`typing.Generic[T]` MRO-entries de-duplication — a real, previously-latent bug**: a class with
  *two* generic bases where one already implies the other (`class Foo(Generic[T], SomeGeneric[T])`,
  where `SomeGeneric` already derives `Generic`) raised `TypeError: Cannot create a consistent MRO`,
  since the existing `__mro_entries__` implementation always contributed bare `Generic` for every
  generic-alias base, producing a resolved bases list with `Generic` appearing twice at incompatible
  positions. Real CPython's `typing.py` avoids exactly this by having a redundant `Generic[T]`
  contribute nothing when another base already brings `Generic` in transitively; `GenericAliasModule`
  now does the same (`GenericPlaceholder`, set once by `MiscModules.CreateTyping`, plus an
  `OriginBringsInGeneric` check in `__mro_entries__`). Verified against 3 hand-written probe patterns
  (redundant pair, three-level chain, two independent generic bases that must both stay recognized)
  before writing tests. Found via anyio's real `class StapledObjectStream(Generic[T_Item],
  ObjectStream[T_Item])` — and the identical pattern recurs throughout `anyio/abc/_streams.py`'s
  whole stream-class hierarchy, so this wasn't a one-off.
- **`typing.override`** (PEP 698, Python 3.12+): a real, if small, runtime side effect — sets
  `__override__ = True` on the decorated function and returns it unchanged (a static-checker marker
  CPython itself still executes for real), not a bare passthrough. Found via anyio's real `from
  typing import override`.
- 11 tests added (`M6_Stdlib/StdlibTests.cs`: `ConcurrentFuturesTests`, `StatModuleTests`,
  `OsChmodTests`, `AbcRegisterTests`, `GenericMroDedupTests`, `TypingOverrideTests`). Full suite green
  throughout: 836/836 by the end of this round, up from 825 at the start of it.

### 3.1.4 — past `subprocess`: 12 more real gaps, all the way through `applications`/`routing`/`responses`/`requests`

Continuing the same probe past `subprocess`, same discipline throughout.

- **`subprocess`**: real process spawning on `System.Diagnostics.Process`, not a stub —
  `Popen` (real stdin/stdout/stderr pipes, `wait`/`communicate`/`poll`/`terminate`/`kill`, real
  `FileNotFoundError` for a missing executable), plus `run`/`call`/`check_call`/`check_output` and
  real `CalledProcessError`/`CompletedProcess`/`TimeoutExpired` (the latter three implemented as
  real parsed Python source, matching CPython's own `Lib/subprocess.py`). Verified against 6 real
  Windows subprocess scenarios (captured text output, nonzero exit under `check=True`, piping stdin
  through to stdout, `DEVNULL`, `check_output`, a missing executable) before writing tests. Real
  async subprocess integration (anyio's own `open_process`, wired into PySharp's event loop)
  remains out of scope — nothing in the import chain calls it.
- **`tempfile`**: real files/directories on disk — `gettempdir`/`mkdtemp`/`mkstemp`,
  `NamedTemporaryFile`/`TemporaryFile`/`SpooledTemporaryFile` (file-backed, always-spooled rather
  than CPython's memory-first optimization — a documented simplification, not a functional gap) and
  `TemporaryDirectory`. `mkstemp`'s returned fd is a synthetic counter, not a real OS-level
  descriptor — honest, and harmless, since PySharp has no `os.read`/`os.write(fd, ...)` low-level fd
  API at all yet for anything to misuse it with. Also added `os.rmdir`/`removedirs`/`rename`
  alongside it.
- **`io.TextIOWrapper`**: a real (duck-typed) text wrapper over any binary buffer object —
  encodes/decodes via UTF-8, forwards read/write/close/flush to the wrapped object's own methods, so
  it works over a real file, `BytesIO`, or a `Popen` pipe alike, matching real CPython's generality.
- **`http.HTTPStatus`**: a real IntEnum with a real `.phrase` per member (built by hand in C# with
  the standard IANA status-code/phrase table, rather than replicating CPython's `__new__(cls, value,
  phrase, ...)` tuple-unpacking Enum idiom, which PySharp's enum machinery doesn't support in
  general and nothing else in scope needs). **`http.cookies`**: a real (simplified) port of
  CPython's own `Lib/http/cookies.py` — real quoting (`_quote`)/unquoting (`_unquote`, found via
  starlette's real direct `http_cookies._unquote` call in `requests.py`'s `cookie_parser`) and real
  `Set-Cookie` formatting via `Morsel`/`BaseCookie`/`SimpleCookie`. These hold their own internal
  dict rather than actually subclassing `dict` (PySharp's `class X(dict):` doesn't back subclass
  instances with real storage yet — a separate, standing interpreter gap, not worth taking on just
  for this); everything starlette actually calls behaves identically either way.
- **`email.utils`**: just the RFC 2822 date helpers (`format_datetime`/`formatdate`/`parsedate`),
  real (ported from CPython's own algorithm), not the full MIME/message-parsing machinery — found
  via starlette's real `from email.utils import format_datetime, formatdate` (`responses.py`, for
  `Last-Modified`/`Date` headers) and `parsedate` (`staticfiles.py`, for real `If-Modified-Since`
  conditional-GET comparisons — real CPython's `parsedate` returns a 9-tuple that compares
  lexicographically, which is all `staticfiles.py` actually needs).
- **A real bug in `re.Pattern.search`/`match`/`fullmatch`/`finditer`: the `pos`/`endpos` arguments
  were silently ignored entirely.** Found the hard way: a hand-ported `http.cookies._unquote`
  (itself needed for the fix above) advances `pos` between successive `pattern.search(s, pos)`
  calls; since `pos` was never actually honored, every call re-matched from position 0, `pos` never
  advanced, and the loop span forever. Not cookies-specific — `pos`/`endpos` are a normal, commonly-
  relied-on part of the real `Pattern` API, now implemented for real via `Regex.Match(s, pos, len)`.
- **`typing.Generic`/generic-alias re-subscription (`SomeAlias[T][Concrete]`)**: not previously
  supported *at all* — `alias[index]` where `alias` is itself already a built generic alias (not a
  bare class) raised `TypeError: '_GenericAlias' object is not subscriptable`. Real CPython
  substitutes each free `TypeVar` found recursively in `__args__` (including inside `Callable`'s own
  parameter-list `PyList` and `Union`s of parameterized aliases) with the new subscript's value(s),
  positionally — now implemented for real (`GenericAliasModule.Resubscript`/`CollectTypeVars`/
  `Substitute`), including discovering along the way that `typing.TypeVar("T")` builds a fresh,
  uniquely-named `PyClass` (not a shared-class instance), requiring a marker-key identification
  scheme rather than a class-identity check. Found via starlette's real `applications.py`: a
  `Lifespan[AppType]` function-parameter annotation, eagerly evaluated despite `from __future__
  import annotations` being present (PySharp's standing, documented gap around deferred
  annotations — real CPython would never evaluate this particular expression at all).
- **`html`**: real `escape` (a direct port of CPython's own replace chain, same order, same
  `&#x27;` apostrophe encoding) and `unescape` (backed by .NET's own `WebUtility.HtmlDecode`, a real
  decoder, not guaranteed identical entity-for-entity coverage to CPython's full `html.entities`
  table for obscure entities).
- **`traceback.format_exc`/`print_exc`/`format_exception` + `sys.exc_info()`**: backed by a real
  interpreter-level plumbing change — `Interp`'s exception-handling stack (`_handling`) now tracks
  the full `PyRaise` (not just the exception instance), exposed as `Interp.CurrentHandledException`,
  and reused directly by the existing `PyErr.FormatTraceback` (the same formatting the REPL/CLI use
  for an uncaught exception). Bare `raise` (re-raise) also got more correct as a side effect: it now
  re-throws the exact same `PyRaise` object instead of wrapping a new one, preserving its traceback
  properly.
- **`contextlib.asynccontextmanager`**: applying the decorator (module-definition time — what
  `import starlette` itself exercises, via starlette's real `@asynccontextmanager async def
  create_collapsing_task_group(): ... yield tg ...` in `_utils.py`) works for real. Actually
  *entering* the resulting context manager needs to drive a real async generator, which PySharp
  doesn't support at all (a standing, documented gap — see ROADMAP.md's Axis A) — `__aenter__`/
  `__aexit__` raise a clear `NotImplementedError` instead of hanging or silently misbehaving, the
  same honest-limitation shape as `AsyncExitStack`'s suspending-coroutine case.
- **Real `object.__eq__`/`__ne__`/`__hash__`/`__repr__`/`__str__` default dunders.** Previously only
  reachable via hardcoded C# fallback branches in `PyOps.Repr`/`Str`/`RichEquals` (same output for
  normal `repr()`/`str()`/`==` use, so adding them was a transparent refactor there), but direct/
  unbound access (`object.__eq__`, `SomeClass.__eq__` when never overridden, `super().__repr__()`)
  raised `AttributeError` — a real gap, found via starlette's real `cls.__eq__ is object.__eq__`-
  style idiom (a common way real Python libraries detect whether a class defines custom equality).
- **A real recursion-depth guard, found chasing a regression from the fix above.** A corpus test
  already on file (`recursion.py`, already `Xfail`-listed for exactly this) does `Foo.__repr__ =
  Foo.__str__` on a `class Foo(object):` and expects `str(foo)` to raise a catchable
  `RecursionError` — real CPython's own protection against exactly this cycle. Before
  `object.__str__`/`__repr__` existed, `Foo.__str__` simply didn't resolve (`AttributeError`), so
  the corpus test "passed" by accident, for the wrong reason. Once real defaults existed, the cycle
  became reachable — and revealed a real, pre-existing gap: **nothing in this interpreter enforced
  any recursion limit at all**, so the cycle overflowed the real CLR stack instead of raising a
  catchable error. Fixed with a real depth counter in `Interp.Call` (matching CPython's default
  `sys.getrecursionlimit()` of 1000, thread-static like the existing frame stack, raising a real
  `PyErr.RecursionErrorClass`), *plus* running top-level script/REPL execution on a dedicated
  64 MB-stack thread (`Runtime.BigStack`, the same technique already used for coroutine/generator
  bodies) — needed because this tree-walking interpreter's own C# call chain is several frames deep
  per single Python-level call, so anything close to a 1000-deep Python recursion needs real
  headroom beyond the OS default 1 MB thread stack. `recursion.py` now genuinely passes and moved
  from `Xfail` to supported.
- 18 tests added (`M6_Stdlib/StdlibTests.cs`: `SubprocessTests`, `TempfileTests`,
  `TextIOWrapperTests`, `HttpTests`, `HtmlTests`, `TracebackTests`, `AsyncContextManagerTests`,
  `GenericResubscriptTests`, `EmailUtilsTests` — 9 new test classes;
  `M4_Functions/FunctionTests.cs`:
  `Runaway_recursion_raises_a_catchable_RecursionError`; `M4_Functions/ClassTests.cs`: 2 tests for
  the real `object` dunders). Full suite green throughout: 857/857 by the end of this round, up from
  836 at the start of it.

## Phase 4 — FastAPI itself + a real target app (placeholder)

- [ ] 4.1 `import fastapi` succeeds.
- [ ] 4.2 Write the first real target sample (mirrors scenario 1's/1b's "the script is the test
  bench"): a small real FastAPI app — path params, a pydantic request model, JSON response — run
  under PySharp with an ASGI server (starlette's own dev server, or a minimal one over the C#
  `socket` per ROADMAP 2e's fallback option if uvicorn itself doesn't port cleanly).
- [ ] 4.3 Verify with real HTTP requests (`curl`, matching scenario 1's `http_api.py`/`http_api_min.py`
  verification style), not just offline unit tests.

## Phase 5 — docs

- [ ] 5.1 ROADMAP.md: scenario 2 status flip to done (or partial, with a clear remaining-gap list),
  interpreter evolution log entries for every stdlib module/fix this plan lands.
- [ ] 5.2 RELEASE_NOTES.md + version bump (ask the author for the version number, same as
  AIOMQTT_PLAN.md's 7.2).
- [ ] 5.3 README.md "Verified scenarios and limits" update.

---

## Progress indicator

**Phase 0 and Phase 1 are both done.** `import pydantic` succeeds, verified beyond the bare import
(`from pydantic import BaseModel; print(BaseModel)` → `<class 'BaseModel'>`). Reached across several
long probe-driven sessions (2026-08-05/06 and continuations through 2026-08-07) — 759/759 tests
green, up from 635 before any of this started (~65 real gaps found and fixed, every one with its own
test). See 1.9 for the full blow-by-blow.

The two most consequential finds were both **real, generically important interpreter bugs**, not
pydantic-specific gaps: (1) `from pkg import name` silently replaced *any* fallback-import failure
with a misleading generic message, discarding the real cause; (2) `globals()`/`locals()` at true
module top level fell back to the *builtins* module's dict instead of the actual currently-executing
module's — meaning `globals()[...] = x` at module scope silently leaked into shared builtins
everywhere, for every module, always. A third, smaller one landed in the final round: `PyOps.PyEquals`
had no `PyByteArray` case at all, so `bytearray(b"x") == bytearray(b"x")` silently came back `False`.
All three were found by refusing to trust a first-glance error/result and either bisecting with a
minimal repro or manually verifying against known-correct values before trusting a new module — worth
repeating as the standing method note for whoever picks this up next.

Full new pieces built for real (not stubbed) along the way: **generic-alias tracking**
(`List[int]`/`Optional[int]`/`Pattern[str]`/etc. build a real `__origin__`/`__args__` object),
**`decimal.Decimal`**, **`complex`**, **`pathlib.Path`**, **`weakref`**, **`datetime`** (date/time/
datetime/timedelta/timezone), **`ipaddress`** (v4/v6 Address/Network/Interface, subclassing real
`_BaseAddress`/`_BaseNetwork` marker classes matching CPython's own hierarchy), **`re`** (a full
`System.Text.RegularExpressions`-backed engine), **`colorsys`**, **`pickle`** (round-trip-correct for
the common built-in types, own binary format), plus real (not stubbed) metaprogramming support the
`typing`/`types` modules previously lacked: the generalized `__mro_entries__` protocol (letting
`TypedDict` work as a base class), `types.new_class`/`resolve_bases`/`prepare_class`, and the 3-arg
`type(name, bases, namespace)` dynamic-class-creation call (previously only the 4-arg
`type.__new__(metaclass, ...)` unbound-method form existed).

**Phase 2's first real milestone is also done**: a `BaseModel` subclass now constructs, validates
real field types, raises real `ValidationError` on bad input, and serializes back out via `.dict()`.
Getting there required building real (simplified, not full-generality) **custom-metaclass support**
into `ExecClassDef` — the first PySharp scenario where "custom metaclasses are ignored" (a
deliberate, documented simplification up to this point) actually blocked something, since real
pydantic's `ModelMetaclass.__new__` must run while the `class User(BaseModel): ...` statement
executes to build `__config__`/`__fields__`/validators. `PyClass` gained a `Metaclass` field;
`ExecClassDef` now evaluates `metaclass=` (previously silently dropped) and calls the winning
metaclass's own `__new__` — subclasses inherit their base's metaclass without redeclaring it. Getting
an actual field to end up validated and stored on the instance needed one more real fix beyond the
metaclass plumbing itself: `object.__setattr__(obj, '__dict__', newdict)` (real pydantic's
`BaseModel.__init__` bulk-namespace-replace idiom) was silently setting a literal key named
`"__dict__"` instead of replacing the instance's whole namespace — the single most confusing bug of
the round, since `obj.__dict__` printed the *correct* merged contents (its own code path already
special-cased returning the instance dict directly) while `obj.x` failed, because the real per-key
writes never actually happened; found only by instrumenting attribute lookup itself with a raw
dict-hash/count dump, after source reading alone strongly (and wrongly) suggested the fix should
already have worked. Along the way: a **hang** (not a crash — an honest infinite loop, entered via
`ModelMetaclass.__new__` silently taking the wrong branch because a class's namespace never carried a
real `__module__`) was bisected via `Console.Error` trace prints around every metaclass build,
`issubclass`/`isinstance` were fixed to accept builtin types as either argument, `dict.keys()` became
a real (order-preserving) dict_keys-shaped view usable with the set operators, and `v.__class__` for
a builtin container/scalar became the real, constructible builtin type instead of a bare
non-constructible stand-in. Full blow-by-blow in Phase 2.2.1. 776/776 tests green (up from 759 at the
start of this round).

**Current known gap in Phase 2** (not a new architectural blocker): `BaseModel.dict()` leaks a
spurious `__fields_set__` key, because PySharp doesn't implement real `__slots__`-backed storage
separate from an instance's regular attribute dict (everything lives in the same `PyInstance.Dict`
today) — real pydantic relies on exactly that separation to keep `__fields_set__` out of
`self.__dict__`. A real, but distinctly-scoped gap (its own architectural decision, same category as
the metaclass work) — captured as `PydanticSmokeTests.Basemodel_dict_output_is_the_current_frontier`.

**Phase 3 is underway too** (started the same round, after the author's go-ahead to keep going): real
starlette 1.4.1 + its real `anyio` dependency (both from PyPI, unmodified) closed ~20 more real gaps
— new stdlib modules (`shlex`, `contextvars`, `importlib`, `textwrap`, `signal.Signals`), real
`urllib.parse.SplitResult`/`urlsplit`/`parse_qsl`, real `inspect` predicates
(`isfunction`/`iscoroutine`/etc.), real `threading.local` (genuine per-OS-thread storage), `io.IOBase`,
`socket.AddressFamily`/`SocketKind`, real `contextlib.ExitStack`/`AsyncExitStack`, and — the most
consequential fix of the round — **`Instantiate()` now calls a class's own real `__new__`**, not just
`__init__` (previously ignored entirely; a real gap for the `def __new__(cls, ...): return obj` idiom,
found via typing_extensions' real backported `TypeVar`). Also added real PEP 604 (`X | Y` union
operator between types) and PEP 585 (`tuple[int, str]` builtin-generic subscripting) support, both hit
as genuine *value expressions* (not just type-hint comments) in anyio's real source. Full blow-by-blow
in 3.1.1. 799/799 tests green (up from 776 at the start of this round).

**`match`/`case` structural pattern matching (PEP 634) is now done** (2026-08-08): real parser
(soft-keyword lookahead disambiguation, full pattern grammar) and interpreter (real matching semantics
for every pattern kind, not a stub) support landed, directly requested by the author once this was
identified as Phase 3's frontier. Verified against the exact real anyio statement that originally
blocked this phase (`match self.status: case TaskHandle.Status.PENDING: ...`), plus a small
`typing.Never` fix found immediately after. Full blow-by-blow in 3.1.2. 825/825 tests green (up from
799 at the start of this round).

**6 more real gaps closed past `match`/`case`** (same session, continued probing): real
`concurrent.futures.Future` (a genuinely new thread-safe future distinct from asyncio's cooperative
one, backed by a real .NET `Monitor` — not a stub), `stat` (`S_IF*`/`S_IS*`, ported from CPython's
own `Lib/stat.py`), real `os.chmod` (verified end to end against an actual file), real
`abc.ABC.register()` virtual-subclass support (`PyClass` gained a `VirtualSubclasses` registry
consulted by `IsSubclassOf`), a `typing.Generic[T]` MRO-entries de-duplication fix (a real,
previously-latent bug: `class Foo(Generic[T], SomeGeneric[T])` raised "Cannot create a consistent
MRO" whenever `SomeGeneric` already derived `Generic` — recurs throughout anyio's own
`abc/_streams.py` hierarchy, not a one-off), and `typing.override` (PEP 698, a real `__override__`
side effect, not a bare passthrough). Full blow-by-blow in 3.1.3. 836/836 tests green (up from 825).

**12 more real gaps closed past `subprocess`** (same continuation, "match/case parte 2" →
"prosegui"): real `subprocess` (`Popen` on `System.Diagnostics.Process`, `run`/`call`/
`check_call`/`check_output`), real `tempfile` (files/directories genuinely on disk), `io
.TextIOWrapper` (a real duck-typed text wrapper over any binary buffer), `http.HTTPStatus` (a real
IntEnum with real `.phrase`) and `http.cookies` (a real, if simplified, port of CPython's own
`Lib/http/cookies.py`), `email.utils` (real RFC 2822 date helpers), a real bug fix in
`re.Pattern.search`/`match`/`fullmatch`/`finditer` (`pos`/`endpos` were silently ignored entirely —
found via an infinite loop in a hand-ported `http.cookies._unquote`), real generic-alias
re-subscription with TypeVar substitution (`SomeAlias[T][Concrete]`, not supported at all before),
`html` (real `escape`/`unescape`), `traceback.format_exc`/`sys.exc_info()` (backed by a real
interpreter-level change: `Interp`'s exception-handling stack now tracks the full `PyRaise`, not
just the instance), `contextlib.asynccontextmanager` (works for real at decoration time; entering it
correctly raises `NotImplementedError` since PySharp has no async generators), real `object.__eq__`/
`__ne__`/`__hash__`/`__repr__`/`__str__` defaults (previously only reachable via hardcoded fallback
branches, not as real inheritable methods), and — found chasing a regression from that last one — a
**real recursion-depth guard**, since this interpreter had never enforced any recursion limit at
all: runaway recursion now raises a catchable `RecursionError` (matching CPython's default 1000),
backed by running top-level execution on a real 64MB-stack thread since this tree-walking
interpreter needs real headroom for that depth. Full blow-by-blow in 3.1.4. 857/857 tests green (up
from 836).

**Current frontier for Phase 3**: `mimetypes` — no module named `mimetypes` at all. Found via
starlette's real `staticfiles.py`. Not started.

Phase 4 remains a placeholder (see architecture decisions) until Phase 3 is scoped further from real
probing.

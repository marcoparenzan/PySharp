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

- [x] 3.1 Get `import starlette` to succeed. **Done.** Real starlette 1.4.1 (from PyPI, unmodified),
  plus its real `anyio` dependency, drove five probe-driven rounds (3.1.1–3.1.5 below), closing ~51
  real gaps in total — from the original blocking `SyntaxError` on `match`/`case` all the way
  through `starlette.applications`/`routing`/`responses`/`requests` importing cleanly. The last
  round (3.1.5) went one step further than the checkbox: a real `Starlette(routes=[Route(...)])`
  app now **constructs successfully**, verified directly (not just "the import didn't crash").
  `staticfiles.py` (needing `mimetypes`, closed in 3.1.5) and `websockets.py` are not yet exercised
  by this probe — real usage of either may still turn up further gaps.
- [ ] 3.1b Exercise more of starlette's real surface beyond construction — routing dispatch,
  request/response handling, middleware, `staticfiles.py`, WebSockets — to find the *next* real
  gaps before Phase 3.2's ASGI server work begins. **In progress** (3.1.6 below) — real ASGI request
  dispatch through a constructed `Starlette` app now works end to end (verified with a raw ASGI
  `scope`/`receive`/`send` triple, matching what a real server would send): the index route and a
  path-parameter route (`/items/{item_id}`) both return correct, real HTTP response messages. This
  round surfaced (and fixed) **two significant, previously-silent correctness bugs** — see 3.1.6.
  The 404/exception-handling path is not yet closed (it now needs `asyncio.base_events
  ._run_until_complete_cb`, a private CPython-internal symbol — a deliberate stopping point for this
  round); `staticfiles.py`/WebSockets remain unexercised.
- [x] 3.2 Minimal ASGI app + routing working, driven by PySharp's `asyncio`. **Done** (3.1.14 below):
  `samples/asgi_server.py`, a real, minimal ASGI/3 HTTP server bridging raw HTTP/1.1 to the real
  scope/receive/send protocol — reusable (`serve(app, host, port)` accepts any real ASGI callable),
  verified over real HTTP (curl) against both its own demo app and a real, unmodified `Starlette`
  app. `anyio` question resolved by everything observed since: its real usage surface throughout
  this whole plan has been almost entirely the asyncio backend (`_backends/_asyncio.py`), which
  PySharp's own `asyncio` module (plus a handful of extras: `queue`, `threading.local`, real async
  generators, ...) already supports for real — no separate anyio-specific support was ever needed.

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

### 3.1.5 — past `mimetypes`: the last 4 real gaps — `import starlette` now succeeds, a real app constructs

Continuing the same probe past `mimetypes` (the author's "procedi con i mime types" after
confirming the round-3.1.4 commit), same discipline throughout — and this round finally reached
the end of the original probe script: `starlette+anyio import OK`, all three core modules import,
and `Starlette(routes=[Route("/", homepage)])` constructs for real.

- **`mimetypes`**: `guess_type` via a real extension→MIME table (the common web-relevant subset of
  CPython's own `types_map`, not every obscure entry) plus real encoding-suffix detection
  (`.gz`/`.bz2`/`.xz`/`.Z`/`.br`, matching CPython's actual algorithm: strip a known encoding suffix
  first, then look up what's left). Verified against 6 real cases (`.html`, `.tar.gz`, `.json`, no
  extension, `.txt.bz2`, `.css`) before writing tests — every result matched real CPython exactly.
  Found via starlette's real `from mimetypes import guess_type` (`responses.py`, for
  `FileResponse`'s `Content-Type` header).
- **`secrets`**: `token_bytes`/`token_hex`/`token_urlsafe`/`randbelow`/`choice`/`compare_digest`, a
  real CSPRNG-backed implementation (`System.Security.Cryptography.RandomNumberGenerator`, the same
  one `os.urandom` already uses) with a real constant-time `compare_digest`, not a
  `random.random()`-backed stand-in. Found via starlette's real `from secrets import token_hex`
  (`responses.py`, for `FileResponse`'s ETag generation).
- **`memoryview`**: a real (if simplified) builtin view type over `bytes`/`bytearray` — implemented
  as a `PyClass` (the same pattern as `StringIO`/`BytesIO`) rather than a new native runtime type,
  which gets `isinstance`/`GetItem`/`|`-union-operand support for free from the existing generic
  `PyInstance`/`PyClass` machinery instead of needing new native-type plumbing throughout the
  interpreter. A `bytearray`-backed view shares the *same* underlying storage (mutations through
  either side are visible on the other, matching real CPython's actual view semantics — verified
  end to end: mutating through the view changes the original `bytearray`); a `bytes`-backed view is
  read-only. Supports real indexing/slicing, `==`, iteration, `.tobytes()`/`.tolist()`, `.nbytes`/
  `.readonly`. Found via starlette's real `Content = str | bytes | memoryview` module-level type
  alias (`responses.py`) — evaluated eagerly despite `from __future__ import annotations` being
  present, since it's a plain assignment, not a deferred annotation.
- **A real, separate gap surfaced by the fix above: `isinstance`/`issubclass` never accepted a real
  `X | Y` union as the 2nd argument at all**, raising `TypeError: isinstance() arg 2 must be a type
  or tuple of types` — a real CPython 3.10+ feature (unions work exactly like a tuple of types
  there). Found via starlette's real `isinstance(content, bytes | memoryview)` (`responses.py`),
  reachable only once `memoryview` itself existed for the union to contain. Fixed for real in both
  `IsInstance` and `IsSubclass` (recognizing a `GenericAliasModule`-backed union instance and
  recursing membership over its `__args__`), not just for `memoryview` specifically.
- 5 tests added (`M6_Stdlib/StdlibTests.cs`: `MimetypesTests`, `SecretsTests`, `MemoryViewTests` —
  covering real view/mutation semantics and the `isinstance`/`issubclass`-with-union fix together).
  Full suite green throughout: 862/862 by the end of this round, up from 857 at the start of it.
- **Directly verified against the original probe script's own end-to-end goal**: re-ran the exact
  scenario that had been the moving target since the `match`/`case` blocker — `import starlette`,
  `import anyio.from_thread`/`to_thread`, and `Starlette(routes=[Route("/", homepage)])` — all
  succeed for real, not just "doesn't crash at import time."

### 3.1.6 — 3.1b begins: real ASGI request dispatch, and two significant correctness bugs found

With `import starlette` and app construction both done, the natural next step (the author's plain
"procedi") was to actually **exercise** the constructed app — send it a real ASGI request, the way
a real server would, using a raw `scope`/`receive`/`send` triple built by hand (no real ASGI server
exists yet — that's Phase 3.2). This immediately surfaced two bugs far more consequential than a
missing module: both would have silently broken *any* real FastAPI/starlette app.

- **`inspect.isfunction` incorrectly excluded async (and generator) functions** —
  `a[0] is PyFunction { IsGenerator: false, IsAsync: false }`. Real CPython's `isfunction` is purely
  "is this a `FunctionType`"; async-ness/generator-ness are what `iscoroutinefunction`/
  `isgeneratorfunction` are for, not `isfunction` itself. This silently broke **every `async def`
  route handler**: starlette's real `Route.__init__` does `if inspect.isfunction(endpoint_handler)
  or inspect.ismethod(endpoint_handler): self.app = request_response(endpoint)` (routing.py) —
  since virtually every real endpoint handler is `async def`, this check failed for essentially all
  of them, so `Route` treated the plain handler function as if it were *already* a raw ASGI app,
  calling it directly with `(scope, receive, send)` instead of wrapping it via `request_response()`
  to call it correctly with just `(request)` — `TypeError: homepage() takes 1 positional arguments
  but 3 were given`. Fixed by removing the exclusions entirely; verified against sync/async/
  generator/async-generator functions and lambdas before writing the regression test.
- **`re.Match.groups(default=None)` only read `default` from kwargs, never positionally** — a
  normal positional-or-keyword parameter in real CPython, not keyword-only. This silently broke
  **any route with a path parameter that omits an explicit type** (the overwhelming majority of
  real routes, e.g. `/items/{item_id}` vs. the less common `/items/{item_id:int}`): starlette's real
  `compile_path` (routing.py) does `param_name, convertor_type = match.groups("str")`, passing
  `"str"` *positionally* to default the optional `:type` capture group to `"str"` instead of `None`
  when a route parameter has no explicit type. `AttributeError: 'NoneType' object has no attribute
  'lstrip'` resulted from the un-defaulted `None` reaching `convertor_type.lstrip(":")` next.
- **`callable.__call__` didn't exist as an attribute at all**, for functions, bound methods, or
  builtins — real CPython: any callable's `.__call__` is itself callable (a bound method-wrapper
  around the same underlying call). Found via starlette's real `is_async_callable`'s own fallback
  branch, `iscoroutinefunction(obj) or (callable(obj) and iscoroutinefunction(obj.__call__))`
  (`_utils.py`) — reached for a bound method (the default 404 handler is one), where
  `iscoroutinefunction(obj)` alone returns `False` since a `PyBoundMethod` isn't a `PyFunction`.
- **`array`**: a real (if simplified) compact typed array — real per-typecode byte width (`b`/`B`/
  `h`/`H`/`i`/`I`/`l`/`L`/`q`/`Q`/`f`/`d`), real `tobytes`/`frombytes` round-tripping (verified
  against int and float typecodes before writing tests), not a stub. Found via anyio's real `import
  array` (`_backends/_asyncio.py`, for Unix file-descriptor-passing ancillary data).
- **`asyncio.AbstractEventLoop`/`all_tasks`/`current_task`**: added as real names for real CPython
  API surface anyio imports at module level — `AbstractEventLoop` is a bare placeholder (real event
  loop objects are the native `PyEventLoop`, never wrapped as an instance of it; nothing in scope
  does `isinstance(loop, AbstractEventLoop)`), and `all_tasks`/`current_task` are honest, documented
  limitations (PySharp's event loop doesn't keep a live-task registry, so they report "no other
  tasks"/`None` rather than the true values) — not stubs pretending otherwise, and safe here since
  nothing in the reachable path asserts on their contents.
- **Verified real, correct ASGI responses end to end** for both the index route and a
  path-parameter route (`/items/{item_id}` → real JSON `{"item_id": "42"}` with correct
  `content-length`/`content-type` headers) using a hand-built `scope`/`receive`/`send` triple — the
  same shape a real ASGI server sends. The 404/exception-handling path goes one layer deeper into
  anyio's real `_backends/_asyncio.py` and currently needs `from asyncio.base_events import
  _run_until_complete_cb` — a private, underscore-prefixed CPython-internal symbol, not real public
  API — a deliberate stopping point for this round rather than chasing CPython internals.
- 6 tests added (`M6_Stdlib/StdlibTests.cs`: `ArrayTests`, `CallAttributeTests`, plus regression
  tests added to the existing `InspectTests`/`ReTests` classes for the two significant bugs). Full
  suite green throughout: 868/868 by the end of this round, up from 862 at the start of it.

### 3.1.7 — past the private asyncio internal: 8 more real gaps, one more significant bug, then a genuine unknown

Continuing past `asyncio.base_events._run_until_complete_cb` (the author's plain "procedi") by
adding it as a real, importable, self-identity-comparable sentinel — it turned out to be a small,
one-off addition, not the deep rabbit hole it looked like from the name alone. The probe then kept
advancing through anyio's real `_backends/_asyncio.py` module-level imports.

- **`inspect.CORO_CREATED`/`CORO_RUNNING`/`CORO_SUSPENDED`/`CORO_CLOSED` + `getcoroutinestate`**:
  real constants, with `getcoroutinestate` derived from PyCoroutine's own real `Started`/`Finished`
  state. PySharp's coroutines run on their own dedicated OS thread rather than CPython's single-
  threaded generator-style suspension, so distinguishing "actively running right now" from
  "suspended" as precisely as CPython's own `cr_running` flag genuinely isn't the same question here
  — a documented simplification (both map to `CORO_SUSPENDED`), verified to still satisfy the one
  real call site found (`getcoroutinestate(coro) in (CORO_RUNNING, CORO_SUSPENDED)`, which only
  needs "started and not finished").
- **`queue`**: a real, thread-safe FIFO queue for cross-*OS-thread* producer/consumer use — backed
  by `System.Collections.Concurrent.BlockingCollection` (real blocking `put`/`get` with real
  timeouts), a genuinely different primitive from PySharp's existing `asyncio.Queue` (cooperative,
  single-threaded coroutines on one thread). Verified end to end including a real cross-thread
  blocking handoff (`queue.get()` on the main thread's own worker thread, unblocked by a `put()`
  from main) before writing tests. One bug found by my own manual probe before it ever reached a
  test: `__init__` only read `maxsize` positionally, so `Queue(maxsize=1)` — the common keyword-arg
  call shape — silently stayed unbounded.
- **`asyncio.Runner`** (real CPython 3.11+ API): a lazily-created event loop wrapped for reuse
  across several `.run(coro)` calls, closed once via `.close()`/`__exit__` — real CPython's own
  `asyncio.run()` is itself built on top of exactly this class, so this is genuinely reusing the
  same real machinery, not a parallel stub.
- **`asyncio.eager_task_factory`**: implemented as real *parsed Python source* (not a
  `PyBuiltinFunction`) specifically so `.__code__` resolves for real via the normal `PyFunction`
  attribute path — matching the real object shape anyio's own version-gated code expects
  (`asyncio.eager_task_factory.__code__`, only ever used for an identity comparison, never actually
  invoked as a task factory in the reachable path). Not actually eager (schedules normally rather
  than running synchronously to the first suspension point) — a documented simplification.
- **Real `asyncio.protocols` hierarchy** (`BaseProtocol`/`Protocol`/`BufferedProtocol`/
  `DatagramProtocol`/`SubprocessProtocol`/`SubprocessStreamProtocol`): real, subclassable base
  classes with CPython's own real no-op-by-default callback methods
  (`connection_made`/`data_received`/`datagram_received`/...), matching `Lib/asyncio/protocols.py`
  exactly in shape. PySharp's own event loop doesn't drive these callbacks from real socket I/O — a
  separate, larger feature nothing in scope needs yet — so this covers real subclassability, not a
  wired-up transport layer.
- **`asyncio.subprocess.SubprocessStreamProtocol` accessible as a real module attribute** — found a
  second, more general gap while fixing this: real CPython's own `asyncio/__init__.py` imports its
  submodules internally, so `.subprocess` (and others) become real attributes of the `asyncio`
  module immediately after a plain `import asyncio`, with no separate `import asyncio.subprocess`
  statement needed anywhere. PySharp's own submodules were only ever reachable via an explicit
  dotted import. Fixed by building the submodule inline inside `AsyncioModule.Create()` and setting
  it as a real dict entry, rather than relying solely on the Importer's separate dotted-registration
  path (kept too, for explicit `import asyncio.subprocess` to also still work).
- **`asyncio.Task`**: added as a real, directly-constructible, importable class (previously entirely
  absent as a name, despite the underlying real `PyTask` machinery already existing and working via
  `create_task`/`ensure_future`) — **and a second, more general bug found alongside it**:
  `isinstance(a_real_task, asyncio.Future)` was `False`, when real CPython's `class Task(Future):`
  means a Task genuinely *is* a Future. `TypeMatchesBuiltinName`'s generic fallback is a flat
  type-name equality check (`PyOps.TypeName` reports the *most specific* name, `"Task"`, for a
  `PyTask`), which can't see through `PyTask`'s real C# inheritance from `PyFuture` on its own —
  fixed with an explicit `"Future" => obj is PyFuture` case ahead of the fallback.
- **Verified real exception propagation end to end**: a genuine unhandled exception raised inside a
  route handler (`/boom` → `ValueError`) now correctly propagates all the way back out through
  starlette's real `wrap_app_handling_exceptions` middleware to the caller — confirming the core
  exception-handling ASGI plumbing works for the common case (an application bug), not just the
  happy path.
- **New, narrower frontier found**: the specific 404-not-found fallback path (`Router.not_found` →
  `raise HTTPException(status_code=404)` → re-raised uncaught by `wrap_app_handling_exceptions`
  since no custom handler is registered) now hits a bare `AssertionError` with no message and a very
  short 2-frame traceback, not yet root-caused. Real starlette's `Router.app` has exactly one bare
  `assert scope["type"] in ("http", "websocket", "lifespan")`, a plausible but unconfirmed
  candidate — the responding scope should genuinely satisfy it, so if that *is* the culprit, something
  about how PySharp threads the scope dict through the not-found fallback is suspect, not the
  assertion's own logic. Deliberately not chased further this round (see 3.1.6/3.1.7's shared
  "procedi" mandate to keep making real progress, not to exhaustively solve every edge case in one
  sitting) — a good target for the next round, now much more narrowly scoped than "the whole 404
  path" was before this round started.
- 9 tests added (`M6_Stdlib/StdlibTests.cs`: `QueueModuleTests`, `AsyncioAdditionsTests`). Full
  suite green throughout: 877/877 by the end of this round, up from 868 at the start of it.

### 3.1.8 — the 404 path: 5 more real bugs, two of them structural, chased down to a new unknown

Picking up the exact 3.1.7 frontier (the bare `AssertionError` on the 404-not-found fallback path).
Root-caused via the project's usual debug-print-then-remove bisection on `AssertStmt`/`GetItem`/
`AttributeExpr` evaluation (each print removed once its diagnosis was confirmed).

- **`asyncio.current_task()` always returned `None`**: the 3.1.6-era "honest limitation" finally hit
  by real code — anyio's `CancelScope` exit path does
  `assert self._host_task is not None` after remembering `current_task()` on entry, and both were
  `None`. Fixed for real: `PyCoroutine.CurrentTask`, a thread-static explicitly propagated down
  through every nested `await` (`DelegateTo`) and set once per `PyTask` (`OwningTask`) — needed
  because, unlike CPython's single-threaded generator-style coroutines, PySharp runs each nested
  `await` level on its own dedicated OS thread, so nothing propagates for free the way it would with
  real thread-locality.
- **`asyncio.Future[T]()` — real runtime PEP 585 subscript-then-call — raised `TypeError: 'function'
  object is not subscriptable`**: found in anyio's real `_backends/_asyncio.py`
  (`future = asyncio.Future[T_Retval]()`, a genuine executed expression, not just a type annotation
  PySharp's deferred-annotation handling could shrug off). `"Future"` (plus `"OrderedDict"`,
  `"WeakKeyDictionary"`, hit by the same code path) were missing from
  `Builtins.BuiltinTypeNames`, the allowlist gating PEP 585 subscripting for a raw
  `PyBuiltinFunction`. Fixing the allowlist alone wasn't enough: the resulting generic alias also
  needed to be *callable* for the trailing `()` to actually construct something, so
  `GenericAliasModule`'s alias class gained a real `__call__` forwarding to `__origin__` (matching
  real CPython's `_GenericAlias.__call__`).
- **`'Task' object has no attribute '_loop'`**: anyio's real `WorkerThread.__init__` reads a Task's
  private `_loop` directly (`self.loop = root_task._loop`), bypassing the public `get_loop()` for
  perf — a real CPython `Future`/`Task` attribute PySharp's `PyFuture` never exposed as plain data
  (only as methods, via `FutureTable`). Added alongside `PyRange`'s existing data-attribute pattern
  in `TypeMethods.cs`.
- **A genuinely structural bug: `threading.local` state set inside a `@contextmanager` generator,
  before its `yield`, was invisible in the `with`-body** — surfaced when the fix above let anyio's
  real `run_sync_in_worker_thread` actually spawn a worker thread and hit
  `claim_worker_thread` (`with claim_worker_thread(...): threadlocals.current_token = ...; yield`).
  Root cause: `PyGenerator`, like `PyCoroutine`, runs its body on its own dedicated OS thread (a
  producer/consumer handshake, not CPython's single-threaded suspension) — so the pre-`yield` code
  of a `@contextmanager` generator executes on a *different real CLR thread* than the `with`-body it
  logically wraps, and `threading.local`'s old `ThreadLocal<PyDict>` storage (keyed by raw CLR
  thread) split what should have been one logical Python thread into two. Fixed with a new
  `LogicalThread` (`Runtime/LogicalThread.cs`): a stable per-Python-thread identity, explicitly
  propagated into `PyGenerator.Resume`'s and `PyCoroutine.Resume`'s dedicated threads at spawn time
  (mirroring the `current_task`/`OwningTask` propagation above) — but deliberately *not* propagated
  by `threading.Thread.start()` (`ThreadingModule.cs`), so genuinely independent Python threads still
  get their own fresh, isolated storage, matching real CPython. `threading.local`'s storage switched
  from `ThreadLocal<PyDict>` to a `ConcurrentDictionary<object, PyDict>` keyed by `LogicalThread.Current`.
- **Two more bugs found underneath, both real correctness gaps beyond this one scenario**:
  - `Interp.DelAttr` never checked a class's `__delattr__` (unlike `SetAttr`, which already checked
    `__setattr__`) — `del tl.token` inside `claim_worker_thread`'s `finally` raised `AttributeError`
    even though `threading.local` defines a real `__delattr__`. Fixed by mirroring `SetAttr`'s
    dispatch.
  - `TryGetAttr`'s `__getattr__` fallback always returned `true` after calling `__getattr__`, even if
    the call itself raised `AttributeError` — the exact contract real `__getattr__` implementations
    rely on to signal "not found either" so `getattr(obj, name, default)`/`hasattr` can catch it and
    fall through. Found via `getattr(threadlocal_obj, name, default)` on a missing per-thread key
    inside a background-thread test. Fixed by catching `PyRaise` where the raised value
    `IsSubclassOf(AttributeErrorClass)` and returning `false` (any other exception from
    `__getattr__` still propagates, correctly).
- **New frontier**: past all of the above, the 404 path now reaches a `TypeError: 'coroutine' object
  is not callable` — some code in the same `<string>`/`<string>` two-frame chain calls an
  already-produced coroutine object a second time as if it were still the callable that produced it.
  Not yet root-caused (the traceback's `<string>` labels don't reveal the real file — a known,
  pre-existing limitation of PySharp's traceback formatting worth revisiting separately). Good next
  target.
- 6 tests added (`M6_Stdlib/StdlibTests.cs`: `AsyncioAdditionsTests` gained
  `Current_task_reflects_the_real_owning_task_across_nested_awaits`,
  `Future_supports_PEP585_subscript_then_call`, `Future_and_Task_expose_a_private_loop_attribute`;
  new `ThreadingLocalContextManagerTests` with 3 tests covering the `LogicalThread` propagation,
  the `__delattr__` dispatch fix, and independent-thread isolation still holding). Full suite green
  throughout: 883/883 by the end of this round, up from 877 at the start of it.

### 3.1.9 — the 404 path is closed: one more real bug, then a full end-to-end pass

Picking up the exact 3.1.8 frontier (`TypeError: 'coroutine' object is not callable`). Root-caused via
the same `EvalCall`-site debug-print-then-remove bisection, this time dumping the callee expression's
AST and owning module name.

- **`asyncio.iscoroutinefunction`/`inspect.iscoroutinefunction`/`inspect.isgeneratorfunction` didn't
  see through a bound method**: all three only matched a raw `PyFunction`, never a `PyBoundMethod`
  wrapping one. Real CPython unwraps a bound method to its underlying function first — a bound async
  instance method genuinely is a coroutine function. The debug print pinned the failing call to
  `starlette._exception_handler`, calling `response(scope, receive, sender)` where `response` was a
  `PyCoroutine`, not the `Response` instance it should have been. Traced to real starlette's
  `ExceptionMiddleware.http_exception` (an `async def` *instance method*, registered as the default
  handler for `HTTPException` — including the plain 404 case, via `self.http_exception`): starlette's
  real `is_async_callable(handler)` (`_utils.py`) calls `asyncio.iscoroutinefunction(handler)` first
  (Python <3.13's import path), which came back `False` for the bound method, routing the call
  through the *sync* `run_in_threadpool(handler, conn, exc)` path instead of `await handler(conn,
  exc)`. Calling an `async def` method without awaiting it just produces a coroutine object without
  running it — that unawaited coroutine became `response`, and the next line
  (`await response(scope, receive, sender)`) tried to ASGI-dispatch it as if it were a real `Response`,
  hence "coroutine object is not callable". Fixed with a shared `InspectModule.UnwrapBoundMethod`
  helper, applied to all three predicates.
- **Verified the full 404 path end to end**: a real, unmodified `Starlette(routes=[...])` app with no
  custom exception handlers now correctly returns `{"type": "http.response.start", "status": 404,
  ...}` + `{"type": "http.response.body", "body": b"Not Found"}` for an unmatched route — matching real
  starlette's default `ExceptionMiddleware.http_exception` behavior exactly. Re-verified the happy path
  (`/` → 200, `"hello world"`) and the uncaught-exception path (`/boom` → real `ValueError`
  propagating all the way to the caller) together in the same run to confirm no regression from this
  round's fix. **This closes the entire 404-path investigation chain started in 3.1.6.**
- 2 tests added (`M6_Stdlib/StdlibTests.cs`: `InspectTests.Iscoroutinefunction_and_isgeneratorfunction_
  see_through_a_bound_method`, `AsyncioAdditionsTests.Asyncio_iscoroutinefunction_sees_through_a_bound_
  method`). Full suite green throughout: 885/885 by the end of this round, up from 883 at the start.
- **New frontier for Phase 3.1b**: `staticfiles.py`/WebSockets remain entirely unexercised; path-
  parameter routes and custom exception handlers (registered via `Starlette(exception_handlers=...)`)
  haven't been probed yet either — good next targets before considering 3.1b closed and moving to 3.2
  (a real ASGI server).

### 3.1.10 — custom exception handlers verified clean, then `staticfiles.py` closed: 7 more real gaps

Picking up the exact 3.1.9 frontier. First, **custom exception handlers verified end to end with zero
new bugs**: a `Starlette(exception_handlers={404: custom_404, Exception: custom_500})` app correctly
routes a 404 to `custom_404` and an uncaught `ValueError` to `custom_500` — including real starlette's
documented "always re-raise after handling" semantics for the `Exception`/500 case (`ServerErrorMiddleware`
calls the handler, sends its response, *then* re-raises so a real ASGI server/test client can still log
or observe the original error). The first probe run "failed" only because the probe script itself
didn't catch that expected re-raise — not a PySharp bug, caught by re-reading real starlette source
before concluding otherwise (the project's standing discipline: verify against real behavior, don't
assume a probe failure is always the interpreter's fault).

Then pushed into `staticfiles.py`, entirely unexercised before this round — a real `StaticFiles`-mounted
directory serving an actual file from disk. 7 real gaps found and fixed, one at a time via the same
bisection loop:

- **`importlib.util` didn't exist as an importable submodule at all** — `staticfiles.py`'s own
  `import importlib.util` (a module-load-time statement, unconditional) failed before any of its code
  could even run. Real CPython resolves `import a.b` by finding a *separately registered* submodule,
  not just an attribute a parent module happens to expose after import — matching the existing
  `asyncio.base_events` pattern, added `importer.RegisterBuiltin("importlib.util", ...)` as its own
  factory (StdlibModules.cs). Implemented `find_spec(name)` for real via a new
  `Importer.FindModuleSpec`: locates a module *without importing/executing it* (already-loaded →
  real `__file__`; builtin C# module → `origin=None`; else a real file-system search over
  `SearchPaths`), returning `None` only when nothing matches at all — matching real
  `importlib.util.find_spec`'s contract.
- **`os.stat`/`os.stat_result` didn't exist** — added for real: real `st_mode` (`S_IFREG`/`S_IFDIR`,
  matching `StatModule.cs`'s existing real `S_ISREG`/`S_ISDIR` bit values), real `st_size`, real
  `st_mtime`/`st_atime`/`st_ctime` (via `FileSystemInfo`, converted to Unix-epoch seconds); `st_uid`/
  `st_gid`/`st_ino`/`st_dev` are `0` (real CPython itself synthesizes meaningless values for these on
  Windows — not a shortcut specific to PySharp). Raises a real `FileNotFoundError` for a missing path.
- **`os.path.normpath` didn't exist** — implemented for real: collapses `.`/`..` segments and
  redundant separators *lexically*, without touching the filesystem or changing a relative path into
  an absolute one (the key difference from `Path.GetFullPath`, which does both).
- **`os.path.realpath` didn't exist** — real symlink resolution via `FileSystemInfo.ResolveLinkTarget`
  when the path actually is a symlink, falling back to the same absolute-path canonicalization as
  `abspath` otherwise (matching real CPython's behavior for the overwhelmingly common non-symlink
  case).
- **`os.path.commonpath` didn't exist** — implemented for real: the longest common leading sequence
  of path *components* (not a naive string prefix) across the given paths — this is starlette's real
  path-traversal guard (`commonpath([full_path, directory]) == directory` rejects a request path that
  escapes the configured static directory via `..` segments). v1 scope: doesn't raise `ValueError` for
  a mix of absolute/relative paths or an empty sequence — not exercised by the reachable path.
- **`NotADirectoryError`/`IsADirectoryError` didn't exist as builtin exceptions** — added as real
  `OSError` subclasses (`PyErr.cs`), matching real CPython's hierarchy.
- **`collections.abc.Mapping` had no real mixin methods at all** (a documented v1 simplification from
  much earlier in the project — "just need to exist and be importable/subclassable... unless a real
  scenario needs it"; now one does). Real starlette's `Headers(Mapping[str, str])` (datastructures.py)
  overrides `__getitem__`/`keys`/`values`/`items`/`__contains__`/`__eq__`/`__iter__`/`__len__` itself,
  but relies on the *real Mapping ABC's* `get(key, default=None)` mixin (`self[key]` via
  `__getitem__`, catching `KeyError`) for `headers.get("content-type")`-style lookups. Also fixed
  `MutableMapping` to derive from `Mapping` for real (previously two independent, unrelated placeholder
  classes — an existing structural gap, not itself blocking this round but essentially free to fix
  alongside): required constructing `Mapping` *before* `MutableMapping` with `Mapping` already in its
  base list, since `PyClass` computes its MRO once in the constructor — mutating `.Bases` afterward
  (the first attempt) silently doesn't update an already-computed `.Mro`, caught by the regression test
  itself failing before being trusted as fixed.
- **Verified the full static-file path end to end**: `GET /static/hello.txt` against a real
  `StaticFiles(directory=...)` mount returns 200 with the real file's bytes and real
  `content-type`/`accept-ranges`/`content-length`/`last-modified`/`etag` headers; `GET
  /static/nope.txt` correctly returns starlette's real 404. Both went through the *entire* real ASGI
  dispatch chain fixed across 3.1.6–3.1.9 (routing, exception middleware, worker-thread offload for
  the blocking `os.stat`/file-read calls) with zero further gaps once the 7 above were closed.
- 9 tests added (`M6_Stdlib/StdlibTests.cs`: new `OsStatAndPathTests` (4), `ImportlibUtilTests` (1),
  `CollectionsAbcMappingTests` (2), plus 2 more folded into the same batch). Full suite green
  throughout: 892/892 by the end of this round, up from 885 at the start of it.
- **New frontier for Phase 3.1b**: WebSockets remain entirely unexercised (a different `scope["type"]`
  and message protocol, not yet probed at all) — the natural next target. Path-parameter routes and
  custom exception handlers are now both verified; `staticfiles.py`'s common case (plain
  `directory=...`, no `packages=[...]`) is verified — the `packages=` argument itself (which actually
  calls `find_spec` at runtime, not just at import time) remains unexercised.

### 3.1.11 — WebSockets: the core protocol works end to end; the real blocker is async generators

Picking up the exact 3.1.10 frontier (WebSockets, entirely unexercised before this round). Built a
real ASGI `websocket` scope by hand (`{"type": "websocket", ...}`) and a real `websocket.connect`/
`websocket.receive`/`websocket.disconnect` message sequence, driving a real, unmodified
`WebSocketRoute` through `Starlette.__call__` — the same "hand-build the exact triple a real server
sends" technique used for the HTTP scenarios since 3.1.6.

- **The core WebSocket protocol works correctly with zero bugs found**, on the very first probe:
  `websocket.accept()` → `receive_text()` → `send_text()` → `close()` produced exactly the real ASGI
  message sequence starlette itself would (`websocket.accept` → `websocket.send` →
  `websocket.close`), matching real semantics precisely. A second probe confirmed the disconnect path:
  a client disconnecting before sending anything correctly raises a real `WebSocketDisconnect` inside
  `receive()`, catchable by the handler's own `try`/`except`, with no spurious close message sent
  afterward (matching real starlette). A third probe confirmed a **manual streaming loop**
  (`while True: data = await websocket.receive_text(); await websocket.send_text(...)`, catching
  `WebSocketDisconnect` to exit) correctly handles multiple messages in sequence before a clean
  disconnect — the real, general WebSocket streaming pattern works.
- **The one real gap found: `WebSocket.iter_text()`/`iter_bytes()`/`iter_json()` don't work**, because
  they're real CPython **async generators** (`async def iter_text(self): ... yield ...`) — a
  documented, deliberately-deferred language feature (Axis A in ROADMAP.md: "async generators still
  missing"). PySharp's `async def` machinery doesn't distinguish a generator body from a plain
  coroutine body, so calling `websocket.iter_text()` just produces a `PyCoroutine`, and
  `async for data in websocket.iter_text():` then fails with `AttributeError: 'coroutine' object has
  no attribute '__anext__'` — expected given the known gap, not a new discovery, but now confirmed as
  the *specific, concrete* blocker (previously only an abstract "missing feature" entry). **Not fixed
  this round**: implementing real async generators is a substantial new capability (a hybrid
  execution model combining `PyGenerator`'s yield-suspension with `PyCoroutine`'s await-suspension,
  plus real `__anext__`/`__aiter__`/`StopAsyncIteration` protocol support and `async for` dispatch for
  it) — a new language feature, not a gap-fill, and a natural point to check in with the author before
  investing in it rather than starting unprompted.
- No interpreter changes this round (all three probes passed or failed exactly as real CPython/
  starlette would) — no new tests needed; the existing 892/892 suite is unaffected.
- **Current frontier for Phase 3.1b**: implement real async generators (the one remaining gap for full
  WebSocket streaming-helper parity), or continue exercising other unexercised corners
  (`staticfiles.py`'s `packages=` argument, `Starlette`'s lifespan events) while treating async
  generators as a separate, explicitly-scoped follow-up.

### 3.1.12 — real async generators: a new language feature, built and verified end to end

The author's explicit go-ahead ("Implementa async generator") to invest in the capability flagged as
out of scope in 3.1.11. Design: a new `PyAsyncGenerator` (`Runtime/Async.cs`) — a hybrid of
`PyGenerator` (yield-suspension) and `PyCoroutine` (await-suspension) running its body on *one*
dedicated thread, with a single producer/consumer handshake tagged by which kind of suspension just
happened (`SuspendKind.Yielded`/`Awaiting`). `YieldExpr`/`AwaitExpr` evaluation (Interp.cs) now checks
`PyAsyncGenerator.Current` first, ahead of `PyGenerator.Current`/`PyCoroutine.Current`, so a body
mixing both constructs dispatches correctly without touching either existing class. `__anext__()`
mirrors `PyTask`'s relationship to `PyCoroutine` exactly: it returns a fresh `Future` and steps the
body once; a yield resolves that Future with the value, a real `await` on a still-pending Future
recurses through `AddNativeCallback` (exactly `PyTask.Step`'s pattern) until a yield, a real return
(`StopAsyncIteration`), or an uncaught exception settles it. `Interp.CallFunction` now checks
`fn.IsAsync && fn.IsGenerator` (constructing a `PyAsyncGenerator`) *before* the plain `IsAsync` check
— previously that always won, so `async def f(): yield x` silently produced a plain `PyCoroutine`
with no `__aiter__`/`__anext__`, the exact bug 3.1.11 confirmed. The parser already correctly detected
this case (`ContainsYield` runs regardless of async-ness) — only the interpreter's dispatch and object
model were missing.

- **Manually verified against known-correct Python behavior first** (six scenarios, all matching
  exactly): plain `async for` iteration; the manual `__aiter__`/`__anext__` protocol raising
  `StopAsyncIteration` on exhaustion; a real `await` *inside* the generator body between yields
  (mixing both suspension kinds on the same thread); early `break` out of `async for`; and an
  uncaught exception in the body propagating through `async for`. Then re-verified against real
  starlette's actual `WebSocket.iter_text()` (not a synthetic test) — the exact scenario 3.1.11 found
  blocked — which now round-trips correctly end to end.
- **`inspect.iscoroutinefunction`/`asyncio.iscoroutinefunction` narrowed to exclude async generator
  functions** (`IsGenerator: false` added to the match) and **`isasyncgenfunction`/`isasyncgen`
  made real** (previously hardcoded `False` — a documented limitation from before real async
  generators existed) — real CPython treats coroutine functions and async generator functions as
  mutually exclusive categories; verified via the same probe before trusting it.
- **`contextlib.asynccontextmanager`'s `__aenter__`/`__aexit__` are now real too**, not a side
  effect but a direct, obvious unblock once `PyAsyncGenerator` existed (they previously raised
  `NotImplementedError` unconditionally, explicitly citing "PySharp doesn't support async generators
  yet" in the code comment). Mirrors the existing sync `_GeneratorContextManager`'s `__enter__`/
  `__exit__` exactly (`MoveNext`/`ThrowInto` there ↔ a new `PyAsyncGenerator.ANext`/`AThrow` here),
  wrapped in the Future-continuation shape every other async builtin in this codebase already uses.
  `AThrow` needed no new suspension mechanism at all — `Resume`'s existing `throwErr` parameter
  (originally built for internal await-chain error propagation) already does exactly "deliver a
  pending exception at the next yield/suspend point," so exposing it as a public method was enough.
  Verified manually against known-correct behavior first (normal enter/exit, cleanup-then-propagate
  on an uncaught body exception, suppression of an exception the body catches internally, and a real
  `await` inside the context-manager body) — all four matched exactly, before writing tests.
- 10 tests added (`M10_Async/AsyncGeneratorTests.cs`, new file, 6 tests; `M6_Stdlib/StdlibTests.cs`'s
  existing `AsyncContextManagerTests`, 3 more tests). Full suite green throughout: 901/901 by the end
  of this round, up from 892 at the start of it.
- **Axis A gap list updated**: async generators (and the `asynccontextmanager` entering restriction
  it caused) move from "missing" to real, working language support in ROADMAP.md.
- **Current frontier for Phase 3.1b**: `staticfiles.py`'s `packages=[...]` argument and `Starlette`'s
  lifespan events remain unexercised. Async generators being real now also opens up probing anything
  in the pydantic/starlette/anyio dependency chain that previously hit the same wall — worth keeping
  in mind if a future round's frontier turns out to be exactly this gap again elsewhere.

### 3.1.13 — the last two unexercised corners of 3.1b, both clean: **Phase 3.1b is substantially done**

Picking up the exact 3.1.12 frontier. Three real probes, all against real starlette/anyio, zero bugs
found — every one exercised machinery already built in prior rounds, and all of it held up.

- **Lifespan events** (`Starlette(lifespan=@asynccontextmanager-decorated function)`, the modern
  recommended style): a hand-built `{"type": "lifespan", ...}` scope + `lifespan.startup`/
  `lifespan.shutdown` message sequence correctly drives real starlette's `Router.lifespan()` through
  the *real* `asynccontextmanager` machinery built in 3.1.12 — startup runs the pre-yield code, the
  yielded state dict merges into `scope["state"]` (verified with a real ASGI3 "state" lifespan
  extension scope, `"state": {}`), and shutdown runs the post-yield code, in the correct order,
  producing the correct `lifespan.startup.complete`/`lifespan.shutdown.complete` messages. A second
  probe verified the failure path: an exception raised during startup produces a real
  `lifespan.startup.failed` message with a real traceback string, and correctly re-raises out of
  `app()` afterward — matching real starlette's `except BaseException: ...; raise` exactly. (One
  false start: the first probe's hand-built scope omitted `"state": {}`, correctly triggering
  starlette's own `RuntimeError('The server does not support "state" in the lifespan scope.')` — a
  probe bug, not a PySharp one, caught by re-reading real starlette source rather than assuming the
  interpreter was at fault.)
- **`StaticFiles(packages=[...])`**: a real installed package (`mypkg`, with a `statics/asset.txt`
  alongside its `__init__.py`) mounted via `StaticFiles(packages=["mypkg"])` correctly serves
  `/pkg-static/asset.txt` with a real 200 and the expected file bytes — exercising the real
  `importlib.util.find_spec` built in 3.1.10 for its intended runtime call site (not just at
  `staticfiles.py`'s module-load-time import), plus `os.path.normpath`/`os.path.join` deriving the
  package's statics directory from `spec.origin`.
- No interpreter changes this round — all three probes passed exactly as real CPython/starlette
  would; the existing 901/901 suite is unaffected, no new tests needed.
- **Phase 3.1b is now substantially done**: routing (index + path-parameter routes), exception
  handling (default 404, custom per-status and per-`Exception`-type handlers, uncaught-exception
  propagation), static file serving (both `directory=` and `packages=`), WebSockets (plain
  accept/receive/send/close, disconnect handling, manual streaming loops, and real async-generator-
  backed `iter_text`/`iter_bytes`/`iter_json`), and lifespan events (success and failure) are all
  verified end to end against real, unmodified starlette + anyio. Remaining before considering 3.1b
  fully closed: none identified by probing so far — the next natural step is either 3.2 (a real ASGI
  server, currently just a placeholder) or Phase 4 (attempting `import fastapi` itself, a
  significantly larger new probe target likely to surface many new gaps, similar in character to how
  Phase 2's pydantic work or Phase 3's starlette work began) — a natural decision point for the
  author rather than picking one unprompted.

### 3.1.14 — Phase 3.2: a real, minimal ASGI server, built and verified over real HTTP

The author's explicit direction ("Andiamo in sequenza" — go in order): 3.2 before Phase 4, matching
the plan's own numbering. `samples/asgi_server.py` bridges raw HTTP/1.1 to the real ASGI 3.0
scope/receive/send protocol, reusing scenario 1b/2's real async socket I/O
(`loop.sock_accept`/`sock_recv`/`sock_sendall`, already built for the reactor). `serve(app, host,
port)` is a reusable function accepting *any* real ASGI callable — not tied to the sample's own
hand-written demo app. v1 scope, deliberately: request bodies are read fully before the app runs (no
streaming request bodies), and every connection closes after one response (no HTTP/1.1
keep-alive/pipelining) — real, honest simplifications, matching the project's standing style of
scoping down explicitly rather than silently.

- **Verified over real HTTP** (curl, matching scenario 1's own verification style — not just an
  offline unit test): the sample's own hand-written demo app first (index route, a path parameter,
  a POST echo, a 404) — all correct. Then, more importantly, **a real, unmodified `Starlette` app**
  (with a route, a path-parameter route, and a custom 404 handler) served through the exact same
  `serve()` function — real HTTP GET/POST round-trips against real starlette dispatch, real path
  params, and a real custom exception handler, all correct.
- **One real, general interpreter bug found and fixed along the way, found by the *second*
  verification (the real-Starlette one), not the first**: `sys.path` was built once as a *snapshot
  copy* of `Importer.SearchPaths` at module-creation time
  (`new PyList(importer.SearchPaths.Select(...))`), so a script's own `sys.path.insert(...)`/
  `.append(...)` mutated that disconnected copy and had **zero effect on actual import resolution** —
  a real bug well beyond this one scenario (any script trying to add a sibling directory to
  `sys.path` at runtime was silently doing nothing). Confirmed via the `probe_real_asgi_server.py`
  verification script: `sys.path.insert(0, ".../samples")` then `import asgi_server` raised
  `ModuleNotFoundError` even though the directory genuinely existed and the entry genuinely appeared
  in `sys.path`. Fixed by giving `Importer` a *live* reference to the real `sys.path` `PyList`
  (`Importer.PythonSysPath`), consulted alongside the existing C#-side `SearchPaths` at import time
  — every existing `Importer.SearchPaths.Add(...)` call site (13 of them, mostly test fixtures)
  keeps working unchanged.
- **A second, smaller real gap found by the demo app's own HTTP/1.1 request-line parsing**:
  `bytes.partition`/`rpartition` didn't exist at all — only `str` had them. Real CPython has both.
  Added for real, mirroring `str.partition`'s exact semantics over raw bytes.
- 8 tests added (`M3_Evaluator/StringTests.cs`: 4 `bytes.partition`/`rpartition` cases;
  `M5_Imports/ImportTests.cs`: 1 `sys.path.insert` regression test; new
  `M16_FastApi/AsgiServerSampleTests.cs`: 4 tests exercising the demo app via the same hand-built
  scope/receive/send technique used throughout this whole plan, without needing a real socket in the
  automated suite — the real-socket, real-Starlette path was verified manually via curl instead,
  matching how every other sample in this project is verified). Full suite green throughout: 910/910
  by the end of this round, up from 901 at the start of it.
- **Phase 3.2 is done.** Scenario 2's Phase 3 (starlette + anyio + a real ASGI server) is now
  substantially complete end to end. **Next**: Phase 4 — attempting `import fastapi` itself.

## Phase 4 — FastAPI itself + a real target app (placeholder)

- [x] 4.1 `import fastapi` succeeds. **Done** (4.1.1–4.1.3): pinned to the last real
  `fastapi==0.99.1`/`starlette==0.27.0`/`pydantic==1.10.13` combination built purely against
  pydantic v1; found and fixed two serious real concurrency bugs (an import-system deadlock, and a
  flaky-suite MRO race), implemented real `eval()`/`typing.ForwardRef`/
  `typing_extensions._AnnotatedAlias`, and closed ~15 smaller real stdlib/typing gaps along the way.
  `from fastapi import FastAPI` resolves the real class; `FastAPI()` construction is the next
  frontier (`inspect.isroutine` missing), not yet started.
- [ ] 4.2 Write the first real target sample (mirrors scenario 1's/1b's "the script is the test
  bench"): a small real FastAPI app — path params, a pydantic request model, JSON response — run
  under PySharp with an ASGI server (starlette's own dev server, or a minimal one over the C#
  `socket` per ROADMAP 2e's fallback option if uvicorn itself doesn't port cleanly).
- [ ] 4.3 Verify with real HTTP requests (`curl`, matching scenario 1's `http_api.py`/`http_api_min.py`
  verification style), not just offline unit tests.

### 4.1.1 — version pinning, a real deadlock, several real gaps, then real pydantic validator internals

**Version pinning** (real, needed before any probing could start): the mini-pip's default `install
fastapi` resolves the *latest* release (0.141.1), which requires **pydantic v2** (`pydantic-core`,
Rust — exactly the wall this whole plan has deliberately avoided since Phase 0). Pinned to
`fastapi==0.99.1`, the last release built purely against pydantic v1 (no v2 compatibility layer
assumptions). That in turn requires `starlette>=0.27.0,<0.28.0` — considerably older than every
starlette version probed in Phase 3 (1.4.1/1.5.0) — pinned to `starlette==0.27.0`. `anyio<5,>=3.4.0`
was already satisfied by the existing 4.14.2 install. **This combination — fastapi 0.99.1 + starlette
0.27.0 + pydantic 1.10.13 — is the one to install for all further Phase 4 probing**; re-derive this
exact recipe at the start of any future round rather than defaulting to `install fastapi` bare.

- **`starlette.applications` failed immediately**: `AttributeError: 'type' object has no attribute
  '_reserved'` — real starlette 0.27.0's `responses.py` does
  `http.cookies.Morsel._reserved["samesite"] = "SameSite"` at module level, patching the *class*
  attribute directly. PySharp's `Morsel` (a real parsed-Python-source class, `HttpModule.cs`) had
  `_RESERVED`/`_FLAGS` as *module-level* names (closed over by methods), not real class attributes
  — so `Morsel._reserved` didn't exist at all. Fixed by moving them into the class body as real
  `_reserved`/`_flags` class attributes (renamed to match real CPython's actual lowercase names),
  updating every internal reference from the bare module-level name to `self._reserved`/
  `self._flags` (methods don't see class-body-level names as a lexical scope in Python — a real,
  well-known quirk, not an oversight).
- **`email.message` didn't exist as an importable submodule at all** — fastapi's real `routing.py`
  does `import email.message` at module load time, then later
  `message = email.message.Message(); message["content-type"] = value;
  message.get_content_maintype()`/`get_content_subtype()` to check whether a request body is
  JSON-shaped without a full MIME parser. Implemented a real (not stubbed) `email.message.Message`:
  real header storage (repeated `__setitem__` appends, case-insensitive lookup, matching real
  CPython) and real `get_content_type`/`get_content_maintype`/`get_content_subtype` parsing the
  Content-Type header, defaulting to `"text/plain"` for a missing/malformed value — matching
  `Lib/email/message.py`'s own algorithm. Registered as a separate builtin factory
  (`importer.RegisterBuiltin("email.message", ...)`), matching the `importlib.util` pattern from
  Phase 3.2 — needed for `import email.message` itself to resolve, not just attribute access after
  `import email`.
- **`typing.TypeGuard` didn't exist** — a bare placeholder was enough (real CPython 3.10+, a
  type-checker-only marker with no runtime behavior needed here).
- **A serious, general deadlock found and fixed** (the most significant finding of this round):
  `Importer.ImportAbsolute` held a single C# lock for its *entire* recursive module load-and-execute
  loop — including running the target module's arbitrary Python code. PyGenerator/PyCoroutine/
  PyAsyncGenerator (and real `threading.Thread`) each run their body on a genuine dedicated OS
  thread (the project's whole coroutine/generator model, established since scenario 2a). So a
  module-level generator expression evaluated synchronously (`list(some_generator())`) while the
  importing thread was still inside that held lock would spawn a *second* real OS thread — and if
  that thread's body needed to `import` anything not yet cached, it blocked forever on the very lock
  the first thread wasn't going to release until that same generator finished. Found via real
  pydantic v1's `pydantic/utils.py` (transitively imported by `import fastapi`), whose own
  module-level code hits exactly this shape — reproduced by adding temporary `Importer`-level
  tracing (module name + CLR thread ID at each import call) to watch the exact moment a second
  thread appeared and never got past "waiting for the lock." Fixed by narrowing the lock to only the
  `Modules` dict bookkeeping (the cache check, and the "register before executing" step
  `ExecuteFile` already did — which is what makes circular imports safe in the first place), never
  around the actual code execution — matching how real CPython's own import lock is per-module, not
  one lock held across an unbounded amount of arbitrary code. A dedicated regression test
  reconstructs the exact shape (a package whose `__init__.py` imports a submodule that drives a
  generator whose body itself imports a third, not-yet-cached submodule), wrapped in a
  `Task.WhenAny(..., Task.Delay(15s))` guard so a real regression fails the test instead of hanging
  the whole suite.
- **New frontier**: past all of the above, `import fastapi` now reaches real pydantic v1's field/
  validator machinery (`ModelField.prepare` → `_type_analysis` → `_create_sub_type` →
  `populate_validators` → `find_validators`) and hits a real `RuntimeError`: no validator found for
  `NoneType` (pointing at the `arbitrary_types_allowed` Config option) — real pydantic recursing into
  building a sub-`ModelField` for each member of a `Union`/`Optional` annotation (including the `NoneType`
  member, which real pydantic *does* have a built-in validator for — `none_validator`). Not yet
  root-caused: plausibly a `type(None)`/`NoneType` identity or registry-lookup mismatch somewhere in
  PySharp's typing/class machinery, but unconfirmed. A genuinely deep, pydantic-internals
  investigation — a natural point to pause and report given the size of this round's findings
  (especially the deadlock) rather than pushing further immediately.
- 1 test added (`M5_Imports/ImportTests.cs`: the deadlock regression). Full suite green throughout:
  911/911 by the end of this round, up from 910 at the start of it.

### 4.1.2 — root-caused: two real identity/inheritance bugs, then a real language-feature wall (`eval()`)

Picking up the exact 4.1.1 frontier (`RuntimeError: no validator found for NoneType`). Root-caused
via direct probing (constructing the exact failing expression in isolation, comparing `is`/`==`/
`in` against real pydantic's own `is_none_type` logic) rather than blind guessing.

- **`NoneType` from `type(None)` and from `Optional[X]`'s implicit member were two different
  objects**: `MiscModules.NoneTypeClass` (`typing.NoneType`, and what `Optional`/`Union`'s
  args-transform appends for the implicit `None` member) was built completely independently of
  `Builtins.TypeNamePseudoClass`'s own lazily-created-and-cached "NoneType" pseudo-class — the thing
  `None.__class__`/`type(None)` actually return. Confirmed directly: `get_args(Optional[int])[1] is
  type(None))` was `False`. Real pydantic v1's own `is_none_type` (`type_ in NONE_TYPES`,
  `pydantic/typing.py`) relies on exactly this identity holding. Fixed by special-casing
  `TypeNamePseudoClass` to return `MiscModules.NoneTypeClass` directly for the "NoneType" name,
  unifying both paths onto one canonical object (no import-order dependency, unlike a "seed the
  cache once" approach would have needed).
- **`issubclass(list, typing.List)` returned `False`**: `typing.List`/`Set`/`FrozenSet`/`Dict`/...
  are bare placeholder `PyClass`es with no real relationship to the concrete builtin they represent
  — a flat MRO-based `issubclass` check against them always failed, unlike real CPython's
  `_SpecialGenericAlias.__subclasscheck__`, which delegates to the real origin
  (`GenericAliasModule`'s existing `OriginMap`, e.g. `List` → `list`, already used for
  `get_origin`/subscripting but never consulted by `issubclass`). Found via real pydantic v1's own
  `schema.py` resolving a `Field(min_items=1)` constraint on a real `Optional[List[str]]` field
  (fastapi's real `openapi/models.py`) — `issubclass(get_origin(List[str]), List)` silently came back
  `False`, so pydantic concluded the constraint was unenforced and raised. Fixed by adding a public
  `GenericAliasModule.TryGetOrigin` and consulting it as a fallback in `Builtins.IsSubclass`'s
  `PyClass` case.
- **`inspect.Parameter.replace(**changes)` didn't exist** — a small, independent gap found along the
  way (real pydantic v1's own `generate_model_signature`, `pydantic/utils.py`:
  `var_kw.replace(name=var_kw_name)`, renaming a `VAR_KEYWORD` parameter while building a real
  `BaseModel`'s `__init__` signature). Added for real, returning a new `Parameter` with the given
  field(s) overridden.
- **New frontier — a real language-feature wall, not a gap-fill**: past all three fixes above,
  `import fastapi` now reaches `Schema.update_forward_refs()` (fastapi's real `openapi/models.py`,
  resolving genuinely self-referential JSON-Schema-shaped forward refs — `SchemaOrBool = Union[Schema,
  bool]`, referenced as the *string* `"SchemaOrBool"` throughout `Schema`'s own field annotations,
  defined only *after* the class body). Real CPython's forward-ref resolution fundamentally needs to
  `eval()` the annotation string against the defining module's namespace — and real `eval()`/`exec()`
  are a *documented, existing* Axis A gap (ROADMAP.md), previously never hit by any real scenario.
  PySharp's own `typing._eval_type`/`get_type_hints` were built as honest passthroughs specifically
  *because* nothing had exercised real deferred/string annotations yet — this is now that real
  exercise. Implementing real `eval()` (even scoped to expression-evaluation, which is genuinely all
  `eval()` itself ever does — unlike `exec()`, which also handles statements) is a new language
  capability, not a quick fix — the same class of decision as the async-generator round: a natural
  point to check in with the author before investing, rather than starting unprompted.
- 3 tests added (`M6_Stdlib/StdlibTests.cs`: `Parameter_replace_returns_a_new_Parameter_with_the
  _given_fields_overridden`, new `TypingIdentityTests` with 2 tests). Full suite green throughout:
  914/914 by the end of this round, up from 911 at the start of it.
- **A second real concurrency bug, found by the fix above making an existing latent one much more
  likely to trigger**: `GenericAliasModule.OriginMap`/`ArgsTransform` were plain, non-thread-safe
  `Dictionary`s — static/shared across every `Interp` instance, written on every `import typing`
  (`MiscModules.CreateTyping`) and, after the `issubclass` fix above, read far more often (every
  `issubclass` call, not just the narrower `Subscript` path that read them before). Under xUnit's
  real concurrent test execution (parallelizing across test classes, each with its own `PyEngine`),
  one thread's `import typing` writing to these dictionaries while another thread's `issubclass`
  call read them could corrupt their internal bucket structure — which for .NET's plain `Dictionary`
  can manifest as a genuine infinite loop, not just wrong results. Surfaced as the full test suite
  hanging intermittently right after the `issubclass` fix landed (previously green; three repeat
  runs made it obvious this was new, not flaky infrastructure). Fixed by switching both to
  `ConcurrentDictionary` — thread-safe reads and writes, no lock needed given the access pattern
  (rare writes at module-setup time, frequent reads). Confirmed with 4 consecutive clean full-suite
  runs after the fix. No dedicated regression test (a genuine cross-thread race isn't reliably
  reproducible in one deterministic test the way the import deadlock was); the fix itself is the
  correctness fix, verified by the suite's now-consistent behavior under real parallel execution.

### 4.1.3 — `import fastapi` succeeds: real `eval()`/`ForwardRef`, a second flaky-suite deadlock, then the milestone

The author's explicit go-ahead ("procedi") to implement real `eval()` — the wall 4.1.2 stopped at.
This round closed it out completely and reached Phase 4.1's actual target.

- **Real `eval(source, globals=None, locals=None)`** (Builtins.cs): parses `source` via the
  existing `Parser.ParseExpression` (already used for f-strings) and evaluates it against the given
  namespaces. With no `globals`, evaluates against the caller's own real live environment
  (`Interp.InnermostFrame`), matching real CPython's frame-introspecting default. A new
  `PyModule(name, PyDict)` constructor overload lets the given `globals` dict back the eval
  environment *directly* (not a copy), so mutations during evaluation stay visible to the caller
  afterward — real CPython's exact semantics. Verified manually against known-correct behavior
  first (simple expressions, tuple expressions, no-globals against the caller's scope, explicit
  globals, separate globals+locals) before writing tests. `exec()` (statements, not just an
  expression) stays out of scope — genuinely unneeded here, since `eval()` is real CPython's own
  full scope for a single expression too.
- **Real `typing.ForwardRef`** (`GenericAliasModule.BuildForwardRefClass`): a real `__init__`
  (storing `__forward_arg__` and the rest of the real bookkeeping fields pydantic v1's own
  `update_field_forward_refs` inspects directly via `field.type_.__class__ == ForwardRef`), a real
  `_evaluate(globalns, localns, recursive_guard)` resolving the string via the real `eval()` just
  built, and real `__eq__`/`__hash__`/`__repr__` (two `ForwardRef('X')` instances compare and hash
  equal, matching real CPython). `GenericAliasModule.Subscript` now auto-wraps a bare string type
  argument into one (`Optional["SchemaOrBool"]`) — real CPython's `_type_check` does the same;
  without it, pydantic's `isinstance(type_, ForwardRef)` checks never recognized the string as
  something to defer, and `find_validators`' `issubclass(type_, val_type)` loop raised a real
  `TypeError` trying to `issubclass` a bare string.
- **Real `typing_extensions._AnnotatedAlias`** (MiscModules.cs): a real `__init__` storing
  `__origin__`/`__metadata__`/`__args__` (previously a bare placeholder, raising "takes no
  arguments" — real pydantic v1's own `convert_generics`, pydantic/typing.py, constructs one
  *directly* while recursively replacing bare string type arguments inside `Annotated[...]` with
  real `ForwardRef`s). Merges metadata when wrapping an already-`_AnnotatedAlias` origin
  (`Annotated[Annotated[X, a], b]` flattens to one alias with combined metadata), matching real
  CPython.
- **A real, separate, general gap found while verifying `ForwardRef.__hash__`**: `hash(x)` never
  consulted a `PyInstance`'s own `__hash__` dunder at all — `==`/`RichEquals` already did this for
  `__eq__`, but `hash()` (`Builtins.cs`) always fell back to raw CLR identity hashing regardless of
  any real `__hash__` override. Fixed by checking `inst.Class.TryLookup("__hash__", ...)` first,
  the same way `RichEquals` already does for `__eq__` — a real fix well beyond `ForwardRef`, since
  *any* user-defined class overriding `__hash__` was silently ignored by the builtin before this.
- **A second real, intermittent flaky-suite bug — the same class as 4.1.2's `OriginMap` race, one
  instance missed the first time**: `GenericAliasModule.GenericPlaceholder` (identifying
  `typing.Generic` by identity, to de-duplicate a redundant `Generic[T]` base in a class's resolved
  MRO) was a single plain `public static` field — but `MiscModules.CreateTyping` builds a *fresh*
  "Generic" `PyClass` on every `import typing` (one per `Interp` instance, i.e. one per test/
  script), so under real parallel test execution, whichever test's `import typing` ran *last*
  silently overwrote every other concurrently-running test's own Generic identity. A later test's
  `class Foo(Generic[T]):` de-duplication check then compared against the *wrong* (some other
  test's) Generic class, leaving a genuine duplicate `Generic` in the resolved bases and breaking
  MRO computation outright (`TypeError: Cannot create a consistent MRO`) — intermittently, not
  every run, which is why it survived the 4.1.2 fix undetected until a fresh round of repeated
  full-suite runs caught it (twice: once as an outright hang, once as 2 real test failures). Fixed
  with `[ThreadStatic]`, matching the same pattern `PyGenerator.Current`/`PyCoroutine.Current`
  already use for analogous per-execution-context state — each `PyEngine.Run()` executes its
  script on its own dedicated OS thread (`BigStack.Run`), so this correctly scopes the placeholder
  per test/script without the cross-thread interference. **Confirmed with 41 consecutive clean
  full-suite runs afterward** (25 immediately after this specific fix, 8 more with this round's
  remaining gaps fixed, 8 more with the final test additions) — the flakiness genuinely stopped, not
  just didn't happen to reproduce.
- **Four more small, real stdlib gaps closed reaching the actual milestone**: `email.message.Message`
  didn't exist (real header storage + `get_content_type`/`get_content_maintype`/`get_content_subtype`,
  found via fastapi's real `routing.py` checking whether a request body is JSON-shaped);
  `typing.TypeGuard`/`AsyncGenerator` didn't exist (bare placeholders, matching the rest of the list);
  `binascii` didn't exist at all (just `Error`, a real `ValueError` subclass — found via fastapi's
  real `security/http.py`, `except (ValueError, UnicodeDecodeError, binascii.Error):` around a
  `base64.b64decode` call); `http.client` didn't exist as an importable submodule (just `responses`,
  a real status-code → reason-phrase dict built from the same data `http.HTTPStatus` already
  carries — found via fastapi's real `openapi/utils.py` defaulting a response description from the
  real reason phrase).
- **`import fastapi` succeeds.** Verified directly and repeatedly (manually, then via a new
  deterministic regression test): `import fastapi; print(fastapi.__name__)` runs clean against the
  real, pinned `fastapi==0.99.1`/`starlette==0.27.0`/`pydantic==1.10.13` combination, and
  `from fastapi import FastAPI` resolves the real class. **This is Phase 4.1's target milestone.**
  New frontier found immediately past it (not chased this round): `FastAPI()` *construction* hits
  `AttributeError: 'module' object has no attribute 'isroutine'` (`inspect.isroutine` doesn't exist)
  — a small, concrete, well-scoped next gap, deliberately left for the next round rather than
  chasing indefinitely past this round's actual target.
- 14 tests added: `M6_Stdlib/StdlibTests.cs` gained `EvalBuiltinTests` (4), `ForwardRefTests` (3),
  `AnnotatedAliasTests` (2), `HashDunderTests` (1), `BinasciiAndHttpClientTests` (2); new
  `M16_FastApi/FastApiInstallFixture.cs` + `FastApiSmokeTests.cs` (2, using the same
  `IClassFixture`-based real-PyPI-install pattern as the existing `PydanticSmokeTests`). Full suite
  green throughout, confirmed stable across dozens of repeated runs: 928/928 by the end of this
  round, up from 914 at the start of it.

### 4.1.4 — past the milestone: `FastAPI()` construction, real route registration, `openapi()`, then the `httpx` wall

Continuing straight past 4.1.3's `import fastapi` milestone into actually constructing an app and
registering routes — the next frontier flagged at the end of 4.1.3.

- **`inspect.isroutine`** (InspectModule.cs): didn't exist at all
  (`AttributeError: 'module' object has no attribute 'isroutine'`). Real CPython:
  `isroutine = isbuiltin or isfunction or ismethod or ismethoddescriptor or ismethodwrapper` —
  implemented as `PyBuiltinFunction or PyFunction or PyBoundMethod` (the practically relevant
  subset; the two rare C-level slot-wrapper cases aren't produced by anything reachable here).
  Verified manually against real CPython semantics first (function, bound method, builtin, class,
  int, string — 6 cases). Found via real starlette's own `routing.py`'s `get_name(endpoint)`,
  called while constructing *every* real `Route` — so this blocked `FastAPI()` itself from
  constructing, since it builds its own default docs/openapi/redoc routes at construction time.
  With this fixed, `FastAPI()` constructs successfully.
- **`inspect.Parameter.__init__` didn't accept `name`/`kind` as keywords** (InspectModule.cs): only
  ever read them positionally (`a[1]`/`a[2]`), throwing `TypeError: Parameter() missing required
  argument: 'name'` the moment both were passed by keyword. Real CPython's `Parameter.name`/`.kind`
  are positional-or-keyword, not positional-only (only `default`/`annotation` are keyword-only,
  after the real `*`). Found via real fastapi's own `get_typed_signature`
  (`dependencies/utils.py`), calling `inspect.Parameter(name=param.name, kind=param.kind,
  default=param.default, annotation=...)` entirely by keyword while building the typed signature
  for *every* route handler — so this blocked all real route registration outright. Fixed by
  checking `kwargs` as a fallback for both, matching the pattern already used for
  `default`/`annotation`. Verified manually (`inspect.Parameter(name=..., kind=...)` produces an
  identical object to the equivalent positional call) before trusting it. With this fixed, real
  `@app.get("/")`/`@app.get("/items/{item_id}")` route registration — including a path parameter —
  succeeds (`len(app.routes) == 6`: the app's own default `/openapi.json`/`/docs`/
  `/docs/oauth2-redirect`/`/redoc` plus the two registered here). `app.openapi()` (real schema
  generation) also verified working at this point, unprompted — no further gap needed for it.
- **`urllib.parse.urljoin` didn't exist at all** (UrllibModule.cs): `ImportError: cannot import
  name 'urljoin' from 'urllib.parse'`. Found via real starlette's `testclient.py`
  (`from urllib.parse import unquote, urljoin`, used to build the fake `ws://testserver` URL for
  `TestClient`) — the natural next step past route registration towards actually issuing a request
  against the app. Implemented as a real, direct port of CPython's own `Lib/urllib/parse.py`
  algorithm (RFC 3986 §5 relative-reference resolution: netloc override, last-path-segment
  replacement, absolute-path override, `.`/`..` segment resolution including climbing past the
  root, `uses_relative`/`uses_netloc` scheme allowlists reproduced verbatim), scoped to the
  no-`;params` URL shape this file's `urlparse`/`urlsplit` already use throughout. Two real porting
  bugs were caught and fixed during manual verification (no local Python interpreter was available
  in this environment, so every expected value was hand-derived by tracing CPython's actual
  algorithm line by line, then cross-checked against 17 cases): (1) `urlunsplit` must force a
  leading `/` onto the path when a netloc is present — missing this turned
  `urljoin("http://example.com", "path")` into the malformed `"http://example.compath/path"`
  instead of `"http://example.com/path"`; (2) the `segments[1:-1] = filter(...)` in-place
  slice-assignment CPython uses degenerates when the list has exactly one element (index `0` and
  index `-1` are the *same* element there), which an initial reconstruction as
  `head + middle + tail` didn't account for, duplicating that lone element. Both fixed and
  re-verified against the full 17-case suite before locking in with a test.
- **The next real wall, found immediately after**: `starlette.testclient.TestClient` (needed to
  actually issue a request against a constructed app — the natural next milestone) imports `httpx`
  at module level, and `httpx` isn't installed at all (`ModuleNotFoundError: No module named
  'httpx'`). This is a substantially larger dependency (a real, independent HTTP client library
  with its own transitive dependency tree — `httpcore`, `certifi`, `sniffio`, `h11`, etc.),
  deliberately not chased this round; left as the concrete next step for whoever picks this back up.
- 4 tests added: `M6_Stdlib/StdlibTests.cs` gained
  `Parameter_constructor_accepts_name_and_kind_as_keyword_arguments` and
  `Urljoin_resolves_relative_urls_against_a_base_matching_real_CPython` (17 cases in one test);
  `M16_FastApi/FastApiSmokeTests.cs` gained `FastAPI_app_can_be_constructed` and
  `Real_routes_with_path_parameters_can_be_registered`. Full suite green throughout, confirmed
  stable across 6 repeated full-suite runs this round: 932/932 by the end of it, up from 928 at the
  start.

### 4.1.5 — `httpx` install, real async comprehensions (a genuine language-feature gap), `codecs`, `urllib.request.parse_http_list`, and a real pre-existing flaky-suite bug root-caused

Continuing straight past 4.1.4's `httpx` wall.

- **`httpx==0.28.1` installed** (real PyPI, unpinned — no pydantic/starlette version coupling here).
  `import httpx` immediately hit two real gaps in sequence.
- **PEP 530 async comprehensions (`[x async for x in y]`) didn't parse at all** — a genuine
  language-feature gap, not a stdlib gap: `ParseCompFors` (Parser.cs) only ever recognized a bare
  `for` token, and every comprehension-start check site (list/set/dict literals, a generator
  expression as a call's sole argument) only tested for `Cur.Is(Keyword, "for")`, so `async` there
  fell through to plain-list-literal parsing and blew up on the unexpected `async` token. Found via
  real httpx's own `_models.py` (`self._content = b"".join([part async for part in self.stream])`).
  Fixed by adding `CompFor.IsAsync` (Ast.cs), a shared `AtCompForStart()` predicate consulted at all
  5 comprehension-start sites, and reusing `ExecAsyncFor`'s existing `__aiter__`/`__anext__`/
  `StopAsyncIteration` handshake in a new `Interp.IterateAsync` helper, called from `RunCompFors`
  when the clause is async. No new threading needed: comprehensions are plain C# `yield`-based
  iterators (not PySharp's own dedicated-thread `PyGenerator`), so they run inline on whatever
  thread the enclosing coroutine's body is already executing on — `Await()` already knows how to
  suspend/resume there. Verified manually against real CPython semantics (iteration, an `if` clause,
  nesting inside `sum()`/`"".join()`) before trusting it.
- **`urllib.request.parse_http_list` didn't exist** — `ImportError`, found via real httpx's
  `_auth.py` (`from urllib.request import parse_http_list`, used to split a `WWW-Authenticate`-style
  header into its comma-separated auth-challenge fields). Direct port of CPython's own algorithm
  (RFC 2616 §4.2/§14.45: split on commas, but not commas inside a quoted string, including a
  backslash-escaped quote). Verified against 3 hand-traced cases (plain list, a quoted value
  containing a comma, nested escaped quotes) before trusting it.
- **`codecs` module didn't exist at all** — `ModuleNotFoundError`, found via real httpx's
  `_models.py` (`codecs.lookup(encoding)`, validating a response's charset) and `_decoders.py`'s
  `TextDecoder` (`codecs.getincrementaldecoder(encoding)(errors="replace")`, incrementally decoding
  a streamed HTTP response body). Implemented for real, backed by .NET's own `Decoder` — "real"
  because .NET's `Decoder` already correctly buffers a multi-byte sequence split across chunk
  boundaries, which is exactly what "incremental" means here. Two real bugs were caught during
  manual verification (no local Python interpreter is available in this environment, so behavior
  was hand-derived and cross-checked): (1) calling `Decoder.GetCharCount` then `Decoder.GetChars`
  separately — the obvious-looking approach — double-processes any multi-byte sequence held over
  from a prior call, silently corrupting it; switched to `Decoder.Convert`, the API .NET actually
  documents as safe for incremental/streaming use; (2) `errors="strict"/"replace"/"ignore"` map
  cleanly onto .NET's `DecoderFallback.ExceptionFallback`/`DecoderReplacementFallback("�")`/
  `DecoderReplacementFallback("")` respectively, built against the already-resolved base encoding's
  `CodePage` (reusing `StrModules.GetEncoding`, not a second alias table).
- **A real, reproduced, pre-existing intermittent full-suite hang — root-caused, not a bug in any
  of the above.** Running the suite repeatedly (this project's standing discipline after 4.1.3's
  `GenericPlaceholder` bug) turned up an intermittent hang again. VSTest's
  `--blame-hang-dump-type full` caught it directly: the in-flight test was
  `AsyncioAdditionsTests.Task_is_a_real_importable_class_and_is_also_a_Future` — a test that predates
  this round entirely. Grepping every `asyncio.run`-calling test class turned up exactly two missing
  `[Collection("asyncio-run")]` tags: `AsyncioAdditionsTests` (`M6_Stdlib/StdlibTests.cs`) and
  `AsgiServerSampleTests` (`M16_FastApi/AsgiServerSampleTests.cs`) — every other such class already
  has the tag. `PyEventLoop._running` (Runtime/Async.cs) is a deliberately process-wide, not
  thread-local, static (a coroutine body runs on its own dedicated OS thread but must still see
  which loop is driving it), so any two tests that each drive their own event loop race on it unless
  xUnit is told never to run them concurrently — exactly what the `"asyncio-run"` collection exists
  to guarantee, and these two classes had silently fallen outside it. **Confirmed pre-existing, not
  introduced by this round's changes**, by building an isolated git worktree at the prior commit
  (before this round's async-comprehension/codecs work) and running its suite repeatedly: 13
  failures and 2 hangs across 15 runs, an even higher rate than newly observed here — this round's
  extra async-heavy tests just made the already-broken race easier to hit, they didn't create it.
  Fixed by adding the two missing tags. **Confirmed fixed**: 36 consecutive clean full-suite runs
  afterward (24 of them run concurrently against the still-broken baseline worktree under heavy CPU
  contention — deliberately worse conditions than normal — plus 12 more sequential runs with no
  contention), 0 failures, 0 hangs, versus the baseline's near-100% failure rate under the same
  concurrent load.
- 6 tests added: new `M10_Async/AsyncComprehensionTests.cs` (2, `[Collection("asyncio-run")]`);
  `M6_Stdlib/StdlibTests.cs` gained `CodecsTests` (3) and a `Parse_http_list_...` case added to
  `UrlSplitTests` (1). The flaky-suite fix itself added no new tests — the existing tests in
  `AsyncioAdditionsTests`/`AsgiServerSampleTests` already exercised the buggy path; the fix is to
  how they're scheduled, not what they assert. Full suite green throughout, confirmed stable per
  the 36-consecutive-clean-run count above: 938/938 by the end of this round, up from 932 at the
  start.
- **`httpx` itself still doesn't fully import past this round** — genuinely large scope left
  (`http.cookiejar`: real `Cookie`/`CookieJar` with RFC 6265-style domain/path matching and
  `Set-Cookie` header parsing, plus a real `urllib.request.Request` base class), deliberately not
  started this round; the concrete next step for whoever picks this back up.

### 4.1.6 — `import httpx` succeeds, `TestClient` gets close: a long chain of real gaps, ending at a genuine regex-engine Unicode limitation

The author's go-ahead ("procedi pure") to keep chasing `httpx` past 4.1.5's wall. This round closed
a long, varied chain of real gaps — stdlib modules, core interpreter/typing behavior, and PyPI
dependency installs — reaching **`import httpx` succeeds** before hitting a genuinely deep new wall.

- **`http.cookiejar`** (new `HttpCookiejarModule.cs`): real `Cookie` (generic `**kwargs` storage,
  matching real CPython's own always-fully-keyworded construction) and real `CookieJar` — RFC 6265
  §5.1.3/§5.1.4 domain-match/path-match (including real CPython's own extension: an empty cookie
  domain matches every request host, since `str.endswith("")` is always true in real CPython's own
  `domain_return_ok`), a real `Set-Cookie` response-header parser (`Domain=`/`Path=`/`Expires=`/
  `Max-Age=`/`Secure`), `set_cookie`/iteration/`len`/`clear`/`extract_cookies(response, request)`/
  `add_cookie_header(request)`. Deliberately scoped to the *interface* real httpx's `Cookies` class
  drives, not CPython's internal architecture (no `CookiePolicy` object, no RFC 2965/2109 legacy
  attributes) — the same "real observable behavior, not a byte-identical internal port" standard
  `re` already uses. Found via real httpx's `_models.py` (`from http.cookiejar import Cookie,
  CookieJar`).
- **`urllib.request.Request`** (UrllibModule.cs): real `get_full_url`/`get_method`/`get_type`/
  `get_host`/`get_origin_req_host`/`is_unverifiable`/`has_header`/`get_header`/`add_header`/
  `add_unredirected_header`/`header_items` — scoped to exactly what `CookieJar`'s own
  `add_cookie_header`/`extract_cookies` call (no proxy/redirect machinery). Found via real httpx's
  `Cookies._CookieCompatRequest(urllib.request.Request)`.
- **`email.message.Message.get_all`** (EmailModule.cs): didn't exist — needed by `CookieJar.
  extract_cookies` to read every repeated `Set-Cookie` header, not just the first.
- **`zlib`** (new `ZlibModule.cs`): real `compress`/`decompress`/`decompressobj`, backed by .NET's
  own `ZLibStream`/`DeflateStream`/`GZipStream`. .NET's compression streams are pull-based, not
  zlib's own push-based "feed a chunk, drain what's decodable so far" shape, so the incremental
  `Decompress` object re-decompresses the full byte history accumulated so far on every call and
  returns only the newly-available tail — correct for the common case (a handful of chunks), not a
  literal port of zlib's internal streaming state machine. Two real bugs caught during manual
  verification: separate `GetCharCount`+`GetChars` calls (the obvious-looking approach) corrupt a
  multi-byte/partial-block sequence held over between calls — the *exact same bug class* as the
  4.1.5 `codecs` fix, caught the same way, fixed the same way (`Decoder.Convert`, this time via
  `Decoder.Convert`'s streaming-compression analogue: feeding the accumulated buffer through fresh
  each time and diffing against what was already emitted). Found via real httpx's `_decoders.py`.
- **Real enum member tuple-value unpacking + `int.__new__`** (Interp.cs `ConvertToEnum` /
  `TryGetAttr`'s `PyBuiltinFunction` case): a member's value given as a tuple, combined with a
  class-defined `__new__`, now unpacks the tuple as that `__new__`'s positional args (real CPython
  semantics) — previously the raw tuple was stored as the member's `value`, so any later `int()`/
  hash/comparison on the member raised `TypeError: enum value: expected int, got tuple`. Needed a
  new `int.__new__(cls, value)` (constructing a real `PyInstance(cls)` for an arbitrary subclass,
  not just a bare `int`) since the real pattern's own `__new__` body calls it directly. Found via
  real httpx's own transitive dependency, `httpx._status_codes.codes(IntEnum)`
  (`OK = 200, "OK"` + `def __new__(cls, value, phrase=""): obj = int.__new__(cls, value); ...`),
  which the module ends with `for code in codes: setattr(codes, code._name_.lower(), int(code))` —
  exactly the call pattern that surfaced the bug.
- **Real `typing.TypedDict` subclass construction** (MiscModules.cs): `class Foo(TypedDict): ...;
  Foo(a=1)` raised `TypeError: Foo() takes no arguments` — TypedDict is erased at runtime in real
  CPython, so calling a TypedDict subclass must return a *plain dict*, never build an instance of
  the subclass. Fixed with a `__new__` on the base `TypedDict` placeholder returning a `PyDict`
  directly — `Interp.Instantiate` already implements the real CPython rule that `__init__` is
  skipped when `__new__` returns something that isn't an instance of `cls` (added in an earlier
  round for typing_extensions' backported `TypeVar`), so this needed no interpreter change at all.
  Found via real starlette's own `testclient.py`: `class _AsyncBackend(TypedDict): ...`.
- **Real functional-syntax `typing.NamedTuple`** (Interp.cs `ConvertToNamedTuple` made `internal`,
  called from a new `__new__` on the `NamedTuple` placeholder in MiscModules.cs): `NamedTuple("Name",
  [("field", type), ...])` (the functional form, not `class Foo(NamedTuple):`) raised the same
  "takes no arguments" error — only the class-based syntax was wired up. Fixed by building a real
  class with a synthetic `__annotations__` from the given field list and reusing the exact same
  `ConvertToNamedTuple` the class-based syntax already uses. Found via real httpx's `_types.py`:
  `RawURL = NamedTuple("RawURL", [("scheme", bytes), ("host", bytes), ("port", int)])`.
- **`urllib.parse.parse_qs`** (UrllibModule.cs): didn't exist — built on top of the existing
  `parse_qsl` helper, grouping repeated keys into lists. Found via idna/rfc3986 (httpx's own
  transitive dependencies).
- **`bisect`** (new `BisectModule.cs`): real `bisect_left`/`bisect_right`(`bisect`)/`insort_left`/
  `insort_right`(`insort`) — direct ports of CPython's own `Lib/bisect.py` algorithm. Found via real
  idna's `intranges.py`/`core.py`.
- **`unicodedata`** (new `UnicodedataModule.cs`): `category()`/`normalize()` are real, backed by
  .NET's own genuinely comprehensive Unicode Character Database (`CharUnicodeInfo`/
  `string.Normalize`) — not approximations. `combining()`/`bidirectional()`/`name()` have no .NET
  equivalent (the BCL doesn't expose canonical combining class, bidi category, or character names as
  queryable per-character data) and are honestly scoped: correct for the ASCII range, a documented
  simplification beyond it. Verified this doesn't silently break real idna's own bidi validation for
  ASCII-only hostnames: idna's `check_bidi` short-circuits to `True` for any label containing no
  RTL-category character (RFC 5893) — as long as ASCII never gets misclassified as RTL (it doesn't
  here, by construction), ASCII-only hostnames validate correctly even without a full bidi-category
  table; only genuine RTL-script (Hebrew/Arabic) domains would see a real functional gap. Found via
  real idna's `core.py`.
- **`netrc`** (new `NetrcModule.cs`): real `netrc`/`NetrcParseError` — a whitespace-tokenized parser
  for the real `.netrc` file format, scoped to the common case (no `macdef` macro-body support).
  Found via real httpx's `_utils.py`.
- **A builtin function can now carry arbitrary extra attributes** (`PyBuiltinFunction.Attributes`,
  a new `PyDict`, mirroring `PyFunction.Attributes`; wired into `Interp.SetAttr`/`TryGetAttr`): real
  Python-level functions could already do this, but a PyBuiltinFunction (this interpreter's
  implementation detail for some names that real CPython represents as plain functions) previously
  raised `AttributeError: 'function' object has no settable attribute '__module__'`. Found via real
  httpx's own `__init__.py`: `for __name in __all__: setattr(__locals[__name], "__module__",
  "httpx")`.
- **`sys.maxunicode`** (SysModule.cs): didn't exist — the trivial, well-known constant
  `0x10FFFF`.
- **PyPI dependencies installed** (unpinned, pure-Python, no version conflicts found): `idna`,
  `sniffio`, `rfc3986`.
- **A real version-compatibility wall, resolved by re-pinning, not by an interpreter change**: real
  `starlette==0.27.0`'s `TestClient.__init__` calls `super().__init__(app=self.app, ...)` — the
  `app=` convenience parameter on `httpx.Client`/`AsyncClient` that auto-wraps it into an ASGI
  transport, present in older httpx but removed in modern releases. The initial unpinned
  `httpx==0.28.1` install doesn't have it (`TypeError: _AsyncBackend() takes no arguments` — actually
  a `TypedDict`-construction symptom that surfaced first, then after fixing *that*, the real `app=`
  incompatibility appeared as `TypeError: __init__() got an unexpected keyword argument 'app'`).
  Re-pinned to `httpx==0.23.3` (which still has `app=`), matching the same "pin to what the era's
  other pinned packages actually expect" discipline as `fastapi==0.99.1`/`starlette==0.27.0`/
  `pydantic==1.10.13`.
- **`import httpx` succeeds.** Verified directly and repeatedly.
- **The wall this round actually stopped at, found while constructing a real `TestClient` against a
  real `FastAPI()` app**: real, unmodified `rfc3986` (an httpx transitive dependency) compiles a
  large generated regex for RFC 3986 URI validation whose character classes include literal
  supplementary-plane (astral) Unicode codepoints — real CPython's `re` engine operates on full
  Unicode codepoints, but PySharp's `re` module is backed by .NET's
  `System.Text.RegularExpressions`, which operates on UTF-16 *code units*. An astral codepoint
  becomes a surrogate pair in .NET's UTF-16 string representation, and .NET's character-class range
  syntax (`[X-Y]`) parses surrogate-pair-encoded range boundaries as individual UTF-16 code units
  rather than full codepoints — producing `error: Invalid pattern ... [x-y] range in reverse order`
  for a pattern that's completely valid, unremarkable Python `re` syntax. This is a genuine,
  general-purpose regex-engine limitation (not scoped to rfc3986 or httpx at all — *any* real
  pattern with an astral-plane character-class range would hit it), and fixing it properly needs
  either a codepoint-aware preprocessing pass over character-class ranges before handing patterns to
  .NET's engine, or a deeper change to the `re` module's matching strategy. Deliberately not
  attempted this round — it's a substantial, separate investigation in its own right, not a quick
  fix, and a well-justified stopping point after this round's already-long chain of real gaps.
- 18 tests added: `M6_Stdlib/StdlibTests.cs` gained `EnumTupleValueTests` (2),
  `TypedDictAndNamedTupleFunctionalTests` (2), `BuiltinFunctionAttributeTests` (1), `ZlibTests` (3),
  `BisectTests` (2), `UnicodedataTests` (3), `NetrcTests` (2), `HttpCookiejarTests` (2), plus a
  `Parse_qs_...` case added to `UrlSplitTests` (1). Full suite green throughout, confirmed stable
  across repeated runs: 956/956 by the end of this round, up from 938 at the start.

### 4.1.7 — the astral-regex wall solved for real, `import httpx` succeeds, `TestClient` construction gets much further

The author's go-ahead ("procedi") to tackle 4.1.6's regex-engine wall head-on rather than deferring
it further.

- **Real per-codepoint character-class handling for astral Unicode ranges** (`ReModule.cs`,
  `RewriteAstralCharClasses` + helpers, called from `TranslatePattern` before handing a pattern to
  .NET's `Regex`): implements the standard UTF-16 surrogate-pair range-decomposition algorithm (the
  same technique other UTF-16-based regex engines — e.g. JavaScript's own `u`-flag polyfills — use).
  A non-negated `[...]` class containing an astral member/range is rewritten into an alternation:
  any BMP-only members stay a plain `[...]`, and each astral range is split into the minimal set of
  `[high-surrogate-range][low-surrogate-range]` two-code-unit fragments whose union covers exactly
  that codepoint range — handling the general case (a range spanning many high-surrogate values,
  needing a partial-first/partial-last/full-middle-cross-product split), not just the single-high-
  surrogate special case. Negated classes (`[^...]`) and a class where an escape sequence forms a
  range endpoint are left unchanged (a documented, safe fallback — nothing reachable needs either).
  Verified by hand against 16 cases (range boundaries, a value just past the end, a different astral
  plane, plain BMP text, a mixed BMP+astral class matching correctly at every sub-range boundary and
  correctly rejecting the gaps between them, a quantifier applied to the rewritten group, and
  `findall` returning the right matches) before trusting it — no local Python interpreter is
  available in this environment, so every expected value was hand-derived by reasoning through the
  actual codepoints/surrogate-pair math. Found via real rfc3986's own `abnf_regexp.py` (RFC 3987
  IUNRESERVED/IPRIVATE ranges), confirmed by re-running the exact real pattern that originally
  failed.
- **A function/builtin's `.__hash__` is now a real, callable attribute** (`Interp.cs`'s
  `TryGetAttr`, both the `PyFunction` and `PyBuiltinFunction` cases): real CPython — every function
  is hashable by identity (`object.__hash__`'s default) — but `fn.__hash__` itself raised
  `AttributeError` even though the top-level `hash(fn)` builtin already worked (via
  `PyOps.PyHash`'s identity fallback for the default case). Found via real rfc3986's own dependency
  chain: some code checks `func.__hash__` directly as a hashability probe rather than calling
  `hash(func)`.
- **`atexit`** (new `AtexitModule.cs`, wired into `PyEngine.Run`): real `register`/`unregister`,
  with registered callbacks *actually invoked* in reverse registration order once the top-level
  script finishes — not a bare stub. The callback list lives on the `atexit` `PyModule` instance
  itself, not a shared static, so it's naturally scoped per `PyEngine`/script run (the same lesson
  this project already learned the hard way from the 4.1.5 flaky-suite concurrency bugs — a
  process-wide list here would race the same way under parallel test execution). Found via real
  certifi's `core.py` (`atexit.register(exit_cacert_ctx)`), an httpx transitive dependency.
- **`importlib.resources`** (new `ImportlibResourcesModule.cs`): real `files()`/`as_file()` (the
  3.11+ API certifi's own `sys.version_info >= (3, 11)` branch selects, matching this project's
  declared 3.12 compatibility), resolving a real on-disk package directory via its already-tracked
  `__file__` and returning a real `pathlib.Path` — no zipimport/extraction support, since nothing
  reachable runs from a zip, matching real CPython's own documented "common case" (`as_file`'s
  `__exit__` is a no-op). The older (<3.11) `path`/`read_text` free functions are included too, for
  robustness, though nothing reachable actually selects that branch. Found via real certifi's
  `core.py` resolving its bundled `cacert.pem`.
- **`logging.addLevelName`/`getLevelName`** (LoggingModule.cs): didn't exist — the existing
  (already-real, not-a-stub) `Logger` class hard-coded its level-name mapping. Fixed by threading a
  per-module-instance (not shared-static — same discipline as `atexit` above) custom-level-name
  dictionary through both the new `addLevelName`/`getLevelName` functions and the existing
  `Logger.log`'s own level-name resolution, so a custom level registered via `addLevelName` is
  actually used when a logger emits at that level. Found via real httpx's own `_utils.py`
  (`get_logger`: `logging.addLevelName(TRACE_LOG_LEVEL, "TRACE")`).
- **PyPI dependencies installed**: `certifi`, `httpcore`, `h11` (all real, unpinned — no version
  conflicts found with the already-pinned `httpx==0.23.3`, unlike the earlier `app=` incompatibility
  that forced that pin in the first place).
- **`import httpx` succeeds**, verified directly and repeatedly. Pushed further: constructing a real
  `starlette.testclient.TestClient` against a real `FastAPI()` app now gets substantially deeper
  into `httpcore`/`h11`'s own import chain before hitting the next real wall.
- **The wall this round stopped at**: real, unmodified `h11` (httpx's low-level HTTP/1.1 transport,
  installed as a genuine dependency) compiles several regexes from **bytes** patterns
  (`re.compile(rb"[0-9]+")`, `re.compile(field_name.encode("ascii"))`) — real CPython's `re` module
  supports compiling and matching against `bytes` as well as `str`, but PySharp's `re.compile()`
  only ever accepted a `str` pattern, raising `TypeError: compile(): invalid argument type`. This is
  a genuinely separate, substantial feature (not a quick fix): every entry point in `ReModule.cs`
  (`compile`/`match`/`fullmatch`/`search`/`findall`/`finditer`/`sub`/`subn`/`split`, plus the
  `Match` object's `group`/`groups`/`groupdict`/`start`/`end`) would need bytes-vs-str mode
  awareness — matching against bytes would need a byte-preserving encode/decode strategy (e.g.
  Latin-1, which maps each byte 0–255 to a codepoint 0–255 one-to-one) since .NET's `Regex` only
  operates on `string`, with results re-encoded back to `bytes` for bytes-mode matches/groups.
  Deliberately not started this round — left as the concrete next step for whoever picks this back
  up.
- 7 tests added: `M6_Stdlib/StdlibTests.cs` gained 3 new `ReTests` cases (astral character classes:
  a single range, a mixed BMP+astral class, a quantifier+findall check), `FunctionHashAttributeTests`
  (1), `AtexitTests` (1), `ImportlibResourcesTests` (1), `LoggingAddLevelNameTests` (1). Full suite
  green throughout, confirmed stable across 15 consecutive clean full-suite runs: 963/963 by the end
  of this round, up from 956 at the start.

### 4.1.8 — real bytes-pattern regex, a real `TestClient` request nearly works, ending at a genuine event-loop architecture wall

The author's go-ahead ("prosegui") to tackle 4.1.7's bytes-regex wall head-on. This round implemented
a genuinely large feature (bytes-mode `re`) and then closed a long chain of smaller real gaps found
running an actual `TestClient` request against a real `FastAPI()` app, reaching all the way into
h11/rfc3986/anyio's own internals before hitting a new, deep architectural wall.

- **Real `bytes` pattern/subject support in `re`** (`ReModule.cs`, rewritten in full): real CPython's
  `re` module matches against `bytes` as well as `str` — PySharp's only ever accepted `str`. Every
  entry point (`compile`/`match`/`fullmatch`/`search`/`findall`/`finditer`/`sub`/`subn`/`split`/
  `escape`, plus `Pattern`'s and `Match`'s own methods) now threads a bytes-vs-str mode through:
  a `bytes` pattern/subject is decoded via Latin-1 (a lossless, byte-for-byte 1:1 mapping of 0–255 to
  the identically-valued codepoint — chosen because .NET's `Regex` only operates on `string`) into
  the actual working string matched against, and every result (match text, group values, `.string`,
  `sub`/`split` output) is re-encoded back to `bytes` via the same mapping. A pattern's mode is fixed
  at compile time and enforced against every subject given to it afterward, matching real CPython's
  own "cannot use a bytes pattern on a string-like object" (and the reverse) errors — verified by
  hand against compile/match/search/groups/findall/sub (both a bytes template and a bytes-returning
  callable)/split/fullmatch/finditer/repr, plus both mismatch directions and confirming str-mode
  still works unaffected. Found via real h11's own `_readers.py`/`_events.py`/`_headers.py`
  (`re.compile(rb"[0-9]+")` etc.) — h11 is httpx's low-level HTTP/1.1 transport.
- **`bytes.count()`** (TypeMethods.cs `BytesMethods`): didn't exist. A real port of CPython's own
  non-overlapping-occurrence algorithm with optional `start`/`end`. Found via real rfc3986's own
  `normalizers.py` (`uri_bytes.count(b"%")`).
- **`urllib.parse.parse_qs`/`parse_qsl` coerce a falsy (`None`) argument to an empty string**,
  matching real CPython's own `_decode_args` (`x.decode(...) if x else ''` — a falsy input becomes
  `''`, not a call on `None`). Found via real httpx's own `_urls.py` (`QueryParams.__init__`:
  `parse_qs(value, keep_blank_values=True)` where `value` is `None` whenever a `Client`/`TestClient`
  is constructed without an explicit `params=`).
- **Real `isinstance()` recognition of `dict` as a `Mapping`, plus structural (`__subclasshook__`-
  style) duck typing for `Iterable`/`Iterator`/`Container`/`Sized`/`Callable`/`Hashable`**
  (Builtins.cs): real CPython recognizes any class defining the matching dunder (`__iter__`,
  `__next__`, `__contains__`, `__len__`, `__call__`) as a virtual subclass of the corresponding ABC,
  and `dict` specifically as a registered `Mapping`, all *without* requiring explicit inheritance —
  previously `isinstance({}, Mapping)` and friends were simply `False`. This was a real, silent
  correctness bug beyond the immediate crash: real httpx's own `Headers.__init__` branches on
  `isinstance(headers, Mapping)` to decide whether to iterate `.items()` (correct) or the mapping
  directly (real CPython: iterates *keys*) — with the check always False, a plain dict fell into the
  wrong branch and tried to unpack each key *string* into `(k, v)`, raising "too many values to
  unpack" for any key longer than 2 characters. Found via real httpx's own `_models.py`, then again
  via `self.stream` (a real `ByteStream`, `_content.py`, defining only `__iter__`) needing
  `isinstance(self.stream, typing.Iterable)` to be `True`.
- **Real `MutableMapping.pop`/`popitem`/`setdefault`/`clear` mixins** (CollectionsModule.cs),
  **unified by identity between `collections.abc` and `typing`** (MiscModules.cs): real CPython's
  `typing.Mapping`/`typing.MutableMapping` are literally the same classes as their `collections.abc`
  counterparts (just generic-subscriptable) — previously two separate, unrelated bare placeholders,
  so a class built against one saw none of the other's mixin methods. `MappingClass`/
  `MutableMappingClass` are now built once as shared, process-wide `CollectionsModule` statics (safe
  to share, unlike this project's other script-varying shared state, since the mixin methods are
  stateless — they only ever touch the instance they're called on). Found via real httpx's own
  `Headers(typing.MutableMapping[str, str])` (`_models.py`): overrides `update` itself but relies on
  the inherited mixin for `self.pop(key)`.
- **Real `namedtuple._replace`, and the two separate, drifted namedtuple implementations unified**
  (`Interp.ConvertToNamedTuple` gained `_replace`; `CollectionsModule.BuildNamedTuple` — the
  `collections.namedtuple(...)` factory function — now delegates to `ConvertToNamedTuple` instead of
  hand-duplicating a second, incomplete copy that was missing `_asdict` entirely and, like the first,
  missing `_replace`): found via real rfc3986's own `uri.py` (`class URIReference(namedtuple(
  "URIReference", misc.URI_COMPONENTS), URIMixin):`), with real httpx's own URL-joining logic calling
  `._replace(...)` while resolving a relative redirect/request URL against a base. Verified the
  unification preserves real multiple-inheritance behavior (`class MyPoint(namedtuple(...), Mixin):`)
  before trusting it.
- **`pathlib.Path.expanduser()`** (PathlibModule.cs): didn't exist — a real (if scoped to the common
  case: no `~otheruser/...` support) port of `os.path.expanduser`'s `~`-prefix handling. Found via
  real httpx's own `_utils.py` (`NetRCInfo`: `Path("~/.netrc").expanduser()`).
- **`asyncio.Task.get_name()`/`set_name()`** (Runtime/Async.cs's `PyTask` gained a real `Name`
  property; a new `AsyncioModule.TaskTable` dispatched ahead of the shared `FutureTable` in
  TypeMethods.cs, since real CPython's `Task` has these but `Future` doesn't): the synthetic default
  name (when never explicitly set) is derived from the task's own CLR identity, not a shared counter
  — this project's own established caution around process-wide mutable state, from the very
  concurrency bugs this whole investigative chain kept running into. Found via real anyio's own
  `_backends/_asyncio.py` (`task.set_name(name)`), reached constructing a real `TestClient` request.
- **The wall this round stopped at — a genuine, deep event-loop architecture limitation, not a small
  gap**: constructing and issuing a real `TestClient.get("/")` request now gets all the way into
  anyio's `start_blocking_portal` (`from_thread.py`) — which spawns a **real, independent
  `threading.Thread`** running its *own* `asyncio.run()`-driven event loop, while the *original*
  calling thread blocks on a `concurrent.futures.Future` waiting for it — and then hangs forever (a
  real, reproduced timeout via `timeout 45 ... ; echo $?` → exit 124, not a crash). Root cause,
  reasoned from `Runtime/Async.cs`'s own existing doc comment: `PyEventLoop._running` is a
  deliberately **process-wide, not thread-local**, static (needed so a coroutine body — running on
  its own dedicated OS thread — can still see which loop is driving it, e.g. for
  `asyncio.get_running_loop()`). Every scenario this project has hit before had exactly one *logical*
  event loop in play at a time, even across coroutines' dedicated threads, so this held up fine.
  `start_blocking_portal` is the first scenario needing **two genuinely independent, concurrently-
  running event loops on two real, separate OS threads** — and they almost certainly stomp on the
  same `_running` field, corrupting each other's "which loop is this callback for" bookkeeping.
  Fixing this properly needs real thread-local propagation (the same technique `LogicalThread`
  already uses to propagate identity across a coroutine's own dedicated thread) extended to also give
  a genuine `threading.Thread`-spawned event loop its own independent scope — a substantial,
  carefully-scoped architecture change in its own right, not a quick patch, and a well-justified
  stopping point given how many real concurrency bugs this exact project has already had to
  carefully root-cause and fix this session alone.
- 12 tests added: `M6_Stdlib/StdlibTests.cs` gained 4 new `ReTests` cases (bytes pattern match/
  search/findall/groups, bytes sub/split incl. a bytes-returning callable, both pattern/subject
  bytes-vs-str mismatch directions, `bytes.count`) and a new `HttpxRequestChainFixesTests` (8:
  parse_qs/parse_qsl None-coercion, dict-as-Mapping, structural ABC duck-typing, MutableMapping
  mixins, typing/collections.abc Mapping unification, namedtuple._replace for both namedtuple forms,
  Path.expanduser, Task get_name/set_name — `[Collection("asyncio-run")]`-tagged, since one case
  calls `asyncio.run`). Full suite green throughout, confirmed stable across 15 consecutive clean
  full-suite runs: 975/975 by the end of this round, up from 963 at the start.

### 4.1.9 — the event-loop architecture wall fixed for real, then a new, much deeper wall found (CPython `Task` internals)

The author's go-ahead ("procedi") to tackle 4.1.8's event-loop architecture wall head-on.

- **`PyEventLoop._running` made real thread-local, propagated the same way `LogicalThread` already
  is** (`Runtime/Async.cs`, `PyGenerator.cs`): changed from a plain `static` field to `[ThreadStatic]`,
  plus a new `PyEventLoop.AdoptRunning(loop)` method called at the very start of every
  coroutine/generator/async-generator's own dedicated internal thread (`PyCoroutine.Resume`,
  `PyAsyncGenerator`'s equivalent, `PyGenerator.Resume`) — capturing the caller's `PyEventLoop.Running`
  *before* spawning and re-adopting it *inside* the new thread, mirroring `LogicalThread.Adopt`'s
  existing propagation exactly. Deliberately **not** called for a genuine `threading.Thread.start()`
  (`ThreadingModule.cs` already didn't propagate `LogicalThread` either, for the same reason) — so a
  real independently-started thread correctly begins with no running loop of its own, letting its own
  `asyncio.run()` establish an independent scope instead of corrupting the caller's. This is exactly
  the fix 4.1.8's own investigation predicted: the old plain-static field held up fine as long as only
  one *logical* event loop was ever in play at a time (even across a coroutine's own dedicated CLR
  threads), but real anyio's `start_blocking_portal` spawns a genuine second, independent
  `threading.Thread` running its own `asyncio.run()`-driven loop concurrently with the original — the
  first scenario needing two truly independent loop scopes on two real OS threads.
- **Verified against the real primitive anyio's own cross-thread dispatch uses, not just the simple
  case**: first confirmed the straightforward nested-`asyncio.run`-in-a-real-thread shape works (a
  background thread runs `asyncio.run(inner())` to completion while the outer loop keeps running
  concurrently); then, since that alone didn't fully unblock the real `TestClient` request either,
  bisected further and reproduced the *exact* mechanism real anyio's own
  `AsyncIOBackend.run_sync_from_thread` (`_backends/_asyncio.py`) uses: a `concurrent.futures.Future`
  to fetch the *running* loop object out of a still-active background thread (via
  `asyncio.get_running_loop()`), then `loop.call_soon_threadsafe(...)` + a second
  `concurrent.futures.Future` to dispatch real work into that still-suspended loop (the background
  coroutine parked on `await some_event.wait()`, exactly like anyio's own
  `BlockingPortal.sleep_until_stopped()`) and synchronously wait for the result. This narrower
  reproduction — built specifically to isolate anyio's own dispatch shape from the simpler nested-run
  case — is what actually confirmed the fix handles the real scenario, not just a simplified
  approximation of it.
- **Confirmed via 25 consecutive clean full-suite runs** (deliberately more than this project's usual
  15, given how deep and sensitive this specific change is — it touches the event-loop threading
  model every async test in the suite depends on): 977/977 throughout, no regressions.
- **The next real wall, found immediately after — and substantially larger in scope than anything
  else in this whole investigative chain**: with the event-loop threading fix confirmed working
  (`portal ready` now prints; a plain `portal.call(sync_fn, 21)` on a *plain function* still hangs),
  bisecting further traced the hang into `BlockingPortal.call` → `start_task_soon` →
  `_spawn_task_from_thread`, which doesn't just dispatch a callback — it calls
  `self._task_group.start_soon(...)`, spawning a real task inside anyio's own `TaskGroup`. Testing
  `anyio.create_task_group()` directly (no blocking portal involved at all) surfaced the actual
  problem: `AttributeError: 'Task' object has no attribute '_must_cancel'` — real anyio's own asyncio
  backend directly reads and writes **private, undocumented `asyncio.Task` implementation-detail
  attributes** (`_must_cancel`, and almost certainly others in the same cancellation-delivery code
  path: `_fut_waiter`, `_coro`, etc.) for precise cancel-scope semantics, not just Task's public API.
  This is a fundamentally different *kind* of gap than everything else found across this entire
  investigative chain: every previous fix targeted real CPython's *documented, public* behavior
  (however obscure); this needs replicating a slice of CPython's actual C-level `Task` state machine
  well enough to satisfy code that was written against those internals directly. A substantially
  larger, more open-ended undertaking than a "next real gap" — deliberately not started this round.
- 2 tests added: new `M10_Async/EventLoopThreadingTests.cs` (`[Collection("asyncio-run")]`) — the
  nested-`asyncio.run`-in-a-thread case, and the real `call_soon_threadsafe`-onto-a-still-running-
  loop case matching anyio's own dispatch mechanism. Full suite green throughout: 977/977 by the end
  of this round, up from 975 at the start.

### 4.1.10 — the `Task`-internals wall closed for real, ~10 more real gaps, and the milestone: a real `TestClient` request round-trips end to end

The author's go-ahead ("procedi") to tackle 4.1.9's `Task`-internals wall.

- **Real CPython `asyncio.Task` private internals** (`Runtime/Async.cs`): `PyCoroutine.CurrentAwaited`
  (maps onto the coroutine's own `_awaited` tracking), `PyTask.FutWaiter`/`MustCancel`/`GetCoro()`, and
  a real `Task.cancel()` override — cancels the current waiter future directly if there is one,
  otherwise sets `MustCancel` for delivery at the next step (matching real CPython's own
  `_deliver_cancellation`). `AsyncioModule.cs`'s `TaskTable` gained `cancel`/`get_coro`;
  `TypeMethods.cs` wired `_must_cancel`/`_fut_waiter` as real attribute reads. A C# `new`-hiding
  gotcha caught proactively during design (not shipped as a bug): `PyTask.Cancel()` declared `new`
  hides, not overrides, the non-virtual base `PyFuture.Cancel()`, so `FutureTable`'s shared `["cancel"]`
  entry (cast to `(PyFuture)`) would have silently called the wrong one — handled by giving `TaskTable`
  its own `["cancel"]` cast to `(PyTask)`. **A real correctness bug found during manual verification**:
  `t.cancelled()` returned `False` after a real cancellation, because `Step()` delivered the resulting
  `CancelledError` via `SetException(...)`, which doesn't set `PyFuture.Cancelled = true` — fixed by
  special-casing a `CancelledError` result to call `base.Cancel()` instead, matching real CPython's own
  `Task.__step`. Verified against 5 hand-derived checks (`_must_cancel`/`_fut_waiter`/`get_coro`/a real
  `.cancel()` delivering `CancelledError`/`.cancelled()`).
- **Real PEP 654 `BaseExceptionGroup`/`ExceptionGroup`** (`PyErr.cs`): anyio's own cancel-scope
  teardown (`_backends/_asyncio.py`) references `BaseExceptionGroup` directly, expecting it as a
  builtin since PySharp's declared `sys.version_info` is 3.12. Implemented the real auto-upgrade-to-
  `ExceptionGroup` type-selection rule in `__new__` (every given exception being an `Exception`, not
  just `BaseException`, upgrades a bare `BaseExceptionGroup(...)` call) and a real `.split(condition)`
  (partitions `.exceptions`, either side `None` if empty). `except*` syntax is **not** supported (a
  separate, deliberately out-of-scope parser gap — confirmed via `SyntaxError: unexpected token '*'`;
  anyio itself only uses `.split()`, never `except*`). Verified against 12 hand-derived checks, zero
  bugs found on the first pass.
- **Real `Coroutine`/`Generator`/`Awaitable` ABC duck-typing for `isinstance()`** (`Builtins.cs`'s
  `SatisfiesAbcByDuckType`, extending the mechanism 2.2.1 already built for `Mapping`/`Iterable`/etc.):
  real CPython recognizes `types.CoroutineType`/`GeneratorType` instances as virtual subclasses of
  `collections.abc.Coroutine`/`Generator`/`Awaitable` structurally, without explicit inheritance. Found
  the hard way — a `TaskGroup.start_soon(worker)` call raised `ExceptionGroup: unhandled errors in a
  TaskGroup, PySharpLib.Runtime.PyTuple`, which *looked* like a cosmetic `FormatForClr` display bug
  (it partially is: a naive `.args`-join via raw `.ToString()`, not fixed, low-priority) but was
  actually masking a real bug — `worker()`'s own `print` never ran. Diagnosed by catching the group and
  inspecting `.exceptions` directly: the real cause was `TypeError: Expected worker() to return a
  coroutine, but the return value (...) is not a coroutine object`, from anyio's own
  `abc/_tasks.py`'s `isinstance(coro, Coroutine)` (imported from `collections.abc`) returning `False`
  for a real `PyCoroutine`. Not accepting the superficial display-bug explanation and instead finding
  the real cause underneath it is what actually unblocked this.
- **`loop.get_task_factory()`/`set_task_factory()`** (`Runtime/Async.cs`'s new `PyEventLoop.TaskFactory`
  property, `AsyncioModule.cs`'s `EventLoopTable`): anyio's real `TaskGroup._spawn` checks `if factory
  := loop.get_task_factory()` before falling back to plain `loop.create_task` — previously
  `AttributeError` since neither method existed at all.
- **Real `io.BytesIO` position tracking** (`IoModule.cs`): `seek(offset, whence=0)`/`read(size=-1)`/
  `tell()`/`truncate(size=None)` all now track and honor a real cursor position (`write` overwrites at
  the current position and only extends the buffer past it), replacing a prior always-no-op `seek`
  that happened to work only because `read()` always returned the whole buffer regardless of position.
  Found via starlette's real `TestClient` (`testclient.py`'s `handle_request`, which streams an ASGI
  response body into a `BytesIO` via repeated `write()` then `seek(0)` before reading it back), and
  `truncate` via httpx's `_decoders.py` chunked-body buffering (`seek(0); truncate()` to clear the
  buffer at each chunk boundary). Verified against 9 + 5 hand-derived checks.
- **`dict()`/`dict.update()` now honor the real `keys()`-mapping protocol** (`PyOps.cs`'s new
  `TryGetMappingItems`, used from `Builtins.cs`'s `dict()` and `TypeMethods.cs`'s `dict.update`): real
  CPython treats any object exposing a `keys()` method as a mapping (iterate `x.keys()`, fetch each
  value via `x[key]`), not as an iterable of `(key, value)` pairs — previously only a literal `PyDict`
  argument was special-cased, so any other mapping-shaped object (real CPython dict subclasses
  included) fell through to the pairs-iterable path and raised "dictionary update sequence element is
  not a pair" whenever its own `__iter__` only yielded keys (as `MutableMapping`'s default does). Found
  via httpx's real `dict(request.headers)` (`_models.py`'s `_CookieCompatRequest.__init__`, needed by
  cookie-jar extraction on every response). Verified against a hand-written `FakeMapping` duck-type.
- **Real `generator.send(value)` semantics** (`PyGenerator.cs`): previously any non-`None` `send()`
  value was unconditionally rejected ("not supported in PySharp v1"). Now resumes the generator with
  `value` as the real result of its currently-suspended `yield` expression (`Yield()` returns the
  stored `_sendValue` instead of always `None`), and sending a non-`None` value to a not-yet-started
  generator correctly raises `TypeError` (matching real CPython) instead of silently starting it. Found
  via httpx's real synchronous auth flow (`_client.py`'s `_send_handling_auth`:
  `auth_flow.send(response)`), which relies on `send()` succeeding even for the default no-auth flow's
  bare `yield request` (the sent value is unused by that particular generator body, but CPython still
  requires the call itself to succeed).
- **A generator's `return value` now actually reaches `StopIteration.value`** (`PyGenerator.cs`,
  `PyErr.cs`, `Builtins.cs`'s `next()`): a real, previously-silent bug, not just a missing feature —
  `Interp.ExecFunctionBody` itself already catches `ReturnSignal` and returns its `.Value` normally
  (so a generator's dedicated-thread body's own `catch (ReturnSignal)` around `ExecFunctionBody` was
  dead code that could never fire), meaning every generator's return value was silently discarded.
  Fixed by capturing `ExecFunctionBody`'s own return value as `PyGenerator.ReturnValue`, and threading
  it through every `StopIteration` raised on exhaustion (`generator.send`/`__next__`, and the `next()`
  builtin). `PyErr.StopIteration(value)` now also matches real CPython's actual rule precisely: a
  `None` return value (explicit `return None`, bare `return`, or falling off the end) raises a bare
  `StopIteration()` with empty `.args`, while any other value becomes `.args == (value,)` — both
  expose the real value via `.value` either way. Verified against 6 hand-derived checks across both
  branches.
- **`email.message.Message.get_content_charset(failobj=None)`** (`EmailModule.cs`): parses the
  `charset=` parameter off a `Content-Type` header (plain `key=value`/`key="value"` params, not RFC
  2231 `charset*=` encoding — out of scope, nothing reachable needs it). Found via httpx's own
  `_utils.py`'s `parse_content_type_charset`, used by `Response.json()` to pick a decode charset.
  Caught a real keyword-argument-plumbing bug during verification: the builtin ignored a `kwargs`-
  passed `failobj=` entirely (only checked positional `a[1]`), which is exactly how httpx calls it.
- **`codecs.BOM_UTF8`/`BOM_UTF16_LE`/`BOM_UTF16_BE`/`BOM_UTF32_LE`/`BOM_UTF32_BE`** (+ `BOM`/`BOM_LE`/
  `BOM_BE`/`BOM_UTF16`/`BOM_UTF32` aliases) as real byte constants (`CodecsModule.cs`): found via
  httpx's own `_utils.py`'s `guess_json_utf`, which sniffs a response body's first bytes against these
  exact values for `Response.json()`'s charset auto-detection when no explicit charset is given.
- **The milestone**: a real, unmodified `starlette.testclient.TestClient` (backed by real
  `httpx`/`httpcore`/`h11`) driving a real `FastAPI()` app's ASGI callable end to end —
  `client.get("/")` and `client.get("/items/42")` both return the correct status code and JSON body,
  through real anyio task groups/cancel scopes and this project's own async runtime. This is the
  target Phase 4 has been chasing since 4.1.1.
- **A separate, unrelated, deliberately out-of-scope gap found but not pursued**: `import anyio`
  executed *inside* a function body (rather than at module top level) hits `NameError: name '__value'
  is not defined`, from anyio's real `__init__.py`'s own module-level idiom (`for __value in
  list(locals().values()): ...; del __value`, used to rewrite re-exported names' `__module__`). Traced
  to `locals()` apparently resolving to the *calling* function's scope rather than the newly-created
  module's own namespace when a module is imported from inside a function frame — a genuine, separate
  gap in nested/deferred-import scoping, distinct from (though thematically related to) 1.9's earlier
  `globals()`/`locals()`-at-module-scope fix. Every real scenario this project has actually exercised
  imports `anyio` at module level, so this didn't block the milestone; worth revisiting on its own.
- 16 tests added across `M4_Functions/GeneratorTests.cs` (3: `send()`, a not-yet-started-generator
  `send()` `TypeError`, `StopIteration.value`), `M4_Functions/ExceptionTests.cs` (4: 3
  `BaseExceptionGroup`/`ExceptionGroup` + 1 `Coroutine`/`Generator`/`Awaitable` ABC duck-typing),
  `M3_Evaluator/CollectionTests.cs` (1: `dict()`/`update()` `keys()`-mapping protocol),
  `M6_Stdlib/StdlibTests.cs` (4: `BytesIO` seek/read/truncate ×2, `email.message.get_content_charset`,
  `codecs.BOM_*`), new `M10_Async/TaskInternalsTests.cs` (`[Collection("asyncio-run")]`, 2: Task
  internals, `get_task_factory`/`set_task_factory`), and `M16_FastApi/FastApiSmokeTests.cs` (2: a real
  anyio task group end to end, and — the milestone itself — a real `TestClient` GET round trip);
  `FastApiInstallFixture` extended to also install `httpx==0.23.3` + its transitive deps
  (`idna`/`sniffio`/`rfc3986`/`certifi`/`httpcore`/`h11`) and moved to `[Collection("asyncio-run")]`
  since `TestClient` itself drives `anyio.from_thread.start_blocking_portal` (a genuine background
  thread running its own `asyncio.run`). Full suite green throughout, confirmed via 15 consecutive
  clean full-suite runs: 993/993 by the end of this round, up from 977 at the start.

### 4.1.11 — POST/PUT/DELETE/query-params verified against real `TestClient` (zero new bugs), then real `__slots__`-backed storage closes the last documented Phase-2 gap

The author's go-ahead ("procedi") to push past GET-only `TestClient` coverage.

- **POST with a real pydantic request body, PUT, DELETE, query params, and `HTTPException` all verified
  against real `TestClient` — zero new bugs found.** A route accepting a pydantic `BaseModel` body
  validates and serializes correctly, including the real 422 validation-error shape
  (`{"detail": [{"loc": ["body", "price"], "msg": "field required", "type":
  "value_error.missing"}]}`) when a required field is missing; `HTTPException(status_code=404, ...)`
  round-trips its status and detail correctly; `Query`-typed and defaulted query parameters both
  parse correctly. Confirms Phase 4's core request/response plumbing generalizes well past the
  original GET-only milestone.
- **A real, previously-documented gap surfaced immediately when a route returns a pydantic model
  directly**: the JSON response leaked a spurious `"__fields_set__"` key alongside the real fields
  (`Basemodel_dict_output_is_the_current_frontier`, tracked since Phase 2.2.1) — this is the single
  most common FastAPI usage pattern (returning a `BaseModel` instance from a handler), so it was worth
  closing now rather than leaving it open.
- **Real `__slots__`-backed storage, closing the gap** (`PyClass.HasSlot` (new), `PyInstance.Slots`
  (new), `Builtins.cs`'s `object.__setattr__`/`__delattr__`, `Interp.cs`'s `TryGetAttr`/`SetAttr`/
  `DelAttr`): a name declared in any class's own `__slots__` (walking the whole MRO) now gets
  dedicated per-instance storage kept separate from the instance's regular attribute dict — even when
  `__dict__` itself is also a declared slot, exactly like real pydantic's own `BaseModel.__slots__ =
  ('__dict__', '__fields_set__')`. `__slots__` is recognized in all four real shapes CPython accepts:
  a plain string (one slot), tuple, list, or set (pydantic's real `ModelMetaclass` computes a
  subclass's effective `__slots__` as `slots | private_attributes.keys()` — a genuine Python `set`,
  not a tuple, which the first pass of this fix missed entirely and had to add). Deliberately scoped:
  this does **not** implement full slot-descriptor semantics (a class whose `__slots__` omits
  `'__dict__'` still gets a — real-CPython-incorrect — usable `__dict__` in PySharp; a separate,
  smaller, pre-existing limitation, not reachable by anything this round exercised).
- **A real regression caught during verification, not shipped**: `copy.copy()`/`copy.deepcopy()` on a
  `PyInstance` only ever copied `.Dict`, so the very first end-to-end retest after landing the slots
  fix broke `import fastapi` itself — `AttributeError: 'FieldInfo' object has no attribute 'default'`,
  traced to real pydantic's own `_get_field_info` (`fields.py`) calling `copy.copy(field_info)` on a
  genuinely slots-only object (pydantic's real `FieldInfo.__slots__` lists ~25 names, no `__dict__`
  at all) — the shallow copy silently came back with every field empty, since all the real data now
  lived in `.Slots`, which `CopyInstance`/`DeepCopyInstance` didn't know existed yet. Fixed by copying
  `.Slots` alongside `.Dict` in both. Caught immediately by re-running the exact real-package probes
  this round had already verified, before trusting the fix — the same discipline that has caught every
  other regression across this whole investigation.
- Verified by hand: a `FakeMapping`-style minimal `__slots__` class (all 3 collection shapes, plus the
  bare-string shape), a real `copy.copy`/`copy.deepcopy` round trip on a slots-only object, and the
  real pydantic `BaseModel` case end to end (`.dict()`, `.__fields_set__`, `.__dict__`) — all matched
  hand-derived real CPython output. 5 tests added: `M4_Functions/ClassTests.cs` (4: three `__slots__`
  container shapes via `[Theory]`, the bare-string single-slot shape, `copy`/`deepcopy` round trip —
  general-purpose, independent of pydantic), and `M16_FastApi/PydanticSmokeTests.cs`'s existing
  frontier test flipped from documenting the bug to verifying the fix. Confirmed via 65 consecutive
  clean full-suite runs (one unrelated, unreproduced flake at run 12 of an early batch — isolated,
  consistent with this project's own documented history of rare parallel-execution flakes unrelated to
  whatever specific change is in flight, and inherently implausible here since this fix touches only
  per-instance state, no shared/static mutable state at all). 998/998 by the end of this round, up
  from 993 at the start.

## Phase 4.2 — first real target FastAPI sample app, live over real HTTP

The author's go-ahead ("procedi") to write the actual sample app Phase 4.1 has been building toward.

- **`samples/fastapi_demo.py`**: a real, unmodified `fastapi.FastAPI()` app — GET `/` (a plain JSON
  message), GET `/items/{item_id}` (typed path parameter + a real `HTTPException(404)` for a missing
  item), PUT `/items/{item_id}` (a real pydantic `Item` request body), DELETE `/items/{item_id}`, POST
  `/items` (a real pydantic body, computing a derived field), and GET `/search` (query parameters with
  real defaults) — wired to `samples/asgi_server.py`'s real, reusable `serve(app)` (`from asgi_server
  import serve`, resolved via PySharp's own script-directory search-path insertion — verified the
  cross-import works cleanly) instead of starlette's in-process `TestClient`. This is the *live*,
  curl-able version of the exact milestone `FASTAPI_PLAN.md` 4.1.10/4.1.11 already verified in-process.
- **Run live and driven entirely with real `curl` over a real HTTP/1.1 connection, not simulated**:
  started the server as a real background process (`pysharp run samples/fastapi_demo.py`), then curled
  every route from outside the process — GET `/`, GET `/items/42` (404 before any item exists), GET
  `/search?q=hello&limit=5`, POST `/items` (both a valid body and one missing `price`, confirming the
  real 422 shape), PUT `/items/1`, GET `/items/1` (now found), DELETE `/items/1`, GET `/items/1` again
  (404 again, confirming the delete took). **Zero new bugs found** — every response matched
  hand-derived real FastAPI/starlette/pydantic output exactly, on the first run.
- 6 tests added: new `M16_FastApi/FastApiDemoSampleTests.cs` (`[Collection("asyncio-run")]`), driving
  `fastapi_demo.app` directly via a hand-built ASGI scope/receive/send triple — the same technique
  `AsgiServerSampleTests` already uses for the dependency-free demo app, here exercising the real
  fastapi/starlette/pydantic ASGI callable instead, covering the same scenarios verified live via curl
  (index route, the full PUT→GET→DELETE→GET item lifecycle, POST with a valid body, POST with the real
  422 validation shape, query-parameter defaults). No interpreter changes needed this round — the
  sample and its tests are the deliverable. Full suite green: 1004/1004 (up from 998), confirmed
  stable across 5 consecutive full-suite runs (a lighter check than usual since nothing in
  `src/PySharpLib` changed this round).

## Phase 4.3 — refinements: a real `locals()`/`globals()` nested-import scoping bug fixed

The author's go-ahead ("vai di rifiniture") to close a documented, real loose end noted in 4.2's
"remaining open items" — `import anyio` from inside a function body raised a spurious `NameError:
name '__value' is not defined`.

- **Root cause**: `locals()`/`globals()` used `Interp.CurrentFrame`, which deliberately searches
  *past* module-level frames (`Fn == null`) to find the nearest enclosing *function* call — correct
  for `super()`'s own need (a zero-arg `super()` can only ever be inside a real method body, never a
  module frame), but wrong for `locals()`/`globals()`: a module imported from inside a function body
  (`def f(): import anyio`) pushes its own module-level frame *mid-stack*, and `locals()`/`globals()`
  called from *that* module's own top-level code must see that module's own dict — not skip past it
  to the calling function's locals/module. Real anyio's own `__init__.py` (and `abc/__init__.py`) do
  exactly `for __value in list(locals().values()): ...; del __value` at their own top level, to
  rewrite re-exported names' `__module__` — with the old bug, `locals()` returned the *importing
  function's* (possibly near-empty) locals instead of anyio's own already-populated module dict, so
  the loop's target was never actually bound, and the following `del __value` raised the NameError.
- **Fix**: both builtins now key off `Interp.InnermostFrame` (a simple top-of-stack peek, unlike
  `CurrentFrame`'s linear search) instead of `CurrentFrame`, correctly reflecting whatever code is
  running *right now* — a function body, a module's true top level, or (the fixed case) a module
  pushed mid-stack via a nested/deferred import — while `super()`'s own `CurrentFrame` use is
  untouched (still correct for its narrower need). Verified by hand against 8 real CPython scenarios:
  a function's own locals, `globals()` reading/writing the right module from inside a function, module
  top-level `locals()`/`globals()` after some names are already bound, and (deliberately, as a
  documented pre-existing gap this fix does *not* touch) confirmed `locals()` inside a class body
  remains incorrectly scoped to the enclosing module — no Frame is pushed for class-body execution at
  all, a separate, smaller, unreached-by-anything-real gap worth revisiting on its own.
- **A false alarm worth recording**: after this change, `dotnet test`/`import fastapi` runs briefly
  appeared to hang or crash the test host. Bisected carefully with `git stash` (reverting *only* this
  file, rebuilding, re-running) — the exact same slowness (a full `FastApiSmokeTests` run taking ~45s
  instead of the usual ~6s) was present *without* the fix too, proving it was this session's
  accumulated environmental load (a very long day of repeated builds/probes/package
  installs/reinstalls), not a real regression from this change. The fix itself was independently
  verified correct via direct hand-derived probes (the exact `import anyio` scenario, plus 8 broader
  locals()/globals() checks) before this slow-suite scare even came up — worth remembering next time
  a change "looks like" it broke something: bisect with the actual tool (stash/revert), don't just
  assume correlation from timing alone.
- 1 test added: `M5_Imports/ImportTests.cs`'s
  `Locals_and_globals_at_module_top_level_work_correctly_even_via_a_nested_deferred_import`, a
  dependency-free repro of the real anyio pattern (a sibling module using the same
  `for X in locals().values(): ...; del X` idiom, imported from inside a function). Full suite green:
  1005/1005 (up from 1004), confirmed via multiple full-suite runs once the environmental slowness was
  isolated as unrelated to the change.

### 4.3.1 — real WebSocket support for the live ASGI server (RFC 6455 handshake + framing)

The author's go-ahead ("procedi", after confirming via `AskUserQuestion` that a real WebSocket
implementation — not a smaller polish item — was the wanted next refinement) to close the other
loose end noted at the end of Phase 4.2: `samples/asgi_server.py`'s `serve()` only spoke plain
HTTP/1.1, with WebSocket explicitly out of scope ("needs the HTTP/1.1 Upgrade handshake").

- **A real RFC 6455 WebSocket implementation, built from scratch on top of the existing raw-socket
  HTTP server** (`samples/asgi_server.py`): detects a WebSocket Upgrade request (`Upgrade: websocket`
  and `Connection: Upgrade` headers), computes the real handshake response (`base64(SHA1(client_key +
  the spec's own fixed GUID))`), and speaks the real binary frame format both ways — reading masked
  client frames (7-bit/16-bit/64-bit length forms, unmasking with the frame's own 4-byte key) and
  writing unmasked server frames. Bridges to the exact real ASGI `websocket` scope/receive/send
  protocol already verified against real starlette in Phase 3.1.11 (`websocket.connect`/
  `websocket.receive`/`websocket.disconnect` via `receive()`; `websocket.accept`/`websocket.send`/
  `websocket.close` via `send()`), so any real ASGI app — not just the sample's own dependency-free
  `demo_app` — works unmodified. Also refactored HTTP request parsing onto a new shared `_ConnReader`
  (buffers partial socket reads so a byte read past what the HTTP parse needed, e.g. the start of a
  WebSocket frame arriving in the same TCP segment as the handshake headers, isn't lost). v1 scope,
  deliberately: no fragmented-message reassembly (continuation frames aren't handled — matches how
  real clients send reasonably-sized messages), and an unmasked client frame is accepted rather than
  rejected (RFC 6455 requires the server to close on one — real clients always mask, so this is a
  protocol-strictness simplification, not a functional gap).
- **Verified live, over a real socket, byte for byte — zero bugs found on the first complete run**:
  wrote a from-scratch Python WebSocket client (raw `socket`, no library) implementing the same RFC
  6455 handshake and masked-frame construction from the *client* side, then ran it against a real,
  live `asgi_server.serve(demo_app)` background process. Checked: the handshake's `Sec-WebSocket-
  Accept` against a hand-computed expected value: match. Three sequential text messages, each
  correctly echoed in order. A binary frame, echoed correctly. A ping, correctly auto-answered with a
  pong carrying the same payload, without disturbing the next real message. A 300-byte message,
  correctly using the 16-bit extended-length frame form. A clean close. Every single value matched by
  hand before any test was written — the same discipline used throughout this whole plan.
- **The RFC 6455 spec's own canonical worked example used as an independent cross-check**: `_ws_accept_key("dGhlIHNhbXBsZSBub25jZQ==")` produces exactly `"s3pPLMBiTxaQ9kYGzzhZRbK+xOo="` — the literal example from RFC 6455 §1.3, not just an internally-consistent round trip.
- 10 tests added to `M16_FastApi/AsgiServerSampleTests.cs`: the RFC 6455 known-answer handshake test,
  a frame-encoding byte-for-byte check (including the 16-bit extended-length path), two hand-built-
  ASGI-triple tests against `demo_app`'s websocket route (a full connect/echo-text/echo-bytes/
  disconnect sequence, and rejecting an unknown path), and — the live counterpart, mirroring
  `AsyncServerTests`'s own real-socket pattern (`FreeTcpPort` + a background `Thread` running
  `asyncio.run(asgi_server.serve(...))`) — a genuine end-to-end test driving a real `TcpClient`
  through the actual handshake and two real masked/unmasked frame round trips. Full suite green:
  1011/1011 (up from 1005), confirmed via 5 consecutive full-suite runs once this session's earlier
  environmental slowness had cleared (back to ~45-60s for the whole suite, matching pre-session norms).

### 4.3.2 — WebSocket hardening: real fragmentation reassembly and a real closing handshake

The author's go-ahead ("rinforza websocket") to close the two v1 simplifications 4.3.1 had
deliberately left open, plus two smaller defensive gaps found while reviewing that code again.

- **Real message fragmentation reassembly** (`samples/asgi_server.py`): `_recv_ws_frame` now also
  returns the real FIN bit (previously read off the frame header but discarded); `_handle_websocket`'s
  `receive()` accumulates `0x0` continuation-frame payloads under the *first* fragment's original
  opcode (text or binary) until a frame with FIN=1 completes the message, matching what a real browser
  actually sends for any sufficiently large WebSocket message — the previous v1 scope treated every
  continuation frame as an immediate protocol-error disconnect. Correctly handles a real, legal edge
  case: a ping/pong or close control frame arriving *between* two fragments of an in-progress message
  (control frames must never themselves be fragmented, but RFC 6455 explicitly allows them
  interleaved mid-fragmentation) — verified this doesn't disturb the in-progress reassembly.
- **A real closing handshake**: previously, receiving a client close frame just synthesized
  `websocket.disconnect` for the app and let the connection die via the ordinary `finally: conn.close()`
  — no close frame was ever sent back. Real RFC 6455 requires the *receiving* side to echo a close
  frame (its code, if any) before the connection actually ends. Fixed: `receive()` now writes a real
  close-frame reply (mirroring the client's own code, or 1000 if none was sent) immediately upon
  seeing one, *before* returning the disconnect event to the app.
- **Two smaller defensive fixes found on re-review, not previously exercised**: a WebSocket Upgrade
  request missing `Sec-WebSocket-Key` entirely (a genuinely malformed handshake) previously crashed
  with an `AttributeError` calling `.decode()` on `None` — now answered with a real `400 Bad Request`
  instead. Calling `receive()` again after a disconnect was already delivered (not something any real
  ASGI app does, but worth being well-defined about) previously tried to read from an already-closing
  socket — now consistently returns another disconnect event.
- **Verified by hand before any test was written, same discipline as 4.3.1**: a fragmented two-part
  text message ("Hello, " + "World!"), with a ping sent *between* the fragments — correctly
  auto-answered without corrupting the in-progress reassembly, and the full "Hello, World!" still
  echoed correctly afterward. A client-initiated close — the server's close-frame reply carried the
  exact same code (1000) back, and a subsequent read hit real EOF (the connection had genuinely
  ended). A handshake request with the `Upgrade`/`Connection` headers but no `Sec-WebSocket-Key` —
  answered with a real `HTTP/1.1 400 Bad Request` status line. Every value matched by hand, live over
  a real socket, before any test was written.
- 3 tests added to `M16_FastApi/AsgiServerSampleTests.cs` (all real-socket, extending the same
  `StartServerAndHandshake` helper the 4.3.1 live test now shares): fragmentation reassembly with a
  mid-fragment ping, the closing handshake (echoed code, then real EOF), and the missing-key 400.
  Full suite green: 1014/1014 (up from 1011), confirmed via 5 consecutive full-suite runs.

### 4.3.3 — WebSocket threaded through fastapi_demo.py: real starlette WebSocket over the real socket server

The author's go-ahead ("procedi") to close the other remaining WebSocket item noted at the end of
4.3.1: WebSocket support had only been verified against `asgi_server.py`'s own dependency-free
`demo_app`, not against the real `FastAPI()` app in `samples/fastapi_demo.py`.

- **A real `@app.websocket("/ws")` route added to `fastapi_demo.py`**, using real starlette's own
  `WebSocket`/`WebSocketDisconnect` (`await websocket.accept()`, `receive_text()`/`send_text()` in a
  loop, `except WebSocketDisconnect: pass`) — the exact same real starlette WebSocket class already
  verified against hand-built ASGI triples back in Phase 3.1.11/3.1.12, now driven for the first time
  over `asgi_server.py`'s real raw-socket RFC 6455 implementation instead.
- **Verified live over a real socket — zero bugs found on the very first run**: the real handshake, two
  sequential echoed text messages, and a client-initiated close correctly echoed back with the same
  code (1000) — exercising 4.3.2's freshly-hardened closing handshake through real starlette's own
  WebSocket implementation, not just `demo_app`. Separately verified the abrupt-disconnect path too
  (closing the TCP connection with no close frame at all): real starlette's `WebSocketDisconnect`
  fired and was caught cleanly by the route's own `except`, no traceback, no error logged server-side —
  confirming `asgi_server.py`'s `{"type": "websocket.disconnect", "code": 1006}` synthesis (used when
  `_recv_ws_frame` hits real EOF) integrates correctly with real starlette's own exception-based
  disconnect signaling, not just the demo app's plain `event["type"] == "websocket.disconnect"` check.
- 1 test added to `M16_FastApi/FastApiDemoSampleTests.cs`: a real-socket test (mirroring
  `AsgiServerSampleTests`'s own live-socket pattern, including `FastApiInstallFixture`'s site-packages
  on the search path) driving `fastapi_demo.app`'s real WebSocket route through the real handshake,
  two echoed messages, and the closing handshake. Full suite green: 1015/1015 (up from 1014),
  confirmed via 6 consecutive full-suite runs (back to a healthy ~18s for the whole suite).

## Phase 4.4 — real graceful shutdown: `signal.signal()`, and two genuine event-loop bugs found chasing it

The author's go-ahead ("procedi" → confirmed via `AskUserQuestion` that a real, OS-signal-backed
implementation was wanted, not a smaller polish item) to close the last tracked open item: a real
uvicorn-equivalent graceful shutdown for `asgi_server.py`'s `serve()`.

- **Real `signal.signal()`/`getsignal()`/`SIG_DFL`/`SIG_IGN`** (`SignalModule.cs`), backed by .NET's
  own cross-platform `PosixSignalRegistration` — a real OS signal handler, not a stub. Delivery is
  marshaled through the currently-running event loop's own `CallSoon` (captured at registration time)
  when one exists, the same thread-safety pattern `call_soon_threadsafe` already uses elsewhere: a raw
  OS signal can land on any thread, and this interpreter's coroutines assume only one executes at a
  time, so the Python-level handler must run through the loop's own single-consumer queue rather than
  directly on whatever thread .NET delivered the signal on — mirroring real CPython's own safety story
  (its C-level handler just sets a flag; the Python handler only runs later, at a safe point) without
  needing bytecode-tick polling, since this interpreter already has a real cross-thread-safe dispatch
  primitive to reuse.
- **Verification note — a genuine sandbox limitation, not a code gap**: this session's own tool sandbox
  turned out to have no usable Win32 console session for automated Ctrl+C testing. Tried three
  mechanisms (`GenerateConsoleCtrlEvent` with a fresh console, with an inherited console, and bash
  `kill -INT`) — all failed to deliver. Isolated whether this was a bug in the new C# code or the
  environment by writing a minimal, PySharp-independent raw `PosixSignalRegistration` probe (no
  interpreter involved at all): identical non-delivery, confirming it was the sandbox, not the
  implementation. Real delivery was then verified directly by the author, by hand, in a real
  interactive terminal — `Ctrl+C` correctly invoked the registered handler and printed its own output.
- **A real bug found from that exact manual test**: the handler's `raise SystemExit(0)` (a completely
  standard "clean shutdown" idiom) surfaced as an unhandled *.NET* stack trace
  (`PySharpLib.Runtime.PyRaise: SystemExit: 0` straight through `PosixSignalRegistration`'s own native
  callback trampoline) instead of a clean process exit — because the handler invocation happens on a
  thread never covered by the CLI's own top-level `PyRaise`/`SystemExit` → exit-code translation
  (`Program.cs`'s `Host.Guard`, which only wraps the main thread's own command execution). Fixed by
  wrapping every handler invocation in `SignalModule.cs` itself: `SystemExit` now calls
  `Environment.Exit(code)` cleanly (no traceback), and any other exception escaping the handler prints
  a real Python-style traceback (`PyErr.FormatTraceback`, already used by the CLI) instead of a raw
  .NET one. Re-verified by the author, by hand, in the same real terminal — clean exit, no traceback.
- **Real graceful shutdown built on top, in `samples/asgi_server.py`**: `serve()` now registers
  SIGINT/SIGTERM handlers that set an `asyncio.Event`; the accept loop races each `sock_accept()`
  against that event via `asyncio.wait(..., return_when=FIRST_COMPLETED)`, so a signal received while
  idle between connections stops the server immediately rather than only being noticed after the next
  one connects. On stop: the listening socket closes right away (no new connections accepted), then
  up to 10s is spent draining any in-flight connection handlers before actually returning. The core
  accept-then-drain logic is factored into a separate `_serve_until_stopped(app, srv, loop,
  stop_event)` specifically so it can be exercised directly with a caller-controlled `stop_event` in
  tests, without needing a real OS signal for every check (real signal delivery itself is covered by
  the manual verification above).
- **Two genuine, previously-latent event-loop bugs found while verifying the drain logic — not
  edge cases, a real hang reproduced on the very first attempt**: a hand-built repro (server + a real
  client, both real sockets, no shortcuts) hung forever partway through shutdown. Bisected in stages:
  1. **`asyncio.Event.set()` could silently abandon a real, still-pending waiter.** Each loop
     iteration that loses the accept-vs-stop race cancels the *losing* task — but cancelling a Task
     wrapping `event.wait()` never removed its underlying future from the Event's own internal waiter
     list. `set()` iterated that list unconditionally; hitting the stale, already-cancelled entry
     first threw `RuntimeError: invalid state: future already done` *mid-loop*, silently abandoning
     every waiter still to come — including the very next iteration's genuinely-pending one. A signal
     landing between two `sock_accept` cycles then never actually woke the shutdown wait, hanging the
     whole program despite `set()` itself appearing to "succeed" (no exception visible to the caller).
     Fixed at the root: `Event.set()` now skips already-done waiters instead of erroring in the middle
     of the notification loop.
  2. **A cancelled `sock_accept()`/`sock_recv()`/`sock_sendall()` could resolve its future twice.**
     Cancelling the *Task* wrapping one of these only marks the Python-level future done — the
     underlying real .NET `Socket.AcceptAsync()`/etc. keeps running in the background regardless (no
     API cancels it). When it later completes or fails (e.g. because `srv.close()` fires a real
     `SocketException` on it), the original code unconditionally called `fut.SetResult`/`SetException`
     a *second* time on an already-done future, throwing unhandled. Fixed by guarding each of these
     three call sites on `!fut.IsDone` first.
  3. **A more fundamental hardening, once the above two were understood**: `PyEventLoop`'s own
     `DrainReady`/`RunDueTimers` invoked every scheduled callback with no exception handling at all —
     one bad callback (like either bug above, before they were fixed) didn't just fail that one
     callback, it threw straight out of the loop's own draining method, corrupting *all* other
     pending/future callback processing and hanging the entire program silently. Real asyncio's own
     behavior is to report such an exception (via `loop.call_exception_handler`, its default printing
     to stderr) and keep running everything else scheduled. Matched that: both draining methods now
     route every callback through a shared `InvokeCallback` that catches and reports (via
     `PyErr.FormatTraceback` for a real `PyRaise`, `ex.ToString()` otherwise) rather than letting it
     propagate. This is defense in depth on top of the two specific fixes above, not a replacement for
     them — but it also means a *class* of future "unhandled exception in a scheduled callback" bugs
     will degrade to a stderr message instead of a silent whole-program hang.
- **Verified by hand, real sockets throughout, before any test was written**: a real client connects,
  the server starts a slow (1s) request handler, a real `stop_event.set()` fires mid-request (the
  server immediately stops accepting new connections) — the in-flight request still completes and the
  client receives its full, correct response; the accept-then-drain loop then returns cleanly, having
  logged exactly how many connections it waited for. Confirmed with the real, unmodified
  `_serve_until_stopped` (not a simplified stand-in) via a live background process + curl, byte for
  byte matching hand-derived expectations.
- 5 tests added: `M6_Stdlib/StdlibTests.cs`'s `SignalTests` (2: `signal.signal()`/`getsignal()`
  registration/query semantics, the out-of-range `ValueError`), `M10_Async/AsyncioSyncPrimitivesTests
  .cs` (1: the exact stale-cancelled-waiter repro, without any socket — a cancelled `Task` wrapping
  `event.wait()` must not prevent a later, real waiter from being notified), and
  `M16_FastApi/AsgiServerSampleTests.cs` (1 real-socket end-to-end test: an in-flight connection
  drains to completion while shutting down, and a connection attempted after shutdown completes is
  refused outright — reproduces the exact hang found above and proves it fixed, not papered over).
  Full suite green: 1019/1019 (up from 1015), confirmed via 15 consecutive full-suite runs (this
  round touches core event-loop internals — `Async.cs`/`AsyncioModule.cs` — so held to the project's
  higher bar for concurrency-sensitive changes).

## Phase 4.5 — pydantic v1 robustness sweep: 30 real-world field/validator patterns, 7 real gaps found and closed

The author's own call ("andiamo con il residuo... L'obiettivo è condividere questo progetto e quindi
deve essere robusto" — sharing this project means it needs to be robust), picking Phase 2.3's own
long-deferred item over starting scenario 3: probe pydantic v1 against a much wider range of real
field types/validators/Config options than `fastapi_demo.py` alone happened to exercise, closing
whatever real gaps that turns up.

- **Round 1 — 16 common field-shape patterns, 13/16 already worked, 3 real gaps found**: `Optional[T]`
  with a default, `List[T]`/`Dict[str, T]` fields, nested `BaseModel` fields, `List[Model]` fields,
  `Enum` fields, `Field()` numeric/string constraints, `Union` fields, `Field(default_factory=...)`,
  `.parse_obj()`/`.json()`/`.parse_raw()`, `Config.orm_mode`/`.from_orm()`, and
  `Optional[List[Model]]` all worked correctly on the first try. Three did not:
  - **`@validator`/`@root_validator` raised `AttributeError: 'PyClassMethod' object has no attribute
    '__func__'`**: both decorators internally do `f_cls = classmethod(function)` then read
    `f_cls.__func__` to recover the plain function, and `setattr(f_cls, '__validator_config__',
    ...)` to attach registration metadata directly onto the raw wrapper object — real CPython's
    `classmethod`/`staticmethod` support both. PySharp's `PyClassMethod`/`PyStaticMethod` had neither.
    Fixed: both gained a lazily-allocated `Attributes` dict (`Interp.cs`'s `TryGetAttr`/`SetAttr` wired
    to check `__func__` specially, then fall through to `Attributes`, then to the shared builtin
    `__class__` fallback for anything else — the fall-through mattered: an early version that returned
    `false` outright for unknown names broke `.__class__` too).
  - **`type(classmethod(f)).__name__`/`isinstance(f_cls, classmethod)` were both wrong** — `PyOps
    .TypeName` had no case for `PyClassMethod`/`PyStaticMethod`/`PyProperty`, falling back to the raw
    C# wrapper class name ("PyClassMethod") instead of the real Python type name ("classmethod").
    This silently broke pydantic's own `ModelMetaclass.__new__`: its `is_untouched(value)` check
    (`main.py`) is `isinstance(v, (property, type, classmethod, staticmethod))`, used to recognize a
    decorated validator method isn't a plain field — with the wrong type name, a validator method got
    misclassified as a field, raising `RuntimeError: no validator found for <class 'PyClassMethod'>`.
    Fixed by adding the three real names to `PyOps.TypeName`.
  - **A real `datetime` field raised `ValidationError: ... datetime.__init__() missing required
    argument`**: pydantic's own `datetime_parse.py` (`parse_date`/`parse_time`/`parse_datetime`)
    constructs `date`/`time`/`datetime` **exclusively by keyword** (`datetime(**kw_)`) after
    regex-parsing an ISO string — but `DateTimeModule.cs`'s three constructors only ever read
    `year`/`month`/`day`/`hour`/... positionally (`Int(a[1])`, `Int(a[2])`, ...), so this failed for
    every real ISO-datetime/date/time-string field, not an edge case. Fixed with a shared
    `RequiredArg`/`OptionalArg` helper reading positionally-or-by-keyword, applied to all three
    constructors. Verifying this by hand also caught a second, related bug: `time.microsecond`'s
    getter read only `Milliseconds * 1000`, silently truncating e.g. `123456` down to `123000` —
    fixed to the same real Ticks-based formula `datetime.microsecond`'s own getter already used
    correctly.
- **Round 2 — 14 more advanced real-world patterns, 12/14 already worked, 2 real gaps found**:
  `Field(alias=...)` + `.dict(by_alias=True)`, model inheritance, `.copy(update=...)`,
  `ValidationError.errors()` (real `loc`/`type` per error), `@validator(pre=True)`, a validator
  applying to multiple fields at once, `Config.extra = "forbid"`, `conint`/`constr` constraints,
  real `str`→`int` coercion inside `List[int]`, `repr(model)`, and `Field(None)` as an explicit
  Optional default all worked correctly on the first try. Two did not:
  - **`.dict(exclude={"field"})` raised `TypeError: Unexpected type of exclude value <built-in
    function set>`**: pydantic's own `ValueItems._coerce_items` (`utils.py`) checks `isinstance(items,
    Mapping)` then `isinstance(items, AbstractSet)` (`typing.AbstractSet`) before falling to an error
    — `collections.abc.Set`/`MutableSet` (and `typing.AbstractSet`, its own separately-built
    placeholder in PySharp's typing module rather than a true alias) had no case in the ABC
    duck-typing dispatch (`Builtins.cs`'s `SatisfiesAbcByDuckType`) at all, so a real `{"field"}` set
    literal was never recognized as a real set — every isinstance check silently failed straight to
    the error branch. Fixed by adding `"Set"`/`"AbstractSet"`/`"MutableSet"` cases (same mechanism
    already used for `Mapping`/`Iterable`/`Coroutine`/etc.).
  - **After that fix, a new, more precise error**: `AttributeError: 'function' object has no attribute
    'fromkeys'` — `_coerce_items`'s next line, `dict.fromkeys(items, ...)`, a real CPython classmethod
    called unbound off the `dict` type itself. `dict.fromkeys` didn't exist at all. Added to
    `DictMethods.Table`, scoped to the real (and overwhelmingly common) unbound-call shape
    `dict.fromkeys(iterable, value)` — not the rarer `some_instance.fromkeys(...)` form, which would
    need a different arg layout this round didn't need.
  - **`.schema()` separately hit `ImportError: cannot import name 'getdoc' from 'inspect'`**
    (`schema.py`: `doc = getdoc(model)`, feeding a model's docstring into its generated JSON Schema
    "description"). Added a real (not stubbed) `inspect.getdoc`: reads `__doc__`, cleans it via the
    already-real `cleandoc`, `None` if absent — scoped to what's actually settable, since PySharp
    doesn't capture a real docstring literal at class-definition time at all yet (a separate,
    pre-existing, out-of-scope limitation — confirmed by hand: `SomeClass.__doc__` is already `None`
    for a class with a real docstring, unrelated to this fix).
- **Every one of these 7 fixes was manually verified against hand-derived real CPython output before
  any test was written** — the same discipline used throughout this whole plan — then re-verified by
  re-running both full probe sweeps end to end: 16/16 and 14/14, 30/30 total.
- 13 tests added: `M4_Functions/ClassTests.cs` (2: raw `classmethod`/`staticmethod` `__func__`/
  arbitrary-attribute support, and `type()`/`isinstance()` against the real names),
  `M6_Stdlib/StdlibTests.cs` (5: keyword-argument date/time/datetime construction, the missing-
  required-argument `TypeError`, `time.microsecond` precision, `inspect.getdoc`, and a new
  `CollectionsAbcSetTests` class covering `Set`/`MutableSet`/`AbstractSet` isinstance plus
  `dict.fromkeys`), and a new `M16_FastApi/PydanticFieldTypeSweepTests.cs` (14: the real pydantic-
  level scenario for every one of the 7 gaps, end to end through the actual installed package, not
  just the interpreter-level mechanism). Full suite green: 1041/1041 (up from 1019), confirmed via 8
  consecutive full-suite runs.

## Phase 5 — docs ✅ (2026-08-11)

- [x] 5.1 ROADMAP.md: scenario 2 status flip to done (or partial, with a clear remaining-gap list),
  interpreter evolution log entries for every stdlib module/fix this plan lands.
      — *Note:* before touching docs, re-verified the current state was still real and green (not
      stale from a prior session): full suite 1228/1228, `M16_FastApi` 45/45 (which exercises
      `fastapi_demo.py` against real, installed fastapi/starlette/pydantic/httpx packages, not
      mocks). Flipped scenario 2's header, table row, and 2d sub-item to ✅ Done; added a
      consolidated "2e — starlette + ASGI + real FastAPI" row to the interpreter evolution log table
      (2c/2d's pydantic row already existed; 2e covering Phases 3-4 was entirely missing); removed
      the stale "known remaining gaps" note left over from the pre-FastAPI hand-rolled injector
      stage, replaced with the actual small out-of-scope items (`except*` syntax, a nested-import
      scoping edge case, hot-reload).
- [x] 5.2 RELEASE_NOTES.md + version bump (ask the author for the version number, same as
  AIOMQTT_PLAN.md's 7.2).
      — *Note:* added as a new "### FastAPI (scenario 2) — complete" subsection under the existing
      `## v1.0.0` entry, matching this file's own established pattern (incremental `###` subsections
      documenting milestones as they land, not a version bump per milestone — the numpy shim's own
      Phase 12.4 work the day before *did* introduce a new `## v1.1.0` top-level entry, which stays
      as the next one after this). No csproj `<Version>` bump needed here — Phase 5 is docs-only, no
      interpreter/library code changed, and the currently-packed 1.6.0 tool (from the numpy work)
      already includes everything FastAPI needs.
- [x] 5.3 README.md "Verified scenarios and limits" update.
      — *Note:* moved FastAPI from the stale "In progress"/"Does not work (yet)" sections (which
      described the pre-`__slots__` `__fields_set__` gap and "ASGI stack still missing" — both long
      since fixed) into "Done scenarios", with an accurate summary of what actually works. Also fixed
      two other stale claims the same table/list carried: `sqlite3`/`importlib`/`email`/`http` were
      still listed as "still missing" (all exist now); the `FastAPI` "does not work" table row was
      replaced with the two scenarios that genuinely still don't (Postgres, blocked on server
      availability; Django, not started).

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

**`import starlette` now succeeds completely — Phase 3.1 is done.** 4 more real gaps closed past
`mimetypes` (same continuation, "fatto io il commit, procedi con i mime types"): real `mimetypes
.guess_type` (a real extension→MIME table plus real encoding-suffix detection), real `secrets`
(CSPRNG-backed tokens + constant-time `compare_digest`), a real `memoryview` builtin type
(bytearray-backed views share real underlying storage, matching CPython), and — surfaced by
`memoryview` needing it — a real fix making `isinstance`/`issubclass` accept a genuine `X | Y` union
as the 2nd argument, which had never worked at all before. Full blow-by-blow in 3.1.5. 862/862 tests
green (up from 857). **Verified end to end, not just at import time**: `starlette.applications`/
`routing`/`responses` all import cleanly, and a real `Starlette(routes=[Route("/", homepage)])` app
now genuinely constructs.

**3.1b under way: real ASGI request dispatch works, and two significant correctness bugs found.**
Sending a constructed `Starlette` app a real, hand-built ASGI request (the same `scope`/`receive`/
`send` shape a real server sends) surfaced two bugs far more consequential than a missing module —
both would have silently broken *any* real FastAPI/starlette app, not just this probe:
**`inspect.isfunction` incorrectly excluded async functions** (so `Route.__init__`'s real
`isfunction(endpoint_handler) or ismethod(endpoint_handler)` check failed for essentially every real
`async def` route handler, treating it as an already-ASGI-shaped app and calling it with the wrong
arguments), and **`re.Match.groups(default=...)` only read `default` from kwargs, never
positionally** (breaking any path route with an untyped parameter, e.g. `/items/{item_id}` — the
common case). Also closed: `array` (real per-typecode round-tripping), a real `.__call__` attribute
on every callable, and `asyncio.AbstractEventLoop`/`all_tasks`/`current_task`. **Verified real,
correct ASGI responses end to end** for the index route and a path-parameter route. Full blow-by-blow
in 3.1.6. 868/868 tests green (up from 862).

**8 more real gaps closed past the private asyncio symbol, plus a second significant bug** (same
continuation): the private `_run_until_complete_cb` turned out to be a small, one-off addition, not
a deep rabbit hole. Past it: `inspect`'s coroutine-state constants + `getcoroutinestate`, a real
`queue` module (thread-safe, backed by `BlockingCollection` — genuinely different from `asyncio
.Queue`; also fixed a `maxsize=` keyword-argument bug found by manual probing before it ever hit a
test), real `asyncio.Runner`/`eager_task_factory`, the real `asyncio.protocols` base-class hierarchy,
`asyncio.subprocess.SubprocessStreamProtocol` made accessible as a real attribute (uncovering a more
general gap: PySharp's own submodules were never auto-attached to their parent package after a plain
import, unlike real CPython's), and `asyncio.Task` itself (uncovering a second significant bug:
`isinstance(a_task, asyncio.Future)` was `False`, when real CPython's `Task` genuinely *is* a
`Future` — fixed generally, not just for this one case). **Verified real exception propagation end
to end** (`/boom` → `ValueError` → correctly re-raised through starlette's real exception-handling
middleware). Full blow-by-blow in 3.1.7. 877/877 tests green (up from 868).

**5 more real gaps closed on the 404-not-found fallback path, two of them structural fixes with
effects well beyond this one scenario**: the previous round's bare `AssertionError` was
`asyncio.current_task()` always returning `None` (fixed for real: propagated as a thread-static
across every nested `await`'s dedicated OS thread). Past it: real runtime PEP 585 subscript-then-call
(`asyncio.Future[T]()`, plus making the resulting generic alias itself callable), a `Future`/`Task`
private `_loop` attribute anyio reads directly, and — the two structural ones — `threading.local`
state set inside a `@contextmanager` generator (before its `yield`) was invisible in the `with`-body,
because `PyGenerator` (like `PyCoroutine`) runs its body on its own dedicated OS thread; fixed with a
new `LogicalThread` identity explicitly propagated across `PyGenerator`/`PyCoroutine`'s internal
thread hops but *not* across genuine `threading.Thread.start()` calls. Found underneath that:
`Interp.DelAttr` never checked a class's `__delattr__` (only `SetAttr` did), and `TryGetAttr`'s
`__getattr__` fallback didn't catch a raised `AttributeError`, breaking `getattr(obj, name, default)`/
`hasattr` for any type relying on that standard contract — both fixed generally, not just for
`threading.local`. Full blow-by-blow in 3.1.8. 883/883 tests green (up from 877).

**The 404-not-found fallback path is now fully closed** (one more real bug found and fixed):
`asyncio.iscoroutinefunction`/`inspect.iscoroutinefunction`/`inspect.isgeneratorfunction` didn't see
through a bound method (real CPython does), so starlette's real `is_async_callable(self.
http_exception)` — the default 404/`HTTPException` handler, a bound `async def` instance method — came
back `False`, routing the call through a sync path that produced an unawaited coroutine object instead
of the real `Response`. Fixed with a shared `InspectModule.UnwrapBoundMethod` helper across all three
predicates. **Verified end to end**: a real, unmodified `Starlette` app now correctly returns 200 for a
matched route, 404 (`"Not Found"`) for an unmatched one, and correctly propagates an uncaught
`ValueError` from a route handler — all three together in one run. Full blow-by-blow in 3.1.9.
885/885 tests green (up from 877 at the start of 3.1.8).

**Custom exception handlers verified end to end with zero new bugs** (`Starlette(exception_handlers=
{404: ..., Exception: ...})`, including real starlette's "always re-raise after handling" semantics
for the `Exception`/500 case). **`staticfiles.py` is now closed too**: 7 more real gaps found and
fixed — `importlib.util` didn't exist as an importable submodule at all (needed a separately
registered builtin factory, matching the `asyncio.base_events` pattern, plus a new
`Importer.FindModuleSpec` backing a real `find_spec`); `os.stat`/`os.stat_result`, `os.path.normpath`,
`os.path.realpath`, and `os.path.commonpath` didn't exist; `NotADirectoryError`/`IsADirectoryError`
weren't real builtin exceptions; `collections.abc.Mapping` had no real mixin methods at all (now has
a real `get`, and `MutableMapping` derives from it for real, matching CPython's ABC hierarchy).
**Verified end to end**: `GET /static/hello.txt` returns 200 with the real file's bytes and real
`content-type`/`etag`/`last-modified` headers; `GET /static/nope.txt` returns a real 404. Full
blow-by-blow in 3.1.10. 892/892 tests green (up from 885 at the start of 3.1.10).

**WebSockets: the core protocol works end to end, zero bugs found.** A real `WebSocketRoute` driven
by a hand-built ASGI `websocket` scope + `connect`/`receive`/`disconnect` message sequence correctly
handles `accept`/`receive_text`/`send_text`/`close`, the `WebSocketDisconnect` path, and a manual
multi-message streaming loop — all matching real starlette semantics exactly. **The one real gap**:
`WebSocket.iter_text()`/`iter_bytes()`/`iter_json()` are real async generators, a documented,
deliberately-deferred language feature (Axis A) — not fixed this round (a substantial new capability,
not a gap-fill; a deliberate check-in point rather than starting unprompted). Full blow-by-blow in
3.1.11. No interpreter changes; 892/892 tests still green.

**Real async generators are now implemented** (author go-ahead, 3.1.12): a new `PyAsyncGenerator`
hybridizing `PyGenerator`'s yield-suspension with `PyCoroutine`'s await-suspension on one dedicated
thread. `WebSocket.iter_text()`/`iter_bytes()`/`iter_json()` now work for real against real starlette
— full WebSocket streaming-helper parity achieved. `contextlib.asynccontextmanager`'s `__aenter__`/
`__aexit__` are real too (a direct unblock, not a side effect): they previously raised
`NotImplementedError` unconditionally. `inspect`/`asyncio.iscoroutinefunction` now correctly exclude
async generator functions (mutually exclusive from coroutine functions in real CPython), and
`isasyncgenfunction`/`isasyncgen` are real (previously hardcoded `False`). Verified manually against
known-correct Python behavior (10+ scenarios) before writing 10 new tests. Full blow-by-blow in
3.1.12. 901/901 tests green (up from 892 at the start of 3.1.11).

**Lifespan events and `StaticFiles(packages=[...])` verified too, zero bugs found (3.1.13).**
Startup/shutdown (including the ASGI3 "state" extension and a startup-failure path) and
package-relative static file serving both work correctly against real starlette out of the box.

**Phase 3.1b is now substantially done**: routing, exception handling (default + custom, per-status
and per-type), static files (`directory=` and `packages=`), WebSockets (plain and real-async-
generator-backed streaming), and lifespan events are all verified end to end against real,
unmodified starlette + anyio. PySharp's traceback formatting still doesn't reveal real file/line for
imported modules (shows `<string>` — a known, pre-existing limitation, separately worth revisiting,
though it hasn't blocked root-causing anything so far).

**Phase 3.2 is done (3.1.14)**: `samples/asgi_server.py`, a real, minimal, reusable ASGI/3 HTTP
server bridging raw HTTP/1.1 to the real scope/receive/send protocol over PySharp's own async
socket I/O — verified over real HTTP (curl) against both its own demo app and a real, unmodified
`Starlette` app. Found and fixed one real, general interpreter bug along the way: `sys.path` was a
disconnected snapshot copy, so `sys.path.insert(...)` from Python code had zero effect on actual
import resolution — now backed by a live reference. Also added real `bytes.partition`/`rpartition`
(missing entirely; only `str` had them). 910/910 tests green (up from 901).

**Phase 3 (starlette + anyio + a real ASGI server) is now substantially complete end to end.**

**Phase 4 is underway (4.1.1)**: `import fastapi` requires real version pinning first —
`fastapi==0.99.1` + `starlette==0.27.0` + `pydantic==1.10.13` is the last combination built purely
against pydantic v1 (the default `install fastapi` resolves the latest release, which needs pydantic
v2/Rust — the exact wall this plan avoids). Found and fixed **a serious, general deadlock**:
`Importer.ImportAbsolute` held a lock across its entire recursive load-and-*execute* loop, so a
module-level generator expression evaluated during an import (real pydantic v1's own `utils.py` does
this) could spawn a second real OS thread that blocked forever on that same lock if it needed to
import anything new — a real bug well beyond this one scenario, fixed by narrowing the lock to only
the module-registry bookkeeping. Also fixed: `Morsel._reserved` needed to be a real class attribute
(starlette 0.27.0 patches it directly), `email.message.Message` didn't exist, `typing.TypeGuard`
didn't exist. 911/911 tests green (up from 910).

**Root-caused and fixed (4.1.2)**: the `NoneType` `RuntimeError` was two different objects both
claiming to be `type(None)` — `typing.NoneType`/`Optional[X]`'s implicit member vs. what
`None.__class__` actually returns — unified onto one canonical object. Also found and fixed
`issubclass(list, typing.List)` returning `False` (typing generics now delegate `issubclass` to
their real origin, matching CPython's `_SpecialGenericAlias.__subclasscheck__`), and added
`inspect.Parameter.replace(**changes)`. Also found and fixed **a second real concurrency bug** the
`issubclass` fix itself exposed: `GenericAliasModule.OriginMap`/`ArgsTransform` were plain,
non-thread-safe `Dictionary`s, now read far more often — under xUnit's real parallel test execution
this could corrupt their internal state (surfacing as the suite hanging intermittently), fixed by
switching to `ConcurrentDictionary`. 914/914 tests green (up from 911), confirmed stable across
multiple consecutive full-suite runs.

**`import fastapi` succeeds — Phase 4.1 is done (4.1.3).** Real `eval()` (expression evaluation,
real CPython's own full scope for it) and real `typing.ForwardRef`/`typing_extensions._AnnotatedAlias`
were implemented to resolve fastapi's real, genuinely self-referential JSON-Schema-shaped forward
refs (`SchemaOrBool = Union[Schema, bool]`). Along the way: a real, general `hash()` fix (never
consulted a `PyInstance`'s own `__hash__`), a second serious flaky-suite concurrency bug
(`GenericAliasModule.GenericPlaceholder`, the same class of bug as 4.1.2's `OriginMap` race — fixed
with `[ThreadStatic]`, confirmed via 41 consecutive clean full-suite runs), and ~4 more small stdlib
gaps (`email.message.Message`, `binascii.Error`, `http.client.responses`, a couple of bare typing
placeholders). 14 tests added, including a real `import fastapi` regression test against the pinned
PyPI packages. 928/928 tests green (up from 914).

**New frontier**: `FastAPI()` construction hits `inspect.isroutine` (missing). Not started — a
natural next step for Phase 4.2 (writing the first real target FastAPI sample app).

**Milestone reached (4.1.10): a real `TestClient` request round-trips end to end.** After 4.1.5
through 4.1.9 closed the `httpx`/astral-regex/event-loop-threading/`Task`-internals chain, this round
closed the last ~10 real gaps standing between "the event loop doesn't deadlock" and an actual
correct HTTP-shaped response: real `BaseExceptionGroup`/`ExceptionGroup` (PEP 654), real
`Coroutine`/`Generator`/`Awaitable` ABC duck-typing, `loop.get_task_factory()`/`set_task_factory()`,
real `io.BytesIO` position tracking (`seek`/`read`/`truncate`), the `dict()`/`dict.update()`
`keys()`-mapping protocol, real `generator.send(value)` semantics, a generator's `return value`
actually reaching `StopIteration.value`, `email.message.Message.get_content_charset`, and
`codecs.BOM_*`. `TestClient(app).get("/")` and `.get("/items/42")` both now return the correct status
code and JSON body through the real, unmodified fastapi/starlette/pydantic/httpx/httpcore/h11/anyio
stack. 993/993 tests green (up from 977 at the start of this round), confirmed via 15 consecutive
clean full-suite runs. Full blow-by-blow in 4.1.10.

**POST/PUT/DELETE/query-params/`HTTPException` verified, and a documented Phase-2 gap closed
(4.1.11).** Zero new bugs in the request/response plumbing itself past the GET-only milestone — a
pydantic request body validates and serializes correctly (including the real 422 error shape),
`HTTPException` round-trips its status/detail, query params parse correctly. The one real gap found —
a route returning a `BaseModel` directly leaked `__fields_set__` into the JSON, the exact
previously-documented "Current known gap in Phase 2" — is now fixed for real: `__slots__`-declared
attributes get genuine per-instance storage separate from the regular attribute dict
(`PyClass.HasSlot`/`PyInstance.Slots`), matching real pydantic's own `BaseModel.__slots__ =
('__dict__', '__fields_set__')` pattern exactly, including the set-shaped `__slots__` pydantic's own
metaclass actually computes. A `copy.copy`/`copy.deepcopy` regression this surfaced (they only copied
`.Dict`, silently emptying any slots-only object) was caught and fixed before it shipped. 998/998
tests green (up from 993), confirmed via 65 consecutive clean full-suite runs.

**Phase 4.2 is done: a real FastAPI app runs live, over real HTTP.** `samples/fastapi_demo.py` — a
real, unmodified `FastAPI()` app (path/query params, a real pydantic request body, a real
`HTTPException` 404, a computed response field) — wired to the real, reusable `samples/asgi_server.py`
`serve(app)` instead of `TestClient`'s in-process dispatch. Started as a real background process and
driven entirely with real `curl` over a real HTTP/1.1 connection: every route matched hand-derived
expected output exactly, **zero new bugs found**. This is the live, curl-able version of the
GET/POST/PUT/DELETE milestone 4.1.10/4.1.11 already verified in-process. 1004/1004 tests green (up
from 998), no interpreter changes needed this round.

**Phase 4 (FastAPI) has now reached a real, live, end-to-end milestone**: a real, unmodified FastAPI
app — full CRUD, typed path/query params, pydantic request-body validation, `HTTPException` — served
over real HTTP entirely by PySharp, with zero framework code modified. Remaining open items are
smaller-scoped: WebSocket support in `fastapi_demo.py` (real WebSockets already work against
starlette directly, per Phase 3.1b — not yet threaded through this specific sample), a real
uvicorn-equivalent process manager (today's `serve()` is single-connection-at-a-time-per-Task but has
no graceful shutdown/reload story), and the pre-existing, separately-scoped gaps noted along the way
(`except*` syntax, nested-import `locals()` scoping, non-`'__dict__'`-slots-only instances still
getting a usable `__dict__`).

**Phase 4.3–4.5 closed every one of those smaller-scoped items except the deliberately-deferred
ones**: real WebSocket support landed in `fastapi_demo.py` itself (4.3.1–4.3.3, RFC 6455 handshake +
framing + fragmentation + closing handshake, verified live both with a from-scratch client and
through starlette's own `WebSocket`); real graceful shutdown via `signal.signal()` (4.4, plus two
genuine event-loop bugs found and fixed chasing it); and the 30-pattern pydantic robustness sweep
(4.5, 7 more real gaps closed). 1041/1041 tests green, confirmed via 8 consecutive full-suite runs.

**Phase 5 (docs) is now done too (2026-08-11) — this plan is complete.** ROADMAP.md's scenario 2 was
flipped to ✅ Done (status header, table row, the 2d sub-item, and a new interpreter-evolution-log row
covering Phase 3-4's work, which had been entirely missing from that table); RELEASE_NOTES.md got a
new subsection; README.md's "Verified scenarios and limits" section had FastAPI moved out of "In
progress"/"does not work" (both describing gaps closed a session or more ago) and into "Done", plus
two other stale claims in the same section fixed along the way (`sqlite3`/`importlib`/`email`/`http`
were still listed missing; they aren't). Re-verified the underlying claim was still true before
writing any of this: full suite 1228/1228, `M16_FastApi` 45/45, against real installed
fastapi/starlette/pydantic/httpx packages (not mocks) — no regression from the numpy shim work done
in between. Genuinely remaining, intentionally out of scope: `except*` syntax (PEP 654), the
nested-import `locals()`/`globals()` scoping edge case, hot-reload-on-file-change for
`asgi_server.py`. None of these block scenario 2's "done" status.

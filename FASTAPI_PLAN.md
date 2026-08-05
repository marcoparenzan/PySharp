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
  - **Current frontier**: `ModuleNotFoundError: No module named 'pathlib'` — a whole separate
    module matching **ROADMAP.md's own scenario 8** ("File system API": `os.path`/`pathlib`/
    `shutil`/`glob`), not part of scenario 2's scope. Stopped the probe loop here deliberately
    rather than drifting into a different scenario mid-session — pick this back up as part of
    scenario 8 (or extend this plan explicitly if the author wants `pathlib` pulled forward because
    scenario 2 needs it specifically).
  - Full suite green after every single step above (698/698 by the end of this continuation, up
    from 670 at session start); `git status` clean of scratch installs after each round.

## Phase 2 — pydantic v1 (placeholder — scope from real probing once Phase 1 unblocks it)

- [ ] 2.1 Get `import pydantic` to succeed.
- [ ] 2.2 Get a minimal `BaseModel` subclass to construct and validate simple fields (str/int/bool),
  raising `ValidationError` on bad input — the load-bearing subset FastAPI's request/response models
  actually need.
- [ ] 2.3 Expand field types/validators as real usage in Phase 4's target app demands. Do not attempt
  full pydantic v1 API parity — same non-goal discipline as NUMPY_PLAN.md's "not full API parity".

## Phase 3 — starlette + anyio (placeholder)

- [ ] 3.1 Get `import starlette` to succeed.
- [ ] 3.2 Minimal ASGI app + routing working, driven by PySharp's `asyncio` (scenario 1b's reactor —
  `add_reader`/`add_writer`/`run_in_executor` — is exactly the machinery an ASGI server needs; this is
  where that investment pays off for scenario 2). Whether `anyio` gets its own real support or a thin
  asyncio-backed shim (it supports multiple backends upstream; only the asyncio backend matters here)
  is a decision to make once its actual usage surface from starlette is visible.

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

Phase 0 done. Phase 1 in progress: `import pydantic` still doesn't fully succeed, but ~35 real gaps
were found and fixed in one long probe-driven session (2026-08-05/06) — see 1.9's blow-by-blow.
Several of note beyond pydantic itself, all with their own tests: the `from pkg import name`
error-masking bug (1.1), the set/frozenset mixed-operator bug, `super()` falling back to `object`'s
`__init__`/`__new__`/`__setattr__`/`__delattr__`, and two full new pieces built on explicit author
decisions rather than guessed at inline: **real generic-alias tracking** (`List[int]`/
`Optional[int]`/etc. now build a real `__origin__`/`__args__` object instead of subscripting being a
no-op, so `typing.get_origin`/`get_args` work for real — its own regression, `Generic[T]` as a base
class, was caught and fixed via the full suite, not just the probe) and a **real `decimal.Decimal`**
(backed by `System.Decimal`, full arithmetic/comparison dunders via the interpreter's existing
generic instance-dunder dispatch — zero interpreter changes needed for that part). Progress went all
the way past `typing_extensions.py` entirely and deep into pydantic's own modules
(`class_validators.py`, `errors.py`, and beyond). Current frontier is `pathlib` — out of scope for
this scenario, since it's ROADMAP.md's own scenario 8 ("File system API"); stopped deliberately
rather than drift into a different scenario mid-probe. Phases 2–4 remain placeholders (see
architecture decisions) until Phase 1 fully closes.

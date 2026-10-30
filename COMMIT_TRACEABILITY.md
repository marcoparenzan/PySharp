# Commit Traceability — PySharp

A log that associates every commit of the repository with: the package version (`<Version>` in
`src/PySharp/PySharp.csproj` at the time of the commit), the files touched, and the plan
document/phase that commit represents (`FASTAPI_PLAN.md`, `AIOMQTT_PLAN.md`, `ROADMAP.md`,
`RELEASE_NOTES.md`).

**Methodology and honesty about the limits**: for the commits of the last rounds (from the fix of the deadlock
in the import system onwards, `0ac010b`→`ea5657e`), the correlation with `FASTAPI_PLAN.md` is direct —
I wrote those sections myself in the same session in which the commits were made, checking
the exact content of each commit (`git show --name-status`) against the files cited in the plan. For the
earlier commits (pydantic, match/case, subprocess, starlette, aiomqtt, the initial async/interop
core), the correlation is **reconstructed** by crossing the commit message, the files
actually touched and the references already present in `RELEASE_NOTES.md`/`FASTAPI_PLAN.md` — not
from direct knowledge of those sessions. Where the evidence is strong (the files touched correspond
exactly to what is described in a section of the plan) I mark it as such; where it is only plausible
I say so explicitly.

Commits with more than 25 modified files are summarised by directory (not listed file by file) to
keep the document readable — this is mainly the case of the initial commit (193 files) and of a few
other massive patches. The commit subjects are quoted verbatim, so a few remain in Italian.

---

## Index by era

1. [Foundations and first release (v1.0.0)](#1-foundations-and-first-release-v100) — `40357da` → `d227d47`
2. [Async/await, .NET interop, debug, REPL (v1.1.0 → v1.4.1)](#2-asyncawait-interop-net-debug-repl-v110--v141) — `9edbf3a` → `2ff0bae`
3. [NumPy plan (document only)](#3-numpy-plan-document-only) — `f796b88`
4. [aiomqtt scenario (v1.4.1)](#4-aiomqtt-scenario-v141) — `2d66d9d` → `3fbc4ec`
5. [pydantic v1 — Phase 1/2 of FASTAPI_PLAN.md (v1.4.1 → v1.5.0)](#5-pydantic-v1--phase-12-of-fastapi_planmd-v141--v150) — `7be5eed` → `c5e5646`
6. [starlette + anyio — Phase 3 of FASTAPI_PLAN.md (v1.5.0)](#6-starlette--anyio--phase-3-of-fastapi_planmd-v150) — `341965c` → `69079fd`
7. [FastAPI itself — Phase 4 of FASTAPI_PLAN.md (v1.5.0)](#7-fastapi-itself--phase-4-of-fastapi_planmd-v150) — `0ac010b` → `ea5657e`

---

## Summary table

| # | Date | Hash | Version | Files | Subject |
|---|------|------|----------|------|---------|
| 1 | 2026-07-24 | `40357da` | — | 2 | Initial commit |
| 2 | 2026-07-24 | `24523e4` | 1.1.0 | 193 | Initial Commit |
| 3 | 2026-07-24 | `d227d47` | 1.1.0 | 77 | Licence |
| 4 | 2026-07-25 | `9edbf3a` | 1.1.0 | 38 | async/await support: coroutines + asyncio (scenario 2a/2b) |
| 5 | 2026-07-25 | `9495568` | 1.1.0 | 1 | test: de-flake Gather_completes_in_delay_order |
| 6 | 2026-07-25 | `407114d` | 1.1.0 | 8 | feat: inject .NET objects into the Python scope (embedding interop) |
| 7 | 2026-07-25 | `9573d51` | 1.2.0 | 6 | docs: document async + .NET interop; bump packages to 1.2.0 |
| 8 | 2026-07-25 | `b021d7d` | 1.3.0 | 16 | feat: exceptions carry tracebacks + locals; add execution trace hook |
| 9 | 2026-07-25 | `47b73a5` | 1.4.0 | 8 | feat: multi-line input in the REPL |
| 10 | 2026-07-26 | `2ff0bae` | 1.4.1 | 6 | feat(repl): show PySharp version and Python compatibility in the banner |
| 11 | 2026-07-27 | `f796b88` | 1.4.1 | 1 | NUMPY_PLAN |
| 12 | 2026-08-01 | `2d66d9d` | 1.4.1 | 2 | aiomqtt support plan |
| 13 | 2026-08-02 | `f56aac9` | 1.4.1 | 27 | AioMqtt sample scenario - impleementation support |
| 14 | 2026-08-02 | `38cc1d6` | 1.4.1 | 1 | gitignore updated |
| 15 | 2026-08-02 | `0d275d7` | 1.4.1 | 1 | .gitignore updated |
| 16 | 2026-08-02 | `d0b008d` | 1.4.1 | 16 | Delete samples/site-packages directory |
| 17 | 2026-08-02 | `7fd7bd6` | 1.4.1 | 16 | Delete src/site-packages directory |
| 18 | 2026-08-02 | `3fbc4ec` | 1.4.1 | 4 | iothun aiomqtt test and fix done |
| 19 | 2026-08-06 | `7be5eed` | 1.4.1 | 20 | feat: pydantic v1 gap-filling — generic aliases, decimal.Decimal, ~35 stdlib/interpreter fixes |
| 20 | 2026-08-06 | `132cced` | 1.5.0 | 3 | version updated and dotnet tool |
| 21 | 2026-08-07 | `c467f2c` | 1.5.0 | 24 | pydantic implemented |
| 22 | 2026-08-07 | `75b5ddb` | 1.5.0 | 13 | Bug fixing |
| 23 | 2026-08-07 | `c5e5646` | 1.5.0 | 2 | Docs updated |
| 24 | 2026-08-08 | `341965c` | 1.5.0 | 17 | FastPlan Phase 2 + Phase 3 |
| 25 | 2026-08-08 | `e6d4c44` | 1.5.0 | 13 | match/case support |
| 26 | 2026-08-08 | `02afb64` | 1.5.0 | 12 | match/case parte 2 |
| 27 | 2026-08-08 | `5b026ee` | 1.5.0 | 27 | subprocess e altro |
| 28 | 2026-08-08 | `04f2ff7` | 1.5.0 | 7 | Starlette support |
| 29 | 2026-08-08 | `aefd30f` | 1.5.0 | 9 | scope/receive/send and bug fixing |
| 30 | 2026-08-08 | `7056951` | 1.5.0 | 8 | ASGI dispatch e altri fix (asyncio, queue, isfunction, groups) |
| 31 | 2026-08-08 | `389736b` | 1.5.0 | 12 | fix: real asyncio.current_task(), Future[T]() PEP 585, and threading.local across @contextmanager |
| 32 | 2026-08-08 | `7b4aa4f` | 1.5.0 | 5 | fix: real bound-method detection closes the starlette 404 path end-to-end |
| 33 | 2026-08-08 | `6612c4e` | 1.5.0 | 9 | feat: real staticfiles.py support — importlib.util, os.stat/path helpers, Mapping.get |
| 34 | 2026-08-08 | `f7b1ce2` | 1.5.0 | 11 | feat: real async generators — PyAsyncGenerator, iter_text/iter_bytes/iter_json, asynccontextmanager entering |
| 35 | 2026-08-08 | `51a6f7d` | 1.5.0 | 2 | docs: verify lifespan events and StaticFiles(packages=) — Phase 3.1b substantially done |
| 36 | 2026-08-08 | `69079fd` | 1.5.0 | 9 | feat: real minimal ASGI server (Phase 3.2) — bridges HTTP/1.1 to real ASGI, fixes sys.path mutation |
| 37 | 2026-08-08 | `0ac010b` | 1.5.0 | 8 | fix: real deadlock in import system + fastapi/pydantic v1 version pinning (Phase 4 start) |
| 38 | 2026-08-08 | `c22f5da` | 1.5.0 | 6 | fix: unify NoneType identity, issubclass delegation for typing generics, thread-safe GenericAliasModule |
| 39 | 2026-08-08 | `ea5657e` | 1.5.0 | 12 | feat: import fastapi succeeds — real eval()/ForwardRef, two concurrency bugs fixed (Phase 4.1 done) |

---

## 1. Foundations and first release (v1.0.0)

### `40357da` — 2026-07-24 — Initial commit — v. — (2 files)

Only `.gitignore` and `LICENSE`. The actual import of the code arrives in the next commit.

### `24523e4` — 2026-07-24 — Initial Commit — v1.1.0 — (193 files)

The whole initial body of code, in a single commit. It corresponds to `RELEASE_NOTES.md`'s
**v1.0.0** ("First functionally complete version" — M0-M9: lexer, parser, evaluator, functions/classes,
import system, .NET-backed stdlib, mini-pip, ctypes-lite, Azure IoT Hub sample). Files by directory:

- `src/PySharp.Tests/` (111) — the whole initial test suite (RustPython corpus included)
- `src/PySharpLib/` (47) — the interpreter itself
- `src/site-packages/` (16) — packages downloaded for the tests (scratch, later removed — see commits 16/17)
- `src/PySharp/` (3) — the CLI host
- `src/PipSharpLib/` (2) — the mini-pip
- `samples/` (6), project docs (`README.md`/`ARCHITECTURE.md`/`ROADMAP.md`/`RELEASE_NOTES.md`/`TODO.md`), `.vscode/tasks.json`, `src/PySharp.slnx`

### `d227d47` — 2026-07-24 — Licence — v1.1.0 — (77 files)

77 files modified with a single subject ("Licence") — almost certainly the addition of the MIT
licence header at the top of every `.cs` source file (the `// Copyright (c) 2026 Marco Parenzan` /
`// Licensed under the MIT License` pattern seen in every file touched in the later sessions).

---

## 2. Async/await, .NET interop, debug, REPL (v1.1.0 → v1.4.1)

### `9edbf3a` — 2026-07-25 — async/await support: coroutines + asyncio (scenario 2a/2b) — v1.1.0 — (38 files)

Corresponds to `RELEASE_NOTES.md`'s section **"Async/await and asyncio (scenario 2a/2b)"**:
`async def`/`await`/`async for`/`async with` in the language, a `PyCoroutine` on a dedicated thread with
a semaphore handshake, the `asyncio` module (.NET event loop, `Future`/`Task`, `run`/`sleep`/`gather`,
async socket I/O), and `samples/async_api.py` as end-to-end proof (22 new tests in `M10_Async`).
This is the foundation on which the whole Phase 3/4 of `FASTAPI_PLAN.md` (starlette/fastapi) rests
months later.

### `9495568` — 2026-07-25 — test: de-flake Gather_completes_in_delay_order — v1.1.0 — (1 file)

Fix of a single test made non-deterministic by the timing of `asyncio.gather`.

### `407114d` — 2026-07-25 — feat: inject .NET objects into the Python scope (embedding interop) — v1.1.0 — (8 files)

Corresponds to `RELEASE_NOTES.md`'s **".NET object injection (embedding interop)"**: `ClrObject`/
`ClrType`/`ClrMethod` + bidirectional marshalling, 18 tests in `M11_Interop`.

### `9573d51` — 2026-07-25 — docs: document async + .NET interop; bump packages to 1.2.0 — v1.2.0 — (6 files)

Documentation only + version bump to 1.2.0 (consistent with the observed version number).

### `b021d7d` — 2026-07-25 — feat: exceptions carry tracebacks + locals; add execution trace hook — v1.3.0 — (16 files)

Corresponds to **"Tracebacks, variable inspection and a trace hook"**: `PyRaise.Traceback`,
`PyFrameInfo`, `Interp.Trace` (Line/Call/Return/Exception), 9 tests in `M12_Debug` — the foundation
used for all the "debug-print-then-remove" diagnostics of the later sessions.

### `47b73a5` — 2026-07-25 — feat: multi-line input in the REPL — v1.4.0 — (8 files)

Corresponds to **"Multi-line REPL input"**: `InteractiveInput` (`IsIncomplete`/`StartsBlock`), 32 tests
in `M13_Repl`.

### `2ff0bae` — 2026-07-26 — feat(repl): show PySharp version and Python compatibility in the banner — v1.4.1 — (6 files)

REPL banner with version + declared Python compatibility.

---

## 3. NumPy plan (document only)

### `f796b88` — 2026-07-27 — NUMPY_PLAN — v1.4.1 — (1 file)

Only the creation of `NUMPY_PLAN.md` — no implementation in this commit (a scenario
not pursued in the commits traced here, or postponed).

---

## 4. aiomqtt scenario (v1.4.1)

### `2d66d9d` — 2026-08-01 — aiomqtt support plan — v1.4.1 — (2 files)

Creation of `AIOMQTT_PLAN.md`.

### `f56aac9` — 2026-08-02 — AioMqtt sample scenario - impleementation support — v1.4.1 — (27 files)

The bulk of the implementation of the aiomqtt plan (real async IoT Hub sample) — consistent with the
project memory: *"aiomqtt plan — AIOMQTT_PLAN.md complete: real-aiomqtt async IoT Hub sample
runs live (2026-08-02)"*.

### `38cc1d6` / `0d275d7` — 2026-08-02 — gitignore updated / .gitignore updated — v1.4.1 — (1 file each)

Minor adjustments to `.gitignore` (probably to exclude the scratch `site-packages/` used
by the probes, a pattern that recurs throughout the project's later history).

### `d0b008d` / `7fd7bd6` — 2026-08-02 — Delete samples/site-packages directory / Delete src/site-packages directory — v1.4.1 — (16 files each)

Removal of the two scratch `site-packages/` directories (16 files each — the same packages
seen in the initial commit) — the first observable case of the "clean up the scratch at the end of the
round" discipline that recurs in every later session.

### `3fbc4ec` — 2026-08-02 — iothun aiomqtt test and fix done — v1.4.1 — (4 files)

Closure of the aiomqtt scenario: final tests + fixes.

---

## 5. pydantic v1 — Phase 1/2 of FASTAPI_PLAN.md (v1.4.1 → v1.5.0)

### `7be5eed` — 2026-08-06 — feat: pydantic v1 gap-filling — generic aliases, decimal.Decimal, ~35 stdlib/interpreter fixes — v1.4.1 — (20 files)

Corresponds to **Phase 1** of `FASTAPI_PLAN.md` ("cross-cutting stdlib + the import-error-masking
bug"): new real modules `ColorSysModule`/`DateTimeModule`/`IpAddressModule`/`PathlibModule`/
`PickleModule`/`ReModule`/`WeakrefModule`, `ComplexType` (the `complex` type), and deep fixes in
`Interp.cs`/`GenericAliasModule.cs`/`Builtins.cs` — consistent with the project memory on the
pydantic v1 gaps filled in this period.

### `132cced` — 2026-08-06 — version updated and dotnet tool — v1.5.0 — (3 files)

Version bump to 1.5.0 (the last version observed in all the later commits) + update of
the `dotnet` tool.

### `c467f2c` — 2026-08-07 — pydantic implemented — v1.5.0 — (24 files)

`FASTAPI_PLAN.md` modified + `M16_FastApi/PydanticSmokeTests.cs` — the completion of Phase 1,
with `import pydantic` working end-to-end for the first time.

### `75b5ddb` — 2026-08-07 — Bug fixing — v1.5.0 — (13 files)

`M4_Functions/MetaclassTests.cs` (new) + fixes in `PyClass.cs`/`PyDict.cs`/`InspectModule.cs` —
consistent with **section 2.2.1** of `FASTAPI_PLAN.md` ("custom-metaclass support: the
blow-by-blow").

### `c5e5646` — 2026-08-07 — Docs updated — v1.5.0 — (2 files)

Only `README.md`/`ROADMAP.md`.

---

## 6. starlette + anyio — Phase 3 of FASTAPI_PLAN.md (v1.5.0)

All at v1.5.0 — no further version bump observed in this phase.

### `341965c` — 2026-08-08 — FastPlan Phase 2 + Phase 3 — (17 files)

New real modules `ContextVarsModule`/`ImportlibModule`/`ShlexModule`/`SignalModule`/
`TextwrapModule` — the start of **Phase 3** (starlette + anyio), section **3.1.1** of the plan.

### `e6d4c44` — 2026-08-08 — match/case support — (13 files)

Corresponds to **section 3.1.2**: `match`/`case` (PEP 634) — new parsing in `Ast.cs`/`Parser.cs`/
`AstDumper.cs`, new tests `M17_Match`/`M2_Parser/MatchParsingTests.cs`, `ConcurrentModule`/
`ConcurrentFuture` (new).

### `02afb64` — 2026-08-08 — match/case parte 2 (match/case part 2) — (12 files)

Corresponds to **section 3.1.3**: 6 real gaps beyond `match`/`case` (`concurrent.futures`, `stat`,
`chmod`, `ABC.register`, Generic MRO, `typing.override`) — new `StatModule.cs`, fixes in
`AbcModule.cs`/`GenericAliasModule.cs`.

### `5b026ee` — 2026-08-08 — subprocess e altro (subprocess and more) — (27 files)

Corresponds to **section 3.1.4**: 12 real gaps up to `applications`/`routing`/`responses`/
`requests` — new `EmailModule`/`HtmlModule`/`HttpModule`/`SubprocessModule`/`TempfileModule`/
`TracebackModule`, plus `BigStack.cs` (new — the 64MB stack per dedicated thread, later decisive for
the whole thread-per-coroutine/generator model).

### `04f2ff7` — 2026-08-08 — Starlette support — (7 files)

Corresponds to **section 3.1.5**: the last 4 gaps (`mimetypes`) — `import starlette` works
completely for the first time. New `MimetypesModule`/`SecretsModule`.

### `aefd30f` — 2026-08-08 — scope/receive/send and bug fixing — (9 files)

Corresponds to **section 3.1.6**: the first real ASGI dispatch (scope/receive/send built by
hand) — two significant correctness bugs found. New `ArrayModule.cs`.

### `7056951` — 2026-08-08 — ASGI dispatch e altri fix (ASGI dispatch and other fixes: asyncio, queue, isfunction, groups) — (8 files)

Corresponds to **section 3.1.7**: 8 real gaps beyond the private asyncio symbol — new
`QueueModule.cs` (real thread-safe queue), `isfunction`/`groups(default)` fixes.

### `389736b` — 2026-08-08 — fix: real asyncio.current_task(), Future[T]() PEP 585, and threading.local across @contextmanager — (12 files)

Corresponds to **section 3.1.8**: real `asyncio.current_task()`, `Future[T]()` subscript, and the
structural bug of `threading.local` across `@contextmanager` — the first of the commits of this
session that I wrote/verified directly.

### `7b4aa4f` — 2026-08-08 — fix: real bound-method detection closes the starlette 404 path end-to-end — (5 files)

Corresponds to **section 3.1.9**: `iscoroutinefunction` did not see through a bound method —
the 404 path closes completely, verified end-to-end against the real starlette.

### `6612c4e` — 2026-08-08 — feat: real staticfiles.py support — (9 files)

Corresponds to **section 3.1.10**: 7 real gaps (`importlib.util`/`find_spec`, `os.stat`/
`os.path.*`, `NotADirectoryError`, `collections.abc.Mapping.get`).

### `f7b1ce2` — 2026-08-08 — feat: real async generators — (11 files)

Corresponds to **section 3.1.12**: `PyAsyncGenerator` (new hybrid yield/await class), real
`iter_text`/`iter_bytes`/`iter_json`, `asynccontextmanager` finally usable.

### `51a6f7d` — 2026-08-08 — docs: verify lifespan events and StaticFiles(packages=) — (2 files)

Corresponds to **section 3.1.13**: verification only (zero bugs found) — no change to the
interpreter's code, only `FASTAPI_PLAN.md`/`ROADMAP.md`.

### `69079fd` — 2026-08-08 — feat: real minimal ASGI server (Phase 3.2) — (9 files)

Corresponds to **section 3.1.14**: `samples/asgi_server.py` (new, not in the library diff but
part of the commit), `sys.path` fix (new constructor `PyModule(name, PyDict)` in `Env.cs`),
`bytes.partition`/`rpartition`. **Closes Phase 3.**

---

## 7. FastAPI itself — Phase 4 of FASTAPI_PLAN.md (v1.5.0)

### `0ac010b` — 2026-08-08 — fix: real deadlock in import system + fastapi/pydantic v1 version pinning — (8 files)

Corresponds to **section 4.1.1**: the start of Phase 4. The serious deadlock in the import system
(`Importer.ImportAbsolute` held a lock while running arbitrary code), version pinning
(`fastapi==0.99.1`/`starlette==0.27.0`/`pydantic==1.10.13`), `Morsel._reserved`,
`email.message.Message`, `typing.TypeGuard`.

### `c22f5da` — 2026-08-08 — fix: unify NoneType identity, issubclass delegation for typing generics, thread-safe GenericAliasModule — (6 files)

Corresponds to **section 4.1.2**: two identity/inheritance bugs (`NoneType`, `issubclass` on
typing generics), a second concurrency bug (`OriginMap`/`ArgsTransform` not thread-safe, fixed with
`ConcurrentDictionary`), `inspect.Parameter.replace`.

### `ea5657e` — 2026-08-08 — feat: import fastapi succeeds — real eval()/ForwardRef, two concurrency bugs fixed (Phase 4.1 done) — (12 files)

Corresponds to **section 4.1.3**: real `eval()` (new), real `typing.ForwardRef` (new, in
`GenericAliasModule.cs`), real `typing_extensions._AnnotatedAlias`, a third concurrency bug
(`GenericPlaceholder`, fixed with `[ThreadStatic]`), general fix of `hash()`, new `BinasciiModule.cs`
+ `http.client`. **`import fastapi` works — milestone of Phase 4.1.**

---

## Note on version and commits

The version number (`<Version>` in `PySharp.csproj`) **is not incremented at every commit** —
it stays at `1.5.0` for all the 19 commits from `132cced` (2026-08-06) onwards, which cover the whole
Phase 2/3/4 of `FASTAPI_PLAN.md` (pydantic → starlette → fastapi). The version is therefore an indicator
of a **published release**, not of every single development step — for the "every single
change" granularity, the commit hash is the correct reference unit, not the version.

# Commit Traceability — PySharp

Un log che associa ogni commit del repository a: versione del pacchetto (`<Version>` in
`src/PySharp/PySharp.csproj` al momento del commit), file toccati, ed il documento/fase di piano
che quel commit rappresenta (`FASTAPI_PLAN.md`, `AIOMQTT_PLAN.md`, `ROADMAP.md`,
`RELEASE_NOTES.md`).

**Metodologia e onestà sui limiti**: per i commit degli ultimi round (dal fix del deadlock
nell'import system in poi, `0ac010b`→`ea5657e`), la correlazione con `FASTAPI_PLAN.md` è diretta —
ho scritto io stesso quelle sezioni nella stessa sessione in cui i commit sono stati fatti, verificando
il contenuto esatto di ogni commit (`git show --name-status`) contro i file citati nel piano. Per i
commit precedenti (pydantic, match/case, subprocess, starlette, aiomqtt, il nucleo async/interop
iniziale), la correlazione è **ricostruita** incrociando il messaggio di commit, i file
effettivamente toccati e i riferimenti già presenti in `RELEASE_NOTES.md`/`FASTAPI_PLAN.md` — non
da conoscenza diretta di quelle sessioni. Dove l'evidenza è forte (i file toccati corrispondono
esattamente a quanto descritto in una sezione del piano) lo marco come tale; dove è solo plausibile
lo dico esplicitamente.

I commit con più di 25 file modificati sono riassunti per directory (non elencati file per file) per
tenere il documento leggibile — è il caso principalmente del commit iniziale (193 file) e di poche
altre patch massicce.

---

## Indice per era

1. [Fondamenta e primo rilascio (v1.0.0)](#1-fondamenta-e-primo-rilascio-v100) — `40357da` → `d227d47`
2. [Async/await, interop .NET, debug, REPL (v1.1.0 → v1.4.1)](#2-asyncawait-interop-net-debug-repl-v110--v141) — `9edbf3a` → `2ff0bae`
3. [NumPy plan (solo documento)](#3-numpy-plan-solo-documento) — `f796b88`
4. [Scenario aiomqtt (v1.4.1)](#4-scenario-aiomqtt-v141) — `2d66d9d` → `3fbc4ec`
5. [pydantic v1 — Fase 1/2 di FASTAPI_PLAN.md (v1.4.1 → v1.5.0)](#5-pydantic-v1--fase-12-di-fastapi_planmd-v141--v150) — `7be5eed` → `c5e5646`
6. [starlette + anyio — Fase 3 di FASTAPI_PLAN.md (v1.5.0)](#6-starlette--anyio--fase-3-di-fastapi_planmd-v150) — `341965c` → `69079fd`
7. [FastAPI stesso — Fase 4 di FASTAPI_PLAN.md (v1.5.0)](#7-fastapi-stesso--fase-4-di-fastapi_planmd-v150) — `0ac010b` → `ea5657e`

---

## Tabella riassuntiva

| # | Data | Hash | Versione | File | Oggetto |
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

## 1. Fondamenta e primo rilascio (v1.0.0)

### `40357da` — 2026-07-24 — Initial commit — v. — (2 file)

Solo `.gitignore` e `LICENSE`. Il vero e proprio import del codice arriva nel commit successivo.

### `24523e4` — 2026-07-24 — Initial Commit — v1.1.0 — (193 file)

L'intero corpo di codice iniziale, in un solo commit. Corrisponde a `RELEASE_NOTES.md`'s
**v1.0.0** ("First functionally complete version" — M0-M9: lexer, parser, evaluator, funzioni/classi,
import system, stdlib .NET-backed, mini-pip, ctypes-lite, sample Azure IoT Hub). File per directory:

- `src/PySharp.Tests/` (111) — l'intera suite di test iniziale (corpus RustPython incluso)
- `src/PySharpLib/` (47) — l'interprete stesso
- `src/site-packages/` (16) — pacchetti scaricati per i test (scratch, poi rimossi — vedi commit 16/17)
- `src/PySharp/` (3) — l'host CLI
- `src/PipSharpLib/` (2) — il mini-pip
- `samples/` (6), doc di progetto (`README.md`/`ARCHITECTURE.md`/`ROADMAP.md`/`RELEASE_NOTES.md`/`TODO.md`), `.vscode/tasks.json`, `src/PySharp.slnx`

### `d227d47` — 2026-07-24 — Licence — v1.1.0 — (77 file)

77 file modificati con un unico oggetto ("Licence") — quasi certamente l'aggiunta dell'header di
licenza MIT in cima a ogni file sorgente `.cs` (il pattern `// Copyright (c) 2026 Marco Parenzan` /
`// Licensed under the MIT License` visto in ogni file toccato nelle sessioni successive).

---

## 2. Async/await, interop .NET, debug, REPL (v1.1.0 → v1.4.1)

### `9edbf3a` — 2026-07-25 — async/await support: coroutines + asyncio (scenario 2a/2b) — v1.1.0 — (38 file)

Corrisponde a `RELEASE_NOTES.md`'s sezione **"Async/await and asyncio (scenario 2a/2b)"**:
`async def`/`await`/`async for`/`async with` nel linguaggio, un `PyCoroutine` su thread dedicato con
handshake a semaforo, il modulo `asyncio` (.NET event loop, `Future`/`Task`, `run`/`sleep`/`gather`,
I/O socket asincrono), e `samples/async_api.py` come prova end-to-end (22 nuovi test in `M10_Async`).
Questo è il fondamento su cui l'intera Fase 3/4 di `FASTAPI_PLAN.md` (starlette/fastapi) si
appoggia mesi dopo.

### `9495568` — 2026-07-25 — test: de-flake Gather_completes_in_delay_order — v1.1.0 — (1 file)

Fix di un singolo test reso non-deterministico dal timing di `asyncio.gather`.

### `407114d` — 2026-07-25 — feat: inject .NET objects into the Python scope (embedding interop) — v1.1.0 — (8 file)

Corrisponde a `RELEASE_NOTES.md`'s **".NET object injection (embedding interop)"**: `ClrObject`/
`ClrType`/`ClrMethod` + marshalling bidirezionale, 18 test in `M11_Interop`.

### `9573d51` — 2026-07-25 — docs: document async + .NET interop; bump packages to 1.2.0 — v1.2.0 — (6 file)

Solo documentazione + bump versione a 1.2.0 (coerente con il numero di versione osservato).

### `b021d7d` — 2026-07-25 — feat: exceptions carry tracebacks + locals; add execution trace hook — v1.3.0 — (16 file)

Corrisponde a **"Tracebacks, variable inspection and a trace hook"**: `PyRaise.Traceback`,
`PyFrameInfo`, `Interp.Trace` (Line/Call/Return/Exception), 9 test in `M12_Debug` — le fondamenta
usate per tutta la diagnostica "debug-print-then-remove" delle sessioni successive.

### `47b73a5` — 2026-07-25 — feat: multi-line input in the REPL — v1.4.0 — (8 file)

Corrisponde a **"Multi-line REPL input"**: `InteractiveInput` (`IsIncomplete`/`StartsBlock`), 32 test
in `M13_Repl`.

### `2ff0bae` — 2026-07-26 — feat(repl): show PySharp version and Python compatibility in the banner — v1.4.1 — (6 file)

Banner REPL con versione + compatibilità Python dichiarata.

---

## 3. NumPy plan (solo documento)

### `f796b88` — 2026-07-27 — NUMPY_PLAN — v1.4.1 — (1 file)

Solo la creazione di `NUMPY_PLAN.md` — nessuna implementazione in questo commit (uno scenario
successivamente non perseguito nei commit qui tracciati, o rimandato).

---

## 4. Scenario aiomqtt (v1.4.1)

### `2d66d9d` — 2026-08-01 — aiomqtt support plan — v1.4.1 — (2 file)

Creazione di `AIOMQTT_PLAN.md`.

### `f56aac9` — 2026-08-02 — AioMqtt sample scenario - impleementation support — v1.4.1 — (27 file)

Il grosso dell'implementazione del piano aiomqtt (real async IoT Hub sample) — coerente con la
memoria di progetto: *"aiomqtt plan — AIOMQTT_PLAN.md complete: real-aiomqtt async IoT Hub sample
runs live (2026-08-02)"*.

### `38cc1d6` / `0d275d7` — 2026-08-02 — gitignore updated / .gitignore updated — v1.4.1 — (1 file ciascuno)

Aggiustamenti minori a `.gitignore` (verosimilmente per escludere `site-packages/` scratch usato
dai probe, un pattern che ricorre in tutta la storia successiva del progetto).

### `d0b008d` / `7fd7bd6` — 2026-08-02 — Delete samples/site-packages directory / Delete src/site-packages directory — v1.4.1 — (16 file ciascuno)

Rimozione delle due directory `site-packages/` scratch (16 file ciascuna — gli stessi pacchetti
visti nel commit iniziale) — il primo caso osservabile della disciplina "pulisci lo scratch a fine
round" che ricorre in ogni sessione successiva.

### `3fbc4ec` — 2026-08-02 — iothun aiomqtt test and fix done — v1.4.1 — (4 file)

Chiusura dello scenario aiomqtt: test + fix finali.

---

## 5. pydantic v1 — Fase 1/2 di FASTAPI_PLAN.md (v1.4.1 → v1.5.0)

### `7be5eed` — 2026-08-06 — feat: pydantic v1 gap-filling — generic aliases, decimal.Decimal, ~35 stdlib/interpreter fixes — v1.4.1 — (20 file)

Corrisponde alla **Fase 1** di `FASTAPI_PLAN.md` ("cross-cutting stdlib + the import-error-masking
bug"): nuovi moduli reali `ColorSysModule`/`DateTimeModule`/`IpAddressModule`/`PathlibModule`/
`PickleModule`/`ReModule`/`WeakrefModule`, `ComplexType` (il tipo `complex`), e fix profondi in
`Interp.cs`/`GenericAliasModule.cs`/`Builtins.cs` — coerente con la memoria di progetto sui gap di
pydantic v1 colmati in questo periodo.

### `132cced` — 2026-08-06 — version updated and dotnet tool — v1.5.0 — (3 file)

Bump di versione a 1.5.0 (l'ultima versione osservata in tutti i commit successivi) + aggiornamento
del tool `dotnet`.

### `c467f2c` — 2026-08-07 — pydantic implemented — v1.5.0 — (24 file)

`FASTAPI_PLAN.md` modificato + `M16_FastApi/PydanticSmokeTests.cs` — il completamento della Fase 1,
con `import pydantic` che funziona per la prima volta end-to-end.

### `75b5ddb` — 2026-08-07 — Bug fixing — v1.5.0 — (13 file)

`M4_Functions/MetaclassTests.cs` (nuovo) + fix in `PyClass.cs`/`PyDict.cs`/`InspectModule.cs` —
coerente con la **sezione 2.2.1** di `FASTAPI_PLAN.md` ("custom-metaclass support: the
blow-by-blow").

### `c5e5646` — 2026-08-07 — Docs updated — v1.5.0 — (2 file)

Solo `README.md`/`ROADMAP.md`.

---

## 6. starlette + anyio — Fase 3 di FASTAPI_PLAN.md (v1.5.0)

Tutti a v1.5.0 — nessun bump di versione ulteriore osservato in questa fase.

### `341965c` — 2026-08-08 — FastPlan Phase 2 + Phase 3 — (17 file)

Nuovi moduli reali `ContextVarsModule`/`ImportlibModule`/`ShlexModule`/`SignalModule`/
`TextwrapModule` — l'avvio della **Fase 3** (starlette + anyio), sezione **3.1.1** del piano.

### `e6d4c44` — 2026-08-08 — match/case support — (13 file)

Corrisponde alla **sezione 3.1.2**: `match`/`case` (PEP 634) — nuovo parsing in `Ast.cs`/`Parser.cs`/
`AstDumper.cs`, nuovi test `M17_Match`/`M2_Parser/MatchParsingTests.cs`, `ConcurrentModule`/
`ConcurrentFuture` (nuovi).

### `02afb64` — 2026-08-08 — match/case parte 2 — (12 file)

Corrisponde alla **sezione 3.1.3**: 6 gap reali oltre `match`/`case` (`concurrent.futures`, `stat`,
`chmod`, `ABC.register`, MRO Generic, `typing.override`) — nuovo `StatModule.cs`, fix in
`AbcModule.cs`/`GenericAliasModule.cs`.

### `5b026ee` — 2026-08-08 — subprocess e altro — (27 file)

Corrisponde alla **sezione 3.1.4**: 12 gap reali fino a `applications`/`routing`/`responses`/
`requests` — nuovi `EmailModule`/`HtmlModule`/`HttpModule`/`SubprocessModule`/`TempfileModule`/
`TracebackModule`, più `BigStack.cs` (nuovo — lo stack a 64MB per thread dedicato, poi decisivo per
tutto il modello a thread-per-coroutine/generatore).

### `04f2ff7` — 2026-08-08 — Starlette support — (7 file)

Corrisponde alla **sezione 3.1.5**: gli ultimi 4 gap (`mimetypes`) — `import starlette` funziona
per la prima volta completamente. Nuovi `MimetypesModule`/`SecretsModule`.

### `aefd30f` — 2026-08-08 — scope/receive/send and bug fixing — (9 file)

Corrisponde alla **sezione 3.1.6**: il primo vero dispatch ASGI (scope/receive/send costruiti a
mano) — due bug di correttezza significativi trovati. Nuovo `ArrayModule.cs`.

### `7056951` — 2026-08-08 — ASGI dispatch e altri fix (asyncio, queue, isfunction, groups) — (8 file)

Corrisponde alla **sezione 3.1.7**: 8 gap reali oltre il simbolo asyncio privato — nuovo
`QueueModule.cs` (coda thread-safe reale), fix `isfunction`/`groups(default)`.

### `389736b` — 2026-08-08 — fix: real asyncio.current_task(), Future[T]() PEP 585, and threading.local across @contextmanager — (12 file)

Corrisponde alla **sezione 3.1.8**: `asyncio.current_task()` reale, subscript `Future[T]()`, e il
bug strutturale `threading.local` attraverso `@contextmanager` — il primo dei commit di questa
sessione che ho scritto/verificato direttamente io.

### `7b4aa4f` — 2026-08-08 — fix: real bound-method detection closes the starlette 404 path end-to-end — (5 file)

Corrisponde alla **sezione 3.1.9**: `iscoroutinefunction` non vedeva attraverso un bound method —
il path 404 si chiude completamente, verificato end-to-end contro starlette reale.

### `6612c4e` — 2026-08-08 — feat: real staticfiles.py support — (9 file)

Corrisponde alla **sezione 3.1.10**: 7 gap reali (`importlib.util`/`find_spec`, `os.stat`/
`os.path.*`, `NotADirectoryError`, `collections.abc.Mapping.get`).

### `f7b1ce2` — 2026-08-08 — feat: real async generators — (11 file)

Corrisponde alla **sezione 3.1.12**: `PyAsyncGenerator` (nuova classe ibrida yield/await),
`iter_text`/`iter_bytes`/`iter_json` reali, `asynccontextmanager` finalmente utilizzabile.

### `51a6f7d` — 2026-08-08 — docs: verify lifespan events and StaticFiles(packages=) — (2 file)

Corrisponde alla **sezione 3.1.13**: solo verifica (zero bug trovati) — nessuna modifica al codice
dell'interprete, solo `FASTAPI_PLAN.md`/`ROADMAP.md`.

### `69079fd` — 2026-08-08 — feat: real minimal ASGI server (Phase 3.2) — (9 file)

Corrisponde alla **sezione 3.1.14**: `samples/asgi_server.py` (nuovo, non nel diff di libreria ma
parte del commit), fix `sys.path` (nuovo costruttore `PyModule(name, PyDict)` in `Env.cs`),
`bytes.partition`/`rpartition`. **Chiude la Fase 3.**

---

## 7. FastAPI stesso — Fase 4 di FASTAPI_PLAN.md (v1.5.0)

### `0ac010b` — 2026-08-08 — fix: real deadlock in import system + fastapi/pydantic v1 version pinning — (8 file)

Corrisponde alla **sezione 4.1.1**: l'avvio della Fase 4. Il deadlock serio nel sistema di import
(`Importer.ImportAbsolute` teneva un lock durante l'esecuzione di codice arbitrario), pinning delle
versioni (`fastapi==0.99.1`/`starlette==0.27.0`/`pydantic==1.10.13`), `Morsel._reserved`,
`email.message.Message`, `typing.TypeGuard`.

### `c22f5da` — 2026-08-08 — fix: unify NoneType identity, issubclass delegation for typing generics, thread-safe GenericAliasModule — (6 file)

Corrisponde alla **sezione 4.1.2**: due bug di identità/ereditarietà (`NoneType`, `issubclass` sui
generici typing), un secondo bug di concorrenza (`OriginMap`/`ArgsTransform` non thread-safe, fix
con `ConcurrentDictionary`), `inspect.Parameter.replace`.

### `ea5657e` — 2026-08-08 — feat: import fastapi succeeds — real eval()/ForwardRef, two concurrency bugs fixed (Phase 4.1 done) — (12 file)

Corrisponde alla **sezione 4.1.3**: `eval()` reale (nuovo), `typing.ForwardRef` reale (nuovo, in
`GenericAliasModule.cs`), `typing_extensions._AnnotatedAlias` reale, un terzo bug di concorrenza
(`GenericPlaceholder`, fix con `[ThreadStatic]`), fix generale di `hash()`, nuovi `BinasciiModule.cs`
+ `http.client`. **`import fastapi` funziona — traguardo della Fase 4.1.**

---

## Nota su versione e commit

Il numero di versione (`<Version>` in `PySharp.csproj`) **non viene incrementato ad ogni commit** —
resta fermo a `1.5.0` per tutti i 19 commit dal `132cced` (2026-08-06) in poi, che coprono l'intera
Fase 2/3/4 di `FASTAPI_PLAN.md` (pydantic → starlette → fastapi). La versione è quindi un indicatore
di **release pubblicata**, non di ogni singolo passo di sviluppo — per la granularità "ogni singolo
cambiamento", il commit hash è l'unità di riferimento corretta, non la versione.

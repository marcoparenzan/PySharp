# Release Notes — PySharp

Versions follow the development milestones. Dates are completion dates.

---

## v1.0.0 — 2026-07-24

First functionally complete version. A Python 3.x interpreter written from scratch in C# (.NET 10)
that runs paho-mqtt and talks to Azure IoT Hub, **tested end-to-end against a real hub**.

### Main achievement

- `import paho.mqtt.client` works: paho-mqtt **2.1.0**, downloaded from PyPI by the mini-pip, runs
  entirely inside the interpreter.
- **E2E test against a real Azure IoT Hub** — all operations verified from both sides:
  - **D2C** (device→cloud): telemetry sent;
  - **C2D** (cloud→device): message received and decoded;
  - **Device twin GET** and **reported properties** (accepted, status 204);
  - **Desired properties** push (patch received → applied → confirmation reported, verified on the
    hub side).
- MQTT connection over **TLS 8883** with **SAS** authentication (proven e2e) and **X.509**
  (implemented and unit-tested).

### Included milestones

| # | Content | Tests |
|---|---|---|
| M0 | Solution skeleton (4 projects + xUnit) | — |
| M1 | Lexer (numbers, strings, f-strings, bytes, INDENT/DEDENT) | 26 |
| M2 | Parser + AST + dumper (expressions, statements, decorators, comprehensions) | 30 |
| M3 | Core evaluator (BigInteger arithmetic, collections, control flow, LEGB) | 33 |
| M4 | Functions, classes (C3 MRO, super, dunders, property), exceptions, generators | 48 |
| M5 | Import system (packages, relative imports, `sys.modules`) | 12 |
| M6 | .NET-backed stdlib (socket, ssl, threading, struct, json, hmac, …) | 17 |
| M7 | Mini-pip (PyPI JSON API, wheels, sha256) + paho-mqtt import | 3 |
| M8 | ctypes-lite (native DLLs via `NativeLibrary` + `calli` thunk) | 7 |
| M9 | Azure IoT Hub sample (SAS + X.509, D2C/C2D/twin) | 5 |

### Available stdlib modules

`sys`, `os`, `time`, `platform`, `errno`, `io`, `warnings`, `copy`, `socket`, `ssl`, `select`,
`threading`, `struct`, `hashlib`, `hmac`, `base64`, `string`, `urllib`(+`parse`,`request`), `json`,
`collections`, `enum`, `functools`, `math`, `ctypes`, plus the `typing`, `dataclasses`, `__future__`
stubs.

### Operational notes

- The listen window for C2D/desired should be kept wide (~90s) to absorb the hub's delivery latency.
- Fix required for the e2e: paho on Python 3.12 calls `ssl.match_hostname` as a fallback when
  `context.check_hostname` is falsy → exposed `check_hostname`/`verify_mode` as real `SSLContext`
  attributes (CPython default) and added `ssl.match_hostname` as a no-op (host validation is already
  done by `SslStream` during the handshake).

---

## Quality and robustness (post-M9)

Introduced a **conformance corpus** based on RustPython's test snippets (85 files, MIT). Running it
inside PySharp fixed real bugs:

- anti-recursion guard in `repr` (avoids StackOverflow on cyclic structures);
- `BigInteger→int` saturation in slices and controlled shortcut/overflow in powers (avoids two CLR
  crashes with huge indices/exponents, e.g. `2**100`);
- `Env.TryGet` now honors `global`/`nonlocal` on reads too (scoping bug);
- `max()`/`min()` with no arguments raise `TypeError` (not `ValueError`).

Added in this phase the `math`, `io` (StringIO/BytesIO), `functools` modules and the `slice()`,
`locals()`, `globals()` builtins, plus numerous conformance fixes (`range` equality/slicing, `set`
comparisons, multi-iterable `map`/`zip`, `{expr=}` f-strings, etc.).

Test status: **519 green** (unit + 31 supported corpus + 52 documented xfail corpus).

---

## Scenario-driven evolution (post-v1.0.0)

Development continues **by scenarios** — real runnable scripts that drive interpreter evolution (see
[ROADMAP.md](ROADMAP.md)). Highlights since v1.0.0:

- **HTTP API (sync, FastAPI-shaped)**: a hand-rolled synchronous web micro-framework over the
  `socket` module, with path parameters, JSON bodies, and type-hint-driven validation/injection.
  Drove function introspection in the interpreter: populated `__annotations__` (including `'return'`),
  `__code__.co_varnames`/`co_argcount`, and `__name__` on builtin functions.
- **MQTT subscribe**: a real round-trip against a public broker, with no interpreter changes.
- **JSON + YAML (de)serialization**: added a C# `yaml` module (`safe_load`/`safe_dump`, a practical
  PyYAML subset).
- **CLI + global tool**: the console host now uses Spectre.Console.Cli and is packaged as a .NET
  global tool (`pysharp` on PATH).
- **`async`/`await` + `asyncio` (scenario 2a/2b)**: coroutines are now a language feature and there
  is a working `asyncio` event loop (see below).
- **.NET (CLR) interop for embedding**: host apps can inject .NET objects/types into the script
  scope and use them idiomatically from Python (see below).
- **Multi-line REPL**: the `pysharp repl` now accepts blocks, triple-quoted strings and bracketed
  expressions across lines (CPython-style continuation).

Test status at the latest checkpoint: **628 green**.

### Multi-line REPL input

The interactive REPL now reads multi-line input like CPython's: it keeps prompting with `...` while
the input is incomplete (open triple-quoted string, unbalanced `()`/`[]`/`{}`, or a trailing `\`),
and a compound block (`def`/`class`/`if`/`for`/`while`/`try`/`with`/`async`/decorator) is terminated
by a blank line. The completeness logic is the public, testable helper
[InteractiveInput](src/PySharpLib/InteractiveInput.cs) (`IsIncomplete`/`StartsBlock`; 32 tests in
[M13_Repl](src/PySharp.Tests/M13_Repl/)). The REPL also prints a full traceback on errors now.

### .NET object injection (embedding interop)

Hosting the interpreter, you can now expose host objects to the script with
`engine.SetVariable(name, obj)` and use them idiomatically from Python:

- instance & static **method calls** with overload resolution (by arity and marshalled arg types);
- **property** and **field** read/write; **indexers** (`obj[key]`); **construction** of an injected
  `Type` (`Point(3, 4)`); **iteration** over any `IEnumerable`; calling an injected delegate
  (`Func<>`/`Action<>`) as a function;
- automatic **marshalling** both ways (Python `int`↔`BigInteger`/`int`/`long`/…, `float`↔`double`,
  `str`↔`string`, `None`↔`null`, `list`↔arrays/`List<T>`; other objects wrapped transparently).

Implemented in [Clr.cs](src/PySharpLib/Runtime/Clr.cs) (`ClrObject`/`ClrType`/`ClrMethod` +
`ClrMarshal`/`ClrBinder`), wired into the interpreter's attribute/call/index/iterate dispatch.
See the README "Injecting .NET objects" section; covered by 18 tests in
[M11_Interop](src/PySharp.Tests/M11_Interop/). Out of scope for now: `ref`/`out` params,
generic-method inference, events, and passing Python callables as .NET delegates.

### Tracebacks, variable inspection and a trace hook (debugging groundwork)

Exceptions now carry **where** and **what state**. `PyRaise.Traceback` is the call stack captured as
the exception unwinds — each `PyFrameInfo` gives function/file/line and `Locals()` (the variables in
scope at that level). `PyErr.FormatTraceback` renders the CPython-shaped string, and the `pysharp`
console host now prints the **full stack** instead of a single line. For live observation,
`Interp.Trace` fires **Line/Call/Return/Exception** events (synchronous, zero cost when unset) — the
foundation for a **VS Code debugger** (breakpoints/stepping, Variables and Call Stack panes). See the
README sections 6–7, [Frames.cs](src/PySharpLib/Runtime/Frames.cs), and the 9 tests in
[M12_Debug](src/PySharp.Tests/M12_Debug/). Cross-thread frames (generators/coroutines) are not yet
stitched into one stack; the DAP adapter itself is still to be built (see TODO.md).

### Async/await and asyncio (scenario 2a/2b) — key FastAPI groundwork

The heaviest prerequisite of the FastAPI scenario — native asynchrony — is in place:

- **Language core.** `async def`, `await`, `async for` and `async with` now parse and execute. A
  coroutine runs its body on a dedicated thread and suspends at every `await` on a pending Future
  through a semaphore handshake — the same technique as generators — so only one coroutine runs at a
  time: cooperative single-threading, exactly like CPython (no data races on Python objects).
  `await` of a coroutine delegates like `yield from`, exceptions propagate across `await`, and
  `async with`/`async for` drive `__aenter__`/`__aexit__`/`__aiter__`/`__anext__`
  (`StopAsyncIteration` added).
- **`asyncio` module.** A .NET-backed event loop (ready queue + timer heap + cross-thread wake-ups)
  with `Future`/`Task`, `run`, `sleep`, `gather` (incl. `return_exceptions`), `create_task`,
  `ensure_future`, `wait_for`, `get_running_loop`/`get_event_loop`, and **asynchronous socket I/O**
  (`loop.sock_accept`/`sock_recv`/`sock_sendall`) offloaded to the thread pool and rejoined via
  `call_soon`.
- **Proof.** [samples/async_api.py](samples/async_api.py): a fully asynchronous "FastAPI-shaped" HTTP
  server where each connection is its own Task and a slow handler does **not** block the others.
  Verified end-to-end (`curl` on all routes + a real TCP request from an xUnit test) and covered by
  22 new tests in [M10_Async](src/PySharp.Tests/M10_Async/).
- **Still out of scope at the time:** async generators (`yield` inside `async def`), async
  synchronization primitives (`asyncio.Lock`/`Event`/`Queue`/`Semaphore`), and the remaining FastAPI
  stack (`re`/`datetime`/`inspect`, an ASGI server, pydantic — `pydantic-core` is compiled in Rust).
  **All of these are done now — see the next section.**

### FastAPI (scenario 2) — complete (2026-08-10)

The **key scenario** of the roadmap: a real, unmodified `FastAPI()` app — full CRUD, typed path/query
params, real pydantic request-body validation, `HTTPException`, WebSockets, graceful shutdown — served
live over real HTTP entirely by PySharp, zero framework code modified.

- **pydantic v1** (chosen over v2 to avoid the `pydantic-core` Rust wall): `import pydantic` succeeds;
  a `BaseModel` subclass constructs, validates real field types, raises real `ValidationError`, and
  serializes via `.dict()`/`.json()`. Required real (simplified) **custom-metaclass support** in
  `ExecClassDef` — the first scenario where "custom metaclasses are ignored" (a deliberate prior
  simplification) actually blocked something. A 30-pattern real-world robustness sweep then probed
  field types/validators/`Config` options well beyond any one sample app's needs.
- **starlette 1.4.1 + anyio** (both real, unmodified, from PyPI): real ASGI request dispatch, routing,
  exception handling (default + custom), static files, WebSockets (including real async-generator-
  backed streaming helpers), and lifespan events — all verified against real, unmodified packages.
- **`samples/asgi_server.py`**: a real, minimal, reusable ASGI/3 HTTP server over PySharp's own async
  socket I/O — real RFC 6455 WebSocket handshake/framing/fragmentation, and real `signal.signal()`
  (SIGINT/SIGTERM)-backed graceful shutdown.
- **`samples/fastapi_demo.py`**: the live target app, run as a background process and driven entirely
  with real `curl` over real HTTP/1.1 — every route matched hand-derived expected output exactly.
- **New language/runtime capability landed along the way** (general-purpose, not FastAPI-specific):
  real `match`/`case` structural pattern matching (PEP 634); real **async generators** (a new
  `PyAsyncGenerator`, hybridizing yield- and await-suspension); a real recursion-depth guard
  (`RecursionError` at CPython's default limit, on a real 64MB-stack thread); real `__slots__`-backed
  per-instance storage separate from an instance's regular attribute dict; three real, generically
  important concurrency bugs found and fixed (`Importer.ImportAbsolute` holding its lock across a
  recursive *execute*, and two non-thread-safe dictionaries corrupting under real parallel test
  execution).
- Dozens of real interpreter/stdlib gaps found and fixed round by round — full phased blow-by-blow in
  `FASTAPI_PLAN.md`.

---

## v1.1.0 — 2026-08-11

A real, from-scratch **`numpy`-shaped shim** — scenario 12 of the roadmap. Real numpy is a compiled
CPython C extension a from-scratch interpreter cannot load, so this is a reimplementation of numpy's
*observable behavior* (construction, dtypes, indexing, broadcasting, reductions, ufuncs, shape
manipulation, linear algebra, interop), verified throughout against real numpy's own documented
semantics — not a wrapper around the real library.

### Main achievement

- `import numpy as np` works, with `float64`/`int64`/`bool` dtypes and real arithmetic promotion
  (`float64` > `int64` > `bool`, true division always `float64`).
- Real construction (`array`/`zeros`/`ones`/`full`/`empty`/`arange`/`linspace`/`eye`/`identity`,
  `dtype=`, `astype`), indexing/slicing, broadcasting, reductions (`sum`/`mean`/`std`/`argmin`/
  `cumsum`/…), ufuncs (`sqrt`/`exp`/`log`/trig/`round`/`clip`/…), shape manipulation (`reshape`/
  `ravel`/`transpose`/`concatenate`/`stack`/`np.newaxis`/…), basic linear algebra (`dot`/`matmul`/
  `@`, `np.linalg.norm`, `trace`/`diagonal`), a seedable `np.random`, and a two-way `.NET` array
  interop bridge (`to_clr()`/`np.array(clr_array)`).
- **Real strided views**: basic indexing (`a[1:3]`), `.T`/`transpose()`, `reshape`/`ravel` (when the
  source is contiguous), `expand_dims`, and `squeeze` share the source array's buffer instead of
  copying — mutating a slice or a transpose mutates the original, matching real numpy's own actual
  behavior. Boolean masking (`a[mask]`) and `flatten()` still always copy, also matching real numpy.
- Two genuine core-interpreter fixes found along the way: `Interp.CompareExpr` now returns a single
  comparison's raw dunder result instead of always collapsing to `bool` (`arr1 < arr2` returns a real
  array); `PyOps.PyEquals` no longer treats `NaN == NaN` as `True` via a reference-identity fast path
  that was wrong specifically for `double` (IEEE 754 says `NaN != NaN` even for the same object).
- `samples/numpy_demo.py`: a realistic end-to-end session (construct, index, view, broadcast, reduce,
  mask, matmul, random), run and verified via the console host.

### Tests

120+ new tests in [src/PySharp.Tests/M14_Numpy](src/PySharp.Tests/M14_Numpy/), covering every phase
of NUMPY_PLAN.md's 12-phase plan against real, known numpy semantics.

---

## ORM support (SQLAlchemy) — scenario 13 — 2026-08-13

The real, unmodified **SQLAlchemy 2.0.51** now runs live against this project's own real `sqlite3`
module: `declarative_base()`, a mapped class, `Base.metadata.create_all(engine)` (real `CREATE TABLE`
DDL), `Session.add()`/`.commit()` (a full real INSERT flush, including the `insertmanyvalues`/
RETURNING-clause machinery), and `session.execute(select(...)).scalars().all()`/`session.get(...)` —
verified end to end with a full insert-then-query round trip against a real SQLite in-memory
database.

- **New, substantial language capabilities landed along the way** (general-purpose, not
  SQLAlchemy-specific): real `class Foo(dict/list/set/str/int): ...` subclassing (instances behave as
  the real builtin — arithmetic, `[]`, iteration, real methods, value-based hashing/equality with the
  plain builtin); real `__slots__` descriptor semantics (per-class data descriptors that shadow
  inherited attributes); PEP 487 `__init_subclass__`; the general descriptor protocol (`__get__`/
  `__set__` on arbitrary user classes, and on plain functions/builtins themselves — `func.__get__` is
  the real mechanism behind "a function becomes a bound method through a class"); real Python name
  mangling (`__name` → `_ClassName__name`); metaclass `__init__` dispatch and metaclass-level
  binary/comparison operator overloading; `instance.__dict__ = newdict` whole-namespace replacement.
- **~30 real, general interpreter/stdlib gaps** found and fixed round by round (full blow-by-blow in
  `ORM_PLAN.md`) — none SQLAlchemy-specific, each independently reachable by other real packages too.
  Notably: `co_varnames`'s ordering now matches real CPython's actual layout (kwonly names before
  `*args`, not after); a function's `__code__` is now a stable, identity-cached object; `abc.ABCMeta`
  now has a real `type` base, so custom metaclasses built on it (a common real pattern — pydantic's
  own `ModelMetaclass` uses the exact same shape) are correctly recognized.
- Verified live via [src/PySharp.Tests/M22_Orm](src/PySharp.Tests/M22_Orm/) against the real,
  unmodified package. A pure-Python Postgres dialect (`pg8000`) is the natural next step.

---

## Postgres (real DB-API + real SQLAlchemy round trip) — 2026-08-15

A real, unmodified **`psycopg2`-shaped shim over Npgsql** (SQL_PLAN.md Phase 2) — `connect`/
`Connection`/`Cursor`, real `%s`/`%(name)s` placeholder rewriting, psycopg2's own autocommit=False
transaction model (Postgres has fully transactional DDL, unlike sqlite3's own DDL-vs-DML heuristic),
and the PEP 249 exception hierarchy — verified live against a real Azure Database for PostgreSQL
instance ([samples/postgres_demo.py](samples/postgres_demo.py), 11 tests in
[Psycopg2Tests.cs](src/PySharp.Tests/M6_Stdlib/Psycopg2Tests.cs)).

Real, unmodified SQLAlchemy 2.0.51 now round-trips against the same server too (ORM_PLAN.md's
Postgres phase), driven through the shim above via `postgresql+psycopg2://`: connection setup, real
DDL (`create_all`/`drop_all`, including a real `has_table()` reflection round trip), a full
`session.add()`/`.commit()` INSERT flush through SQLAlchemy 2.0's own `insertmanyvalues` sentinel/
batching machinery, and `session.execute(select(...))`/`session.get(...)` all produce exactly the
expected real values ([OrmPostgresSmokeTests.cs](src/PySharp.Tests/M22_Orm/OrmPostgresSmokeTests.cs)).
Getting there surfaced several more general, non-Postgres-specific interpreter fixes: a genuine
concurrency bug (`threading.Condition` wrapped .NET's thread-affine `Monitor` directly, breaking
under this interpreter's own thread-per-generator execution model — rewritten to the same
semaphore-based algorithm real CPython's own `Condition` uses), a general zero-arg `super()` fix (the
implicit `__class__` cell is now recorded at function-definition time, so it survives being wrapped
by *any* decorator, not just this project's own known `staticmethod`/`classmethod`/`property`
shapes), and — the wall that finally blocked a real `session.commit()` — a severe bug where
`SomeClass.__hash__` (unbound, class-level access) always hashed the class where the lookup happened
instead of whatever it was actually called on, silently breaking the real `__hash__ = Operators.
__hash__` idiom every SQLAlchemy `Column` relies on. Full list in ORM_PLAN.md Phase 3.

---

## ctypes: real CFUNCTYPE/WINFUNCTYPE callbacks (CTYPES_PLAN.md Phase 2) — 2026-08-15

Native code can now call back into a real Python function through `ctypes.CFUNCTYPE`/`WINFUNCTYPE` —
the last item the native-libraries cross-cutting scenario was missing. Scoped to scalar/pointer-sized
argument and return types, the same practical-subset choice already made for structs/pointers
(Phase 1) — covers every common real Windows callback shape (`EnumWindows`, `qsort` comparators,
`WNDPROC`, …). Verified against a real Windows API needing a real callback, not just "doesn't
crash": `user32!EnumWindows` calls the Python callback once per real top-level window (a positive,
observable count), and returning `False` from the very first call is confirmed to stop enumeration
at exactly one call — both directions of the marshalling round trip independently verified.

Two real, general bugs found and fixed along the way, neither ctypes-specific: `Marshal.
GetFunctionPointerForDelegate` rejects any delegate type constructed from a generic definition (even
a fully closed `Func<IntPtr, IntPtr, int>`) — fixed by building a genuinely new, non-generic delegate
type at runtime via `System.Reflection.Emit.TypeBuilder` instead (the standard native-callback
recipe), cached per signature; and a switch *expression* mixing several distinct numeric-typed arms
silently widens every arm to a common type (here `double`) before the method's own `object` return
type ever applies — a real, general C# footgun that boxed an `"i4"`-coded value as `System.Double`
instead of `System.Int32` and crashed the generated trampoline's own unboxing, fixed by casting each
arm to `(object)` explicitly.

---

## ASP.NET Core hosting PySharp (scenario 11) — 2026-08-15

The reverse direction from every other scenario: not PySharp running Python code that implements a
server, but a real ASP.NET Core (Kestrel) host **embedding PySharp as a .NET library**, calling into
real Python plugin `.py` files from real C# minimal-API request handlers —
[samples/AspNetPySharpHost](samples/AspNetPySharpHost/). A small `PythonPluginHost` loads/caches each
plugin as a real `PyModule` and calls a named function directly per request; a `reload` endpoint drops
the cache entry, proving real hot-reload (edit the `.py` file, no host restart, no C# recompile) — two
plugins (string formatting/`datetime`, and a tiered-discount pricing rule) demonstrate real business
logic living outside the compiled binary.

Two more real, general bugs found along the way, neither ASP.NET-specific: `ClrMarshal.Unwrap` had the
exact same footgun as the ctypes callback bug above, but via a ternary this time — `bi >= long.MinValue
&& bi <= long.MaxValue ? (long)bi : bi` widened both arms to `BigInteger` (since `long` converts
implicitly *to* `BigInteger`), so the "fits in long" branch silently never took effect; confirmed live
as a plugin's `len(...)` result reflection-serializing as `{"isPowerOfTwo":false,...}` instead of a
plain JSON number, fixed with an explicit `(object)` cast on each arm. Also added
`ClrMarshal.ToPlainObject`, a new general embedding capability: recursively converts an arbitrary
Python return value (`dict`/`list`/`tuple`/`set`, nested) into a plain, JSON-serializable .NET object
graph — needed by any host that calls into Python without knowing the return shape ahead of time, not
just this one.

Verified live via `WebApplicationFactory`'s real in-process HTTP pipeline — 6 tests, deliberately
isolated into their own test project/assembly
([src/PySharp.Tests.AspNetHosting](src/PySharp.Tests.AspNetHosting/)) rather than living in
`PySharp.Tests`, after `WebApplicationFactory`'s own thread-pool needs were found to intermittently
hang the whole run when sharing a process with `PySharp.Tests`' 1300+ tests (many of which dedicate a
real foreground OS thread per in-flight generator/coroutine). Full write-up in
ASPNET_HOSTING_PLAN.md.

---

## Compatibility

- **Runtime**: .NET 10 (`net10.0`).
- **Target language version**: a subset of Python 3.12.
- **paho-mqtt**: pinned to 2.1.0, MQTT 3.1.1 to IoT Hub.

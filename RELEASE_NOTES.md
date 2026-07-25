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

Test status at the latest checkpoint: **587 green**.

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
- **Still out of scope:** async generators (`yield` inside `async def`), async synchronization
  primitives (`asyncio.Lock`/`Event`/`Queue`/`Semaphore`), and the remaining FastAPI stack
  (`re`/`datetime`/`inspect`, an ASGI server, pydantic — `pydantic-core` is compiled in Rust).

---

## Compatibility

- **Runtime**: .NET 10 (`net10.0`).
- **Target language version**: a subset of Python 3.12.
- **paho-mqtt**: pinned to 2.1.0, MQTT 3.1.1 to IoT Hub.

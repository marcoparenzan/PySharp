# Roadmap — PySharp toward CPython

This document tracks **the distance between PySharp and a real CPython** and the strategy to reduce
it. It is not a commitment to become a CPython replacement: it is the map of *how much is missing* and
*how it gets filled in*, one step at a time.

> **Goal declared by the project.** The author wants to use **only PySharp** to run their own Python,
> building it up incrementally through real scenarios. Parity with CPython is not expected — the
> expectation is to cover, one after another, the scripts that actually matter.

---

## Method: scenario-driven development

Progress does **not** proceed by "abstract features" but by **scenarios**: each scenario is **a real
Python script, runnable end-to-end**. The scenario dictates which language features and which stdlib
modules to implement — exactly the method by which paho-mqtt support was born:

```text
run the script  →  ModuleNotFoundError / SyntaxError  →  implement the missing module/feature
       ↑                                                             │
       └────────────────────── repeat until the script runs ────────┘
```

Guiding principle: **the interpreter is made compatible with the script, not the script with the
interpreter**. The script comes from PyPI / the real world *identical to the original*; it is PySharp
that grows.

**Every new scenario brings interpreter feature evolution with it**: adding the scenario means (a)
writing the script in [samples/](samples/), (b) surfacing what is missing, (c) implementing it in
`src/PySharpLib/`, (d) covering it with tests, (e) updating this roadmap.

---

## Scenarios

| # | Scenario | Script | Status | Features/modules it drives |
|---|---|---|---|---|
| 1 | **Azure IoT Hub device** (MQTT on paho-mqtt) | [samples/iothub_device_mqtt.py](samples/iothub_device_mqtt.py) | ✅ **Done** | `socket`, `ssl`, `select`, `threading`, `struct`, `hashlib`/`hmac`/`base64`, generators, classes |
| 1b | **Azure IoT Hub device, async** (aiomqtt) | [samples/iothub_device_aiomqtt.py](samples/iothub_device_aiomqtt.py) | ✅ **Done** (verified end-to-end against a real Azure IoT Hub) | `contextlib`, `asyncio.Queue`/`Lock`/`Event`/`Semaphore`, `asyncio.wait`, event-loop `add_reader`/`add_writer`/`run_in_executor`, real `dataclasses` field generation |
| 2 | **FastAPI API** (no SQL) | [http_api.py](samples/http_api.py) · [async_api.py](samples/async_api.py) | 🟡 **In progress** (2.0/2.0+/2a/2b/2c ✅, 2d 🟡 pydantic BaseModel working, 2e ⚪ not started) | ~~`async`/`await` (core)~~ ✅, ~~`asyncio`~~ ✅, ~~`re`/`datetime`/`inspect`/real `typing`~~ ✅, ~~`contextlib`~~ ✅, ~~`abc`~~ ✅, pydantic (import + BaseModel ✅, more validators/`__slots__` open), ASGI |
| 3 | **SQL access** (SQLite, then Postgres) | _to be created_ | ⚪ Planned | `sqlite3` DB-API module (C# shim on `Microsoft.Data.Sqlite`), then `Npgsql` |
| 4 | **HTTP client** (requests-like) | _to be created_ | ⚪ Planned | full `http.client`/`urllib.request`, `re`, headers/redirects, maybe pure `requests` |
| 5 | **MQTT subscribe on a broker** (client) | [mqtt_subscribe.py](samples/mqtt_subscribe.py) | ✅ **Done** | *none* — paho's subscribe side already ran; real round-trip on test.mosquitto.org |
| 6 | **MQTT broker** (server) | _to be created_ | ⚪ Planned | MQTT server on the C# `socket`: MQTT packet parsing (`struct`), session/topic management |
| 7 | **AMQP / RabbitMQ** | _to be created_ | ⚪ Planned | AMQP 0-9-1 client (e.g. pure `pika`) on the `socket`, or a C# shim on `RabbitMQ.Client` |
| 8 | **File system API** | _to be created_ | 🟡 Partial | `os`/`io`/`open` partly exist; complete `os.path`, `pathlib`, `shutil`, `glob` |
| 9 | **JSON + YAML (de)serialization** | [config_yaml.py](samples/config_yaml.py) | ✅ **Done** | new C# **`yaml`** module (safe_load/safe_dump, PyYAML subset); `json` already present |
| T | **Native libraries** (cross-cutting) | _per-case_ | 🟡 Partial | `ctypes` exists; for rich APIs a dedicated **C# wrapper/shim** is created |

Legend: ✅ done · 🔴 in progress/next · ⚪ planned · 🟡 partial/close.

Scenarios 4–9 are **backlog** collected with the author. **5** (MQTT subscribe) and **9** (JSON+YAML)
are **already done** (see below); **4/6/7/8** remain to be prioritized.

> **Realism note on ordering.** Technically scenario 3 (SQL) is **simpler** than scenario 2 (FastAPI):
> SQLite is a well-bounded C# shim, whereas FastAPI requires the **heaviest work of all** — `async`/
> `await` in the language core, an ASGI stack, and pydantic. The order here reflects the priority
> declared by the author (FastAPI first); it stays noted that, if a quick win were wanted first, SQL
> would be the natural candidate.

### Scenario 1 — Azure IoT Hub device ✅

The script [samples/iothub_device_mqtt.py](samples/iothub_device_mqtt.py) is a **complete IoT device**
that talks to **Azure IoT Hub** directly over **MQTT 3.1.1 on TLS (port 8883)**, using the standard
Python library **paho-mqtt** downloaded from PyPI *unmodified*. What it does, in detail:

- **Authentication**, two modes: **SAS token** (derives an HMAC-SHA256 from the connection string,
  with expiry, used as the MQTT password) or **X.509 client certificate** (self-signed, registered on
  the device, via `ssl.load_cert_chain`).
- **TLS connection** to `<hub>.azure-devices.net:8883` with `ssl.SSLContext`, username in the form
  `<host>/<device>/?api-version=2021-04-12`.
- **Device-to-cloud telemetry (D2C)**: publishes JSON messages on `devices/<id>/messages/events/`.
- **Cloud-to-device messages (C2D)**: subscribes to `devices/<id>/messages/devicebound/#` and receives
  them.
- **Device twin**: `GET` of the document (`$iothub/twin/GET`), sending **reported properties**
  (`PATCH .../reported`), and live reception of **desired properties** (`PATCH .../desired`), to which
  it reacts by reporting the applied state.
- **Network loop**: a timed loop (`client.loop(timeout=...)`) that processes I/O and callbacks.

**Why it is a severe test for the interpreter.** It is not "hello world": it exercises in one shot
non-blocking TCP + **TLS** (`socket`/`ssl`/`select` cooperating), **threads and locks** (paho uses
`threading`), **generators** and protocols, **classes** with callbacks/dunders, `struct` for MQTT
framing, and `hashlib`/`hmac`/`base64`/`urllib.parse` for the SAS. These are the ~20 stdlib modules
written in C# *because paho and this script import them* — see the "Interpreter evolution log".

**Status.** Complete and **tested end-to-end against a real Azure hub** (D2C, C2D, twin GET, reported
and desired). Covered offline by the M9 tests ([IoTHubSampleTests.cs](src/PySharp.Tests/M9_IoTHub/IoTHubSampleTests.cs)):
connection-string parsing, SAS token compared against the C#-computed HMAC, MQTT client construction
with credentials. Example config in
[samples/config.iothub_device_mqtt.json](samples/config.iothub_device_mqtt.json).

### Scenario 1b — Azure IoT Hub device, async (aiomqtt) ✅

Async counterpart of scenario 1: [samples/iothub_device_aiomqtt.py](samples/iothub_device_aiomqtt.py)
is the same device (SAS auth, D2C, C2D, device twin) rewritten against **aiomqtt** (2.5.1, from PyPI,
unmodified) instead of paho-mqtt callbacks + a manual network loop —
`async with aiomqtt.Client(...) as client: await client.subscribe(...); async for message in
client.messages: ...`. Rationale for the two styles (paho-mqtt for blocking scripts/workers, aiomqtt
when the app is already async): https://scadaprotocols.com/python-mqtt/. Full phased build log,
including every bug found along the way, in `AIOMQTT_PLAN.md` at the repo root.

**Why it was a severe test, on top of scenario 1's.** aiomqtt only depends on paho-mqtt (already
vendored) and drives a real chunk of `asyncio` PySharp didn't have: `Queue`/`Lock`/`Event`/`Semaphore`,
`wait`/`FIRST_COMPLETED`, and a full event-loop reactor (`add_reader`/`add_writer`/
`call_soon_threadsafe`/`run_in_executor`) — plus a `contextlib` module that didn't exist at all. Built
in the same "run the real script → fix the next error → test → repeat" loop as every other scenario;
see the "Interpreter evolution log" below.

**Bugs found in already-shipped code, not just gaps.** Running the *real* package against a *real*
broker surfaced defects that offline unit tests hadn't: `isinstance(x, int)` was `False` for
`enum.IntEnum` members, which silently made paho's own CONNACK-success check always take the failure
branch (**every** aiomqtt connection, successful or not, raised `MqttConnectError`); `create_task()`
rejected a bare `Future` (only accepted a real coroutine), breaking `client.messages` iteration; a
poller-thread/loop-thread race could fire an `add_reader` callback twice before the first run drained
the socket. All three are now fixed and regression-tested.

**Status.** Complete and **tested end-to-end against a real Azure IoT Hub** (D2C, C2D, twin GET,
reported and desired — same trust boundary scenario 1 already relied on), after first proving the
core flow — connect, subscribe, concurrent publish, `async for message in client.messages`, clean
disconnect — against the real public broker (`test.mosquitto.org`, plain and, separately, over TLS
against a properly-trusted host to confirm `ssl` itself is correct). The real Azure run surfaced one
more TLS-reactor-only bug invisible to both of those: `SslModule.fileno()` never registered the
socket's handle with `SocketModule`'s fd registry (unlike the plain `socket` module's `fileno()`), so
`add_reader`/`add_writer` could never resolve a TLS socket's fd — the connection hung forever right
after opening, since the CONNACK reader callback could never fire. Fixed with one line (see
`AIOMQTT_PLAN.md` Phase 6.4); confirmed working end-to-end afterward. Offline tests:
[AiomqttSmokeTests.cs](src/PySharp.Tests/M15_Aiomqtt/AiomqttSmokeTests.cs).

### Scenario 2 — FastAPI API (no SQL) 🔴 — **key scenario**

**Why it is key.** An API is the capability that turns PySharp from a *script runner* into a *service
host*. A script (like the IoT one) starts, does its thing, ends; an API **stays alive, listens,
responds** — the model of almost all production software. It is the moment when "I use only PySharp
for my Python" stops being about command-line tools and starts being about real services. That is why
it is the priority even though it is also the most expensive.

**Phased approach (walking skeleton first).** "Key API" does **not** mean "FastAPI+uvicorn on day 1":
FastAPI is the heaviest target because it drags in `async`/ASGI/pydantic all together. The strategy is
to reach a live HTTP endpoint early and then grow toward FastAPI compatibility, with the working API
as the test bench.

- **2.0 — Minimal HTTP API (no async).** ✅ **Done.** Synchronous HTTP server in **pure Python** over
  the existing `socket` module ([samples/http_api_min.py](samples/http_api_min.py)): `def` handlers
  with a `@route` decorator, routing by `(method, path)`, query-string parsing, JSON response, 404.
  **It ran with no interpreter changes** — the C# `socket` already covered `bind`/`listen`/`accept`/
  `recv`/`sendall`. Verified with `curl` on `/`, `/health`, `/hello?name=…` and a 404. Zero async,
  zero pydantic: the first tangible result of the scenario.
- **2.0+ — "FastAPI-shaped" hardening.** ✅ **Done.** A more expressive mini-framework
  ([samples/http_api.py](samples/http_api.py)) that replicates FastAPI's **internal mechanism**, still
  synchronous: **path parameters** (`/items/{item_id}`), JSON body on POST, and above all
  **type-hint-driven parameter validation + coercion + injection**, read at runtime from
  `handler.__annotations__` (422 on invalid values, defaults injected). Verified with `curl` on all
  endpoints + error cases. **Interpreter evolution required and delivered in this round** (see log
  below): populating `__annotations__` and `__name__` on builtins.
- **2a — `async`/`await` in the language.** ✅ **Done.** The parser accepts `async def`/`await`/
  `async for`/`async with`, and the interpreter runs coroutines with a real suspension model:
  each coroutine executes its body on a dedicated thread and suspends at every `await` on a
  pending Future through a semaphore handshake (the same technique as generators), so only one
  coroutine runs at a time — cooperative single-threading, like CPython. `await` of a coroutine
  delegates (à la `yield from`); exceptions propagate across `await`; `async with`/`async for`
  drive `__aenter__`/`__aexit__`/`__aiter__`/`__anext__`. See [PyCoroutine](src/PySharpLib/Runtime/Async.cs).
- **2b — `asyncio` module.** ✅ **Done.** A .NET-backed **event loop** ([Async.cs](src/PySharpLib/Runtime/Async.cs),
  [AsyncioModule.cs](src/PySharpLib/Modules/AsyncioModule.cs)) with a ready queue, timer heap
  (`call_later`/`sleep`) and cross-thread wake-ups for offloaded I/O. Implements `run`, `sleep`,
  `gather` (with `return_exceptions`), `create_task`, `ensure_future`, `wait_for`, `get_running_loop`,
  `Future`/`Task`, and **asynchronous socket I/O** on the loop (`sock_accept`/`sock_recv`/`sock_sendall`,
  backed by .NET async sockets). Blocking I/O is offloaded to the thread pool and rejoined via
  `call_soon`, keeping Python objects single-threaded. Proven by [async_api.py](samples/async_api.py):
  a fully asynchronous HTTP server where a slow handler does not block the others.
  ~~Still open: async synchronization primitives (`Lock`/`Event`/`Queue`), `asyncio.Semaphore`~~ ✅
  — delivered by scenario 1b's aiomqtt work (`AIOMQTT_PLAN.md` phases 2–3), which also added the
  event-loop reactor (`add_reader`/`add_writer`/`call_soon_threadsafe`/`run_in_executor`) this
  scenario will need for a real ASGI server.
- **2c — cross-cutting stdlib.** ✅ **Done** (closed as part of the pydantic v1 probe-driven push — see
  `FASTAPI_PLAN.md` Phase 1). `re` (a full `System.Text.RegularExpressions`-backed engine), `datetime`,
  `inspect` (`signature`/`Signature`/`Parameter`, incl. their real constructors, not just the internal
  builder path), real `typing` (`get_type_hints`, `Annotated`, generic-alias tracking with real
  `__origin__`/`__args__`, the generalized `__mro_entries__` protocol), ~~`contextlib`~~ ✅ (scenario
  1b), `abc`, `pathlib`, `weakref`, `ipaddress`, `pickle`, `colorsys`, `decimal`, `complex`. `email`/
  `http` for the HTTP details remain open for 2e.
- **2d — data validation (pydantic).** 🟡 **In progress**, pydantic **v1** chosen (pure Python, no
  `pydantic-core` Rust wall). `import pydantic` succeeds end to end; a `BaseModel` subclass now
  constructs, validates real field types, raises real `ValidationError` on bad input, and serializes
  via `.dict()`. Getting there required building real (simplified) **custom-metaclass support** into
  the interpreter's class-statement execution (`ExecClassDef`) — the first scenario where "custom
  metaclasses are ignored" (a deliberate v1 simplification) actually blocked something, since real
  pydantic's `ModelMetaclass.__new__` must run while `class User(BaseModel): ...` executes to build
  `__config__`/`__fields__`/validators. Known remaining gap: `.dict()` leaks a `__fields_set__` key,
  since PySharp doesn't implement real `__slots__`-backed storage separate from an instance's regular
  attributes. Full phased log in `FASTAPI_PLAN.md`.
- **2e — ASGI server + FastAPI.** ⚪ Not started. uvicorn is async-native. Options: a mini ASGI server
  written over the C# `socket`, or starlette (pure but with async dependencies).

Milestone outcome: first (2.0) an HTTP endpoint answering a GET on `localhost` with synchronous
handlers; then (2a–2e) the same reached in **FastAPI** compatibility, all run by PySharp. Full
scenario-2 status, including the pydantic v1 probe-driven blow-by-blow, lives in `FASTAPI_PLAN.md` —
this roadmap entry is kept in sync at each major checkpoint but the plan doc is the live source of
truth while 2d/2e are in progress.

### Scenario 3 — SQL access ⚪

- **3a — `sqlite3` DB-API.** New C# module in `Modules/` exposing `connect`/`Connection`/`Cursor`/
  `execute`/`executemany`/`fetchone`/`fetchall`, backed by `Microsoft.Data.Sqlite`. Open decision:
  whether the NuGet dependency goes on "pure" `PySharpLib` or an isolated project/module.
- **3b — Postgres.** Same DB-API shape backed by `Npgsql` (when a server is available).

### Scenario 5 — MQTT subscribe on a broker ✅

The script [samples/mqtt_subscribe.py](samples/mqtt_subscribe.py) performs a **real MQTT round-trip**:
it connects to a public broker (`test.mosquitto.org:1883`, plaintext), subscribes to a unique topic,
publishes 3 JSON messages there and receives them back via `on_message`. **No interpreter changes**:
paho's subscribe side already ran since scenario 1 (which used `client.subscribe`). It confirms the
MQTT/network engine is solid outside the IoT Hub case too. Prerequisite: `pysharp install paho-mqtt`.

### Scenario 9 — JSON + YAML (de)serialization ✅

The script [samples/config_yaml.py](samples/config_yaml.py) loads a **YAML** configuration, inspects
it (correct types: int/bool/str/null, lists, nested mappings) and converts it back and forth between
YAML and JSON verifying the round-trip. `json` was already present; a **C# `yaml` module** was added
([YamlModule.cs](src/PySharpLib/Modules/YamlModule.cs)) with `safe_load`/`load`/`safe_dump`/`dump` over
a **practical PyYAML subset**: block mapping/sequence with indentation, flow style (`[..]`/`{..}`),
typed scalars, quoting, comments, the `---` marker. Out of scope for v1: block scalars `|`/`>`,
anchors/aliases, explicit tags, multiple documents. Covered by [YamlTests.cs](src/PySharp.Tests/M6_Stdlib/YamlTests.cs).

### Cross-cutting — Native libraries 🟡

General rule: **a native library is invoked from C#**, so it is exposed to Python either (a) via
`ctypes` for simple scalar calls (already supported for basic signatures), or (b) by creating a
dedicated **C# wrapper/shim** that presents an idiomatic Python API over the .NET/native lib. It is
the same strategy as scenario 3 (`sqlite3` is a shim on a .NET driver).

### Cross-cutting — .NET object injection (embedding interop) ✅

The host can now inject **any .NET object or `Type`** into the script scope with
`engine.SetVariable(name, obj)` and use it idiomatically from Python — method calls (with overload
resolution), property/field read-write, indexers, construction, `IEnumerable` iteration, delegate
calls — with automatic two-way marshalling. This is the general, reflection-based counterpart to the
per-library C# shim: instead of writing a module you hand the object straight to the script. See
[Clr.cs](src/PySharpLib/Runtime/Clr.cs), the README "Injecting .NET objects" section, and the
[M11_Interop](src/PySharp.Tests/M11_Interop/) tests. Open: `ref`/`out`, generic-method inference,
events, and passing Python callables **into** .NET as delegates (the reverse direction).

---

## Interpreter evolution log (per scenario)

Every scenario brings interpreter feature evolution with it. This records *what was added to the core*
and *which scenario drove it*.

| Scenario | Interpreter evolution | Files |
|---|---|---|
| 1 — IoT Hub | 20 stdlib modules (`socket`, `ssl`, `select`, `threading`, `struct`, …), generators, classes, exceptions | `Modules/`, core |
| 2.0 — HTTP API | *none* — the existing `socket` was enough | — |
| 2.0+ — FastAPI-shaped API | **`__annotations__` populated** with the type callables (lazy, best-effort evaluation of parameter annotations); **`__name__`/`__qualname__`/`__module__` on builtin functions** (e.g. `int.__name__`) | [Interp.cs](src/PySharpLib/Interpretation/Interp.cs) `TryGetAttr`; test [IntrospectionTests.cs](src/PySharp.Tests/M4_Functions/IntrospectionTests.cs) |
| 2.0++ — full signature | **`fn.__code__`** with `co_varnames`/`co_argcount`/`co_kwonlyargcount`/`co_posonlyargcount`/`co_name`: exposes the **parameter names** (including unannotated ones) → full-signature injection, not just annotated parameters | [Callables.cs](src/PySharpLib/Runtime/Callables.cs) `PyCode`; `TryGetAttr`; tests as above |
| 2.0+++ — return annotation | **`__annotations__['return']`**: the handler's `-> T` is now captured (propagated from `FuncDef.Returns` to `PyFunction`). It promoted the corpus snippet `syntax_type_hint.py` from Xfail to Supported | [Callables.cs](src/PySharpLib/Runtime/Callables.cs) `PyFunction.Returns`; `MakeFunction`; `TryGetAttr` |
| 2a — async/await | **coroutines in the language core**: `async def`/`await`/`async for`/`async with`, `AwaitExpr` in the AST, a thread-backed `PyCoroutine` with a suspension handshake, coroutine delegation, exception propagation across `await`, `StopAsyncIteration` | [Parser.cs](src/PySharpLib/Parsing/Parser.cs), [Ast.cs](src/PySharpLib/Parsing/Ast.cs), [Async.cs](src/PySharpLib/Runtime/Async.cs), [Interp.cs](src/PySharpLib/Interpretation/Interp.cs); tests [M10_Async](src/PySharp.Tests/M10_Async/) |
| 2b — asyncio | new stdlib module **`asyncio`**: .NET-backed event loop, `Future`/`Task`, `run`/`sleep`/`gather`/`create_task`/`wait_for`, **async socket I/O** (`sock_accept`/`sock_recv`/`sock_sendall`) | [AsyncioModule.cs](src/PySharpLib/Modules/AsyncioModule.cs), [Async.cs](src/PySharpLib/Runtime/Async.cs); tests [AsyncioTests.cs](src/PySharp.Tests/M10_Async/AsyncioTests.cs), [AsyncServerTests.cs](src/PySharp.Tests/M10_Async/AsyncServerTests.cs) |
| 5 — MQTT subscribe | *none* — paho's subscribe side already ran | — |
| 9 — JSON + YAML | new stdlib module **`yaml`** (safe_load/dump, PyYAML subset) | [YamlModule.cs](src/PySharpLib/Modules/YamlModule.cs); test [YamlTests.cs](src/PySharp.Tests/M6_Stdlib/YamlTests.cs) |
| 1b — aiomqtt | new stdlib module **`contextlib`** (`contextmanager`/`suppress`, needed `PyGenerator.ThrowInto` — inject an exception at a suspended `yield`, like `gen.throw()`); **`asyncio.Queue`/`Lock`/`Event`/`Semaphore`/`BoundedSemaphore`**, **`asyncio.wait`/`FIRST_COMPLETED`**; event-loop **`add_reader`/`add_writer`/`call_soon_threadsafe`/`run_in_executor`** (a `Socket.Select`-based poller thread + a `SocketModule` fd→`Socket` registry); real **`dataclasses`** field-driven `__init__`/`__repr__`/`__eq__`/frozen generation (walks the MRO so a no-new-fields subclass still inherits its base's fields); `types` module (`TracebackType`); `sys.version_info` rich comparison against a tuple; `typing.Concatenate`/`Self`/`TypeAlias`/`ParamSpec`; `isinstance(x, int)` now recognizes `IntEnum` members; `Interp.SetAttr`'s `__setattr__` dispatch now accepts a builtin (not just user-defined) hook; `ssl.CertificateError` (alias of `SSLCertVerificationError`); `create_task`/`loop.create_task` relaxed to accept a bare `Future` | [ContextlibModule.cs](src/PySharpLib/Modules/ContextlibModule.cs), [AsyncioModule.cs](src/PySharpLib/Modules/AsyncioModule.cs), [Async.cs](src/PySharpLib/Runtime/Async.cs), [MiscModules.cs](src/PySharpLib/Modules/MiscModules.cs), [SocketModule.cs](src/PySharpLib/Modules/SocketModule.cs), [SslModule.cs](src/PySharpLib/Modules/SslModule.cs), [SysModule.cs](src/PySharpLib/Modules/SysModule.cs), [Builtins.cs](src/PySharpLib/Builtins/Builtins.cs), [Interp.cs](src/PySharpLib/Interpretation/Interp.cs), [PyGenerator.cs](src/PySharpLib/Runtime/PyGenerator.cs); full log in `AIOMQTT_PLAN.md` |
| 2c/2d — pydantic v1 (real dependency-chain probe) | **~70 real gaps closed** driving `import pydantic` from failing to succeeding, then a `BaseModel` subclass to constructing/validating/`.dict()`-ing: new stdlib modules `re` (real regex engine), `datetime`, `ipaddress` (incl. real `_BaseAddress`/`_BaseNetwork` base classes), `pathlib`, `weakref`, `pickle`, `colorsys`; real `typing`/`types` metaprogramming (`get_type_hints`, generic-alias tracking with real `__origin__`/`__args__`, the generalized `__mro_entries__` protocol enabling `TypedDict` as a base class, `types.new_class`/`resolve_bases`/`prepare_class`, the 3-arg `type(name, bases, ns)` form); real (simplified) **custom-metaclass support** in `ExecClassDef` (`class X(Y, metaclass=M): ...` now calls `M.__new__` for real, with `super().__new__`/direct stub-base `.__new__` access both bottoming out at a real class-build fallback); `object.__setattr__(obj, '__dict__', newdict)` bulk-namespace-replace; `issubclass`/`isinstance` accepting builtin types as either argument; `dict.keys()` as a real (order-preserving) dict_keys-shaped view usable with the set operators; `v.__class__` returning the real constructible builtin type instead of a bare stand-in. Two more generically important interpreter bugs found along the way (not pydantic-specific): `from pkg import name` was masking real fallback-import errors behind a generic message; `globals()`/`locals()` at true module top level leaked writes into the shared builtins module. Full phased blow-by-blow (every fix with its own regression test) in `FASTAPI_PLAN.md` | `Modules/ReModule.cs`, `Modules/DateTimeModule.cs`, `Modules/IpAddressModule.cs`, `Modules/PathlibModule.cs`, `Modules/WeakrefModule.cs`, `Modules/PickleModule.cs`, `Modules/ColorSysModule.cs`, `Modules/MiscModules.cs`, `Modules/GenericAliasModule.cs`, `Interpretation/Interp.cs` (`ExecClassDef`, `TryGetAttr`), `Runtime/PyClass.cs` (`Metaclass`), `Runtime/PyDict.cs` (`PyDictKeysView`), `Builtins/Builtins.cs`; tests `M6_Stdlib`, `M4_Functions/MetaclassTests.cs`, `M16_FastApi`; full log in `FASTAPI_PLAN.md` |

With `co_varnames` (names) + `__annotations__` (types, including `'return'`) the **signature is
complete**: the framework injects every parameter, treating unannotated ones as `str` (like FastAPI).

**Known remaining gaps for scenario 2** (not yet closed): `inspect.signature` as an idiomatic wrapper
over `__code__`; evaluating annotations as strings for forward refs; `*args`/`**kwargs` parameters not
handled by the framework's injector.

---

## Distance from CPython (gap analysis)

Four independent axes. Compatibility with "any PyPI package" would require closing almost all of them
— which is why the goal stays *per-scenario*, not universal.

### Axis A — Language

| Supported | Missing (out of scope for v1) |
|---|---|
| arbitrary ints, floats, str/bytes, list/tuple/dict/set + comprehensions, f-strings, functions (defaults/`*args`/`**kwargs`/kw-only/decorators/closures/`global`/`nonlocal`), classes (C3 MRO, `super`, dunders, property, static/classmethod), exceptions, `with`, generators (`yield`/`yield from`), **`async`/`await`/`async for`/`async with` (coroutines)**, **real async generators** (`async def` with `yield` — a hybrid `PyAsyncGenerator` combining generator-style yield-suspension with coroutine-style await-suspension on one dedicated thread; real `__aiter__`/`__anext__`/`athrow`-driven `StopAsyncIteration`, and `contextlib.asynccontextmanager` can now actually be *entered*, not just defined), import system, function introspection (`__annotations__`, `__code__`), complex numbers (`complex`, not the `1j` literal), **custom metaclasses** (real, simplified — `class X(Y, metaclass=M)` calls `M.__new__`; no multi-metaclass conflict resolution, no metaclass `__init__` dispatch), **`match`/`case` structural pattern matching** (PEP 634 — real soft-keyword parsing + full pattern semantics: literal/capture/wildcard/value/sequence/mapping/class/or/as patterns, guards), real `object.__eq__`/`__ne__`/`__hash__`/`__repr__`/`__str__` default dunders (directly/unbound-accessible, not just hardcoded fallbacks), **real recursion-depth guard** (runaway recursion raises `RecursionError`, matching CPython's default limit, instead of crashing the process), real `memoryview` (bytearray-backed views share real underlying storage), `isinstance`/`issubclass` accepting a real union type (`X \| Y`) as the 2nd argument | `exec()`/`eval()`, `1j` complex literal syntax, exception groups (`except*`), `generator.send(v)` with a value, dunders as attributes of builtin *types*, real `__slots__` (separate per-slot storage — every instance attribute lives in the same dict today, slotted or not), real `class X(dict):` subclass storage (instances of a `dict` subclass aren't backed by real dict storage unless the subclass defines its own `__getitem__`/`__setitem__`) |

### Axis B — Stdlib

Implemented **~59 modules** against CPython's **~200**. Present today: `sys`, `os`, `time`, `platform`,
`errno`, `io` (incl. `TextIOWrapper`), `warnings`, `copy`, `socket`, `ssl`, `select`, `threading`, `asyncio` (incl. real `Runner`/`Task`/protocols hierarchy), `struct`, `hashlib`,
`hmac`, `base64`, `string`, `urllib(.parse/.request)`, `uuid`, `json`, `yaml`, `collections`
(`Counter`/`ChainMap`/`deque`), `collections.abc`, `enum`, `functools`, `math`, `logging`, `ctypes`,
`re` (real regex engine, incl. `pos`/`endpos` and `Match.groups(default)` positionally), `datetime`, `ipaddress`, `pathlib`, `weakref`, `pickle`, `colorsys`,
`decimal`, `itertools`, `operator`, `types`, `abc`, `contextlib` (incl. `asynccontextmanager` at
decoration time), `inspect` (incl. a real `isfunction` fix — async/generator functions were
previously misclassified — and real coroutine-state constants/`getcoroutinestate`), `shlex`, `contextvars`, `importlib`, `textwrap`, `signal`,
`concurrent.futures`, `stat`, `subprocess`, `tempfile`, `http`, `http.cookies`, `email.utils`,
`html`, `traceback`, `mimetypes`, `secrets`, `array`, `queue`; real (not stub) `typing`
and `dataclasses`; stub `__future__`.

**High-priority missing**: `sqlite3` (scenario 3).

### Axis C — Native extensions (C/Rust)

The structural wall. Packages like numpy, pandas, psycopg2, cryptography, orjson, **pydantic-core** are
binaries compiled for CPython: **no Python-in-C# interpreter can load them as they are**. Possible
strategies, all *per-package*:

1. use a **pure-Python fallback** if the package offers one (often not);
2. **reimplement the API in C#** as a native module/shim (via `Microsoft.Data.Sqlite`, `Npgsql`,
   `NativeLibrary`, …);
3. `ctypes` for calls to native DLLs with simple signatures.

There is no *generic* path without embedding CPython — which the project chose not to do.

### Axis D — Packaging / pip

The mini-pip installs **only pure wheels** (`py3-none-any`) and **does not resolve dependencies**
(`requires_dist` ignored). A bounded, low-risk improvement: read the transitive dependencies and
install them (with marker parsing), still rejecting non-pure wheels but with a **clean error** (today
`install numpy` exits with an unhandled CLR exception — see [TODO.md](TODO.md)).

---

## Process to add a scenario

1. Write the real script in [samples/](samples/) (or install the target package from PyPI).
2. Run it with the host: `dotnet run --project PySharp -- run samples/<script>.py` (or `pysharp run …`).
3. Collect the errors (`ModuleNotFoundError`, `SyntaxError`, `AttributeError`).
4. Implement the missing module/feature in `src/PySharpLib/` (new file in `Modules/` + registration in
   `StdlibModules.RegisterAll`, or work on the lexer/parser/interpreter).
5. Add the tests (milestones `Mx_*` in `PySharp.Tests/`).
6. **Update this roadmap** (scenario status + gap analysis) and [README.md](README.md).

---

## Test strategy: tests can be written in Python

**Yes.** Besides the C# (xUnit) tests that embed Python source as a string, the project already
supports **tests written as `.py` files**, with a small C# harness that invokes them. It is the
**corpus** mechanism ([CorpusTests.cs](src/PySharp.Tests/Corpus/CorpusTests.cs)):

- each file in `Corpus/snippets/*.py` **is** a test: it contains its own `assert`s;
- a data-driven `[Theory]` enumerates the files and for each runs `PyEngine.Run(file)`;
- "ran without raising" = **pass**; a failed `assert` → `AssertionError` → **fail**;
- a support module [testutils.py](src/PySharp.Tests/Corpus/snippets/testutils.py) is available for
  snippets to import (`assert_raises`, `assert_equal`, `assert_true`, …).

There is also an `Xfail` table for snippets that **must still fail** (out-of-scope features): if one
starts passing, the test turns red and reminds you to **promote** it to Supported — it happened with
`syntax_type_hint.py` when we added the return annotation.

**Practical consequence for scenarios.** A new scenario can bring its own tests in Python: an
`assert`-based script (e.g. `tests/http_api_test.py`) invoked by a `[Theory]` harness, instead of (or
in addition to) C# tests. It is the most natural way to test language-level behavior; C# tests stay
useful when you need to build up state or compare against a value computed in .NET (like the SAS token
in scenario 1).

---

## Progress indicators

- Scenarios: **1, 1b, 5, 9 complete**; **2** well underway — **3.1/3.1b/3.2 all done**: `import
  starlette` succeeds completely and a real `Starlette` app works end to end over a real, minimal
  ASGI HTTP server (`samples/asgi_server.py`) — routing, exception handling (default + custom),
  static files, WebSockets (incl. real async-generator-backed streaming), and lifespan events all
  verified against real, unmodified starlette and anyio, with ~15 real interpreter/stdlib bugs found
  and fixed along the way (full history in `FASTAPI_PLAN.md` Phase 3). **Phase 3 (starlette, anyio
  and a real ASGI server) is substantially complete end to end.**
  **Phase 4.1 done: `import fastapi` succeeds** — pinned to `fastapi==0.99.1`/
  `starlette==0.27.0`/`pydantic==1.10.13` (the last combination built purely against pydantic v1;
  the default `install fastapi` resolves pydantic v2/Rust, the wall this plan avoids). Along the
  way: two serious, general concurrency bugs found and fixed (an import-system deadlock — a
  module-level generator expression evaluated during an import could spawn a real OS thread
  blocked forever on a lock the importing thread wouldn't release; and a flaky-suite MRO-computation
  race from a shared mutable static identity, fixed with `[ThreadStatic]`, confirmed via 41
  consecutive clean full-suite runs); real `eval()` (expression evaluation) and real
  `typing.ForwardRef`/`typing_extensions._AnnotatedAlias`, implemented to resolve fastapi's real,
  genuinely self-referential JSON-Schema-shaped forward refs; and ~15 smaller real stdlib/typing
  gaps. Full blow-by-blow in `FASTAPI_PLAN.md` Phase 4. **Past the milestone**: `FastAPI()`
  constructs, real route registration (incl. path parameters) works, and `app.openapi()` (real
  schema generation) works — `inspect.isroutine`, `inspect.Parameter.__init__` accepting
  `name`/`kind` as keywords, and a real `urllib.parse.urljoin` (ported from CPython's own
  algorithm) all fixed along the way. `httpx==0.28.1` now installs and import gets past two real
  gaps: **PEP 530 async comprehensions** (`[x async for x in y]`) ✅ — a genuine language-feature
  gap (the parser only ever recognized a bare `for`), reusing `async for`'s existing
  `__aiter__`/`__anext__` handshake, no new threading needed since comprehensions are plain C#
  iterators running inline on the enclosing coroutine's own thread; real **`codecs`** ✅
  (`lookup`/`getincrementaldecoder`, backed by .NET's own `Decoder.Convert` — the incremental-safe
  API, not the naively-obvious `GetCharCount`+`GetChars` pairing, which corrupts a multi-byte
  sequence split across chunk boundaries); real **`urllib.request.parse_http_list`** ✅ (ported from
  CPython). Along the way, root-caused a real, reproduced, **pre-existing** intermittent full-suite
  hang (confirmed pre-existing via an isolated baseline-commit worktree, not introduced by this
  round): two test classes drove their own `asyncio.run` event loop without the
  `[Collection("asyncio-run")]` tag every other such class already carries, racing on
  `PyEventLoop._running`'s deliberately process-wide static — fixed by tagging both, confirmed via
  36 consecutive clean full-suite runs afterward (24 under deliberately heavy concurrent CPU
  contention against the still-broken baseline, which failed nearly every time under the same load).
  **`import httpx` now succeeds** — real `http.cookiejar` (Cookie/CookieJar, RFC 6265-style
  domain/path matching, real Set-Cookie parsing), real `urllib.request.Request`, real `zlib`
  (compress/decompress/decompressobj over .NET's own compression streams), real enum member
  tuple-value unpacking via a class-defined `__new__` (plus a new `int.__new__(cls, value)`), real
  `typing.TypedDict` subclass construction (returns a plain dict, matching real CPython's runtime
  erasure) and real functional-syntax `typing.NamedTuple`, `urllib.parse.parse_qs`, `bisect`,
  `unicodedata` (category/normalize fully real via .NET's own UCD; combining/bidirectional/name
  honestly scoped to ASCII), `netrc`, arbitrary attributes on builtin functions
  (`PyBuiltinFunction.Attributes`), and `sys.maxunicode` — plus 3 PyPI installs (`idna`, `sniffio`,
  `rfc3986`) and a real version-pin fix (`httpx==0.23.3`, since `starlette==0.27.0`'s `TestClient`
  needs the old `Client(app=...)` convenience param modern httpx removed). The astral-regex wall
  from the previous round is now **solved for real**: character classes with astral (>U+FFFF)
  Unicode ranges are decomposed into the standard UTF-16 surrogate-pair sub-range fragments before
  reaching .NET's regex engine (the same technique JS's own `u`-flag polyfills use), handling the
  general multi-high-surrogate case, not just a special case. Also fixed reaching this: a real,
  callable `.__hash__` on every function/builtin (previously only the top-level `hash()` builtin
  worked); real `atexit` (callbacks actually run in reverse order at script end, scoped per engine
  instance, not a shared static); real `importlib.resources` (`files()`/`as_file()`, a real
  `pathlib.Path` via the package's own tracked `__file__`); real `logging.addLevelName`/
  `getLevelName`. Plus 3 more PyPI installs (`certifi`, `httpcore`, `h11`), all compatible with the
  already-pinned `httpx==0.23.3`. **`import httpx` succeeds**, and constructing a real
  `starlette.testclient.TestClient` gets substantially further into `httpcore`/`h11`'s own import
  chain. **Current frontier**: real `h11` compiles several regexes from **bytes** patterns
  (`re.compile(rb"[0-9]+")`) — real CPython's `re` supports both `str` and `bytes` patterns, but
  PySharp's `re.compile()` only ever accepted `str`. A genuinely separate, substantial feature (every
  `re` entry point needs bytes-vs-str mode awareness, matching via a byte-preserving encoding since
  .NET's `Regex` only operates on `string`), not started this round. Phase 4.2 (a real target sample
  app) not started. 6/7/8 to do; native cross-cutting partial.
- Stdlib modules: **~65 / ~200** of CPython (added `re`, `datetime`, `ipaddress`, `pathlib`, `weakref`,
  `pickle`, `colorsys`, `decimal`, `itertools`, `operator`, `types`, `abc`, `contextlib`, `inspect`,
  `shlex`, `contextvars`, `importlib`, `importlib.resources`, `textwrap`, `signal`,
  `concurrent.futures`, `stat`, `subprocess`, `tempfile`, `http`, `http.cookies`, `http.cookiejar`,
  `email.utils`, `html`, `traceback`, `mimetypes`, `secrets`, `array`, `queue`, `codecs`, `zlib`,
  `bisect`, `unicodedata`, `netrc`, `atexit`; `typing`/`dataclasses` upgraded from stubs to real
  implementations).
- Language axes: core subset covered; **complete** signature introspection (`__annotations__` ✅ with
  `'return'`, `__code__.co_varnames` ✅, `inspect.signature` ✅); real (simplified) **custom-metaclass
  support** ✅; `complex` ✅ (the type, not the `1j` literal); `async`/`await` ✅ (incl. real
  `asyncio.Runner`/`Task`, the real `asyncio.protocols` base-class hierarchy); **`match`/`case`
  (PEP 634)** ✅; real `abc.ABC.register()` virtual-subclass support ✅; real `object.__eq__`/
  `__ne__`/`__hash__`/`__repr__`/`__str__` defaults ✅; **real recursion-depth guard** ✅ (runaway
  recursion raises `RecursionError` instead of crashing — this interpreter had no recursion limit at
  all before); real `memoryview` ✅ (bytearray-backed views share real underlying storage); real
  `isinstance`/`issubclass` acceptance of a genuine `X | Y` union as the 2nd argument ✅; every
  callable's real `.__call__` attribute ✅; **`inspect.isfunction` correctness fix** ✅ (previously
  excluded async/generator functions entirely — a real bug that would have broken every `async def`
  route handler in any real ASGI framework built on this interpreter); **`re.Match.groups(default)`
  correctness fix** ✅ (previously only readable via kwargs, not positionally — a real bug that would
  have broken any path route with an untyped parameter); **`isinstance(task, Future)` correctness
  fix** ✅ (a real `Task` genuinely is-a `Future` in CPython; the flat type-name comparison used for
  builtin-name isinstance checks couldn't see that through PyTask's real C# inheritance on its own);
  PySharp's own submodules (e.g. `asyncio.subprocess`) now attach to their parent module as a real
  attribute right after a plain import, matching real CPython's package `__init__.py` behavior, not
  just via an explicit dotted import ✅; `exec`/`eval` still missing (Axis A); real `__slots__`
  (separate per-slot storage) still missing; async generators still missing (blocks *entering*
  `contextlib.asynccontextmanager`-wrapped functions, though defining/decorating them works); real
  `class X(dict):` subclass storage still missing (instances aren't backed by real dict storage
  unless the subclass defines its own `__getitem__`/`__setitem__`); **real `asyncio.current_task()`**
  ✅ (previously always `None`; now a thread-static explicitly propagated across every nested
  `await`'s dedicated OS thread — needed by real anyio cancel-scope code); **`LogicalThread`** ✅, a
  new propagated per-Python-thread identity fixing a structural bug where `threading.local` state set
  inside a `@contextmanager` generator (before its `yield`) was invisible in the `with`-body (`
  PyGenerator`, like `PyCoroutine`, runs its body on its own dedicated OS thread); **`Interp.DelAttr`
  now dispatches a class's `__delattr__`** ✅ (previously only `SetAttr` checked `__setattr__` —
  asymmetric and a real gap for any type routing attribute deletion through `__delattr__`);
  **`TryGetAttr`'s `__getattr__` fallback now catches a raised `AttributeError`** ✅ (previously
  always returned found, even when `__getattr__` itself raised — broke `getattr(obj, name,
  default)`/`hasattr` for any type relying on that standard contract); **`iscoroutinefunction`/
  `isgeneratorfunction` now see through a bound method** ✅ (previously only matched a raw function;
  real CPython unwraps a bound method first — this broke `is_async_callable` for any bound `async
  def` instance method, including starlette's real default 404 handler); real **`os.stat`/
  `os.stat_result`/`os.path.normpath`/`realpath`/`commonpath`** ✅ (didn't exist at all; found via
  starlette's real `staticfiles.py`); real **`importlib.util.find_spec`** ✅, backed by a new
  `Importer.FindModuleSpec` (locates a module without importing/executing it); real
  **`collections.abc.Mapping.get`** mixin ✅ (the ABC previously had no real methods at all — a
  documented v1 simplification, closed once a real scenario needed it), with `MutableMapping` now
  properly deriving from `Mapping` (previously two unrelated placeholder classes); real **async
  generators** ✅ (`async def` with `yield` — a new `PyAsyncGenerator` hybridizing generator-style
  yield-suspension with coroutine-style await-suspension on one dedicated thread; real `__aiter__`/
  `__anext__`/`athrow`-driven `StopAsyncIteration`); real **`contextlib.asynccontextmanager`
  entering** ✅ (`__aenter__`/`__aexit__` previously raised `NotImplementedError` unconditionally,
  a direct consequence of the async-generator gap — now real, driven by `PyAsyncGenerator`); real
  **`isasyncgenfunction`/`isasyncgen`** ✅ (previously hardcoded `False`), and
  `iscoroutinefunction` now correctly excludes async generator functions (mutually exclusive
  categories in real CPython); **real `sys.path` mutation** ✅ (`sys.path.insert(...)`/`.append(...)`
  from Python code previously had zero effect on actual import resolution — it mutated a disconnected
  snapshot copy, not the list the importer actually consults; found via a real ASGI server sample);
  real **`bytes.partition`/`rpartition`** ✅ (only `str` had them before); **fixed a real deadlock in
  the import system** ✅ (`Importer.ImportAbsolute` held a lock across its entire recursive load-
  and-*execute* loop; a module-level generator expression evaluated during an import could spawn a
  real OS thread that blocked forever on that lock — narrowed to only the module-registry
  bookkeeping); real **`email.message.Message`** ✅ (didn't exist at all); real **`Morsel._reserved`
  as an actual class attribute** ✅ (was a module-level name closed over by methods, invisible to
  starlette's real `Morsel._reserved[...] = ...` module-level patch); unified **`typing.NoneType`
  with `type(None)`** ✅ (were two different objects; `Optional[X]`'s implicit `None` member is now
  identical to what `None.__class__` returns); real **`issubclass` delegation for typing generics**
  ✅ (`issubclass(list, typing.List)` now delegates to the real mapped origin, matching CPython's
  `_SpecialGenericAlias.__subclasscheck__`); real **`inspect.Parameter.replace(**changes)`** ✅
  (didn't exist); **fixed a second real concurrency bug** ✅ (`GenericAliasModule.OriginMap`/
  `ArgsTransform` were plain, non-thread-safe `Dictionary`s written on every `import typing` and
  read on every `issubclass` call — under real parallel test execution this could corrupt their
  internal state; switched to `ConcurrentDictionary`); real **`eval()`** ✅ (expression evaluation,
  real CPython's own full scope for it — previously raised `NotImplementedError`, an Axis A gap
  never before exercised by a real scenario); real **`typing.ForwardRef`** ✅ (real `__init__`/
  `_evaluate`/`__eq__`/`__hash__`; bare string type arguments now auto-wrap into one, matching
  CPython's `_type_check`); real **`typing_extensions._AnnotatedAlias`** ✅ (real `__init__`
  storing `__origin__`/`__metadata__`/`__args__`); **fixed a third real concurrency bug** ✅
  (`GenericAliasModule.GenericPlaceholder`, a single shared mutable static overwritten by every
  concurrent `import typing`, causing an intermittent flaky-suite MRO failure — fixed with
  `[ThreadStatic]`); real **`hash()` dunder dispatch** ✅ (never consulted a `PyInstance`'s own
  `__hash__`, unlike `==`); `email.message.Message`, `binascii.Error`, `http.client.responses`
  didn't exist. **`import fastapi` now succeeds.** Past that: real **`inspect.isroutine`** ✅
  (unblocked `FastAPI()` construction — starlette's `routing.py` calls it while building every
  `Route`); **`inspect.Parameter.__init__`** ✅ now accepts `name`/`kind` as keywords, not just
  positionally (real fastapi's `get_typed_signature` calls it entirely by keyword — unblocked real
  route registration, incl. path parameters, and `app.openapi()` schema generation); real
  **`urllib.parse.urljoin`** ✅ (a direct port of CPython's own RFC-3986 relative-resolution
  algorithm — didn't exist at all before). Then, chasing `httpx`: real **PEP 530 async
  comprehensions** ✅ (`[x async for x in y]` — a genuine parser/language gap, not a stdlib one; the
  parser only ever recognized a bare `for` at every comprehension-start site); real **`codecs`** ✅
  (`lookup`/`getincrementaldecoder`, backed by .NET's own incremental-safe `Decoder.Convert`); real
  **`urllib.request.parse_http_list`** ✅ (ported from CPython, RFC 2616 §4.2/§14.45); and **a
  fourth real concurrency bug, this time in the test suite itself** ✅ — two `asyncio.run`-driving
  test classes (`AsyncioAdditionsTests`, `AsgiServerSampleTests`) were missing the
  `[Collection("asyncio-run")]` tag every other such class carries, racing on
  `PyEventLoop._running`'s deliberately process-wide static; confirmed pre-existing (not introduced
  by this round) via an isolated baseline-commit worktree that failed 13/15 runs under the same
  load; fixed by tagging both, confirmed via 36 consecutive clean full-suite runs afterward.
  **`import httpx` now succeeds** too: real `http.cookiejar` ✅ (`Cookie`/`CookieJar`, RFC
  6265-style domain/path matching, a real Set-Cookie parser); real `urllib.request.Request` ✅
  (scoped to what `CookieJar` itself drives); real `zlib` ✅ (compress/decompress/decompressobj,
  backed by .NET's own compression streams — the same GetCharCount+GetChars-vs-Convert incremental-
  state bug class as `codecs` caught again, fixed again); real enum tuple-value member unpacking ✅
  via a class-defined `__new__` (plus a new `int.__new__(cls, value)`, found via
  `httpx._status_codes.codes(IntEnum)`); real `typing.TypedDict` subclass construction ✅ (returns a
  plain dict — real runtime erasure) and real functional-syntax `typing.NamedTuple` ✅
  (`NamedTuple("Name", [...])`, not just the class-based form); `urllib.parse.parse_qs` ✅; real
  `bisect` ✅ (CPython's own algorithm, ported); real `unicodedata` ✅ (category/normalize fully
  real via .NET's own Unicode Character Database; combining/bidirectional/name honestly scoped to
  ASCII — verified this doesn't break real idna's own bidi validation for ASCII-only hostnames);
  real `netrc` ✅; a builtin function can now carry arbitrary extra attributes ✅
  (`PyBuiltinFunction.Attributes`, matching real Python-level functions); `sys.maxunicode` ✅. Plus
  3 PyPI installs (`idna`, `sniffio`, `rfc3986`) and a real version-pin fix
  (`httpx==0.23.3`, since `starlette==0.27.0`'s `TestClient` needs the `Client(app=...)`
  convenience param modern httpx removed). **The astral-regex wall is now solved for real** ✅: a
  non-negated character class containing an astral (>U+FFFF) Unicode range is rewritten into an
  alternation of the standard UTF-16 surrogate-pair sub-range fragments before reaching .NET's regex
  engine — the same technique JS's own Unicode-aware (`u`-flag) regex mode uses — handling the
  general multi-high-surrogate decomposition, not just a narrow special case; verified against 16
  hand-derived cases (range boundaries, gaps, quantifiers, `findall`). Also: a real, callable
  `.__hash__` on every function/builtin ✅ (`fn.__hash__()` previously raised `AttributeError` even
  though `hash(fn)` already worked); real `atexit` ✅ (callbacks actually run at script end, in
  reverse order, scoped per engine instance); real `importlib.resources` ✅ (`files()`/`as_file()`,
  a real `pathlib.Path`); real `logging.addLevelName`/`getLevelName` ✅. Plus 3 more PyPI installs
  (`certifi`, `httpcore`, `h11`). **`import httpx` succeeds**, and building a real `TestClient`
  against a real `FastAPI()` app now gets deep into `httpcore`/`h11`'s own import chain. **New
  frontier**: real `h11` compiles regexes from **bytes** patterns (`re.compile(rb"...")`) — real
  CPython's `re` supports both `str` and `bytes`, but PySharp's `re.compile()` only ever accepted
  `str`. A separate, substantial feature (every `re` entry point needs bytes-vs-str mode awareness),
  not started this round.
- Tests: **963 green** (up from 547 — pydantic v1 + starlette/anyio + match/case probe-driven work
  across `FASTAPI_PLAN.md`, plus aiomqtt/other work in between).

_Update these numbers at every milestone._

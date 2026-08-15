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
| 2 | **FastAPI API** (no SQL) | [http_api.py](samples/http_api.py) · [async_api.py](samples/async_api.py) · [fastapi_demo.py](samples/fastapi_demo.py) | ✅ **Done** (2.0/2.0+/2a/2b/2c/2d/2e all ✅ — a real, unmodified FastAPI app, full CRUD + WebSockets + graceful shutdown, served live over real HTTP entirely by PySharp) | ~~`async`/`await` (core)~~ ✅, ~~`asyncio`~~ ✅, ~~`re`/`datetime`/`inspect`/real `typing`~~ ✅, ~~`contextlib`~~ ✅, ~~`abc`~~ ✅, pydantic (import + BaseModel + real `__slots__` + validators/constraints/Config ✅, 30-pattern robustness sweep ✅, not full API parity by design), ~~ASGI~~ ✅ (real FastAPI app, full CRUD, WebSockets, graceful shutdown, live over curl) |
| 3 | **SQL access** (SQLite, Postgres, SQL Server) | [samples/sqlite_demo.py](samples/sqlite_demo.py) · [samples/pyodbc_demo.py](samples/pyodbc_demo.py) | 🟡 In progress (3a ✅ sqlite3, 3c ✅ SQL Server, 3b ⚪ blocked — no Postgres server) | `sqlite3` (C# shim on `Microsoft.Data.Sqlite`) ✅; `pyodbc` (C# shim on `Microsoft.Data.SqlClient`, verified against a real SQL Server LocalDB) ✅; Postgres (`Npgsql`) blocked on server availability — see SQL_PLAN.md |
| 4 | **HTTP client** (requests-like) | [samples/requests_demo.py](samples/requests_demo.py) | ✅ **Done** | real `http.client` (subclassable HTTPConnection/HTTPSConnection/HTTPResponse) ✅ — the real, unmodified `requests` package runs live over real HTTPS (GET/POST/redirects/sessions/cookies), see HTTP_PLAN.md |
| 5 | **MQTT subscribe on a broker** (client) | [mqtt_subscribe.py](samples/mqtt_subscribe.py) | ✅ **Done** | *none* — paho's subscribe side already ran; real round-trip on test.mosquitto.org |
| 6 | **MQTT broker** (server) | [samples/mqtt_broker_demo.py](samples/mqtt_broker_demo.py) | ✅ **Done** | a real, hand-rolled MQTT 3.1.1 broker on this project's own `socket`/`asyncio`/`struct`/`threading` — no interpreter changes needed, every primitive it exercises was already solid |
| 7 | **AMQP / RabbitMQ** | [samples/amqp_broker_demo.py](samples/amqp_broker_demo.py) | ✅ **Done** | real, unmodified `pika` (PyPI) driving a hand-rolled real AMQP 0-9-1 broker — no RabbitMQ server/Docker available, so both sides run locally, matching scenario 6's own strategy; 8 real gaps found and fixed along the way (`ast`, `numbers`, `heapq` — 3 new modules — plus real fixes to `ABCMeta`, `defaultdict`, `bytes.split()`, `OSError.errno`/`.strerror`, `select.select()`/`getsockopt()`) |
| 8 | **File system API** | [samples/filesystem_demo.py](samples/filesystem_demo.py) | ✅ **Done** | new C# **`glob`**/**`shutil`** modules; `os`/`os.path` fspath (`__fspath__`) coercion for every path-taking function; `pathlib.Path.glob`/`rglob`/`iterdir`/`relative_to`/ordering — see FILESYSTEM_PLAN.md |
| 9 | **JSON + YAML (de)serialization** | [config_yaml.py](samples/config_yaml.py) | ✅ **Done** | new C# **`yaml`** module (safe_load/safe_dump, PyYAML subset); `json` already present |
| 10 | **Django** | _to be created_ | ⚪ Planned | a real, unmodified Django app (Django itself is pure Python — no C extensions in its core, unlike pydantic-core/numpy). Much heavier than scenario 2's FastAPI: WSGI (Django's default; ASGI is opt-in), the ORM (real SQL generation + migrations, heavy metaclass use on `Model`), the template engine, `django.contrib.admin`, class-based views, forms/sessions, `django-admin` management commands, the `settings.py` module-level config pattern |
| 11 | **ASP.NET Core hosting PySharp** | _to be created_ | ⚪ Planned | the *reverse* direction from every other scenario: not PySharp running Python code that implements a web server (scenario 2), but a real ASP.NET Core (Kestrel) host **embedding PySharp as a .NET library**, calling into Python scripts/plugins from C# request handlers — Python as a scripting/plugin layer inside a real production .NET service. Ties directly into the standing TODO ("Extract PySharpLib as a standalone NuGet library") |
| 12 | **Array computing** (numpy shim) | [samples/numpy_demo.py](samples/numpy_demo.py) | ✅ **Done** | a real C# **`numpy`**-shaped shim (not real numpy — a compiled CPython C extension a from-scratch interpreter can't load): `float64`/`int64`/`bool` dtypes with real arithmetic promotion, construction, indexing/slicing as real strided views (Phase 12.1), broadcasting, reductions, ufuncs, shape manipulation, basic linear algebra (`dot`/`matmul`/`@`, `np.linalg.norm`, `trace`/`diagonal`), `np.random`, a two-way .NET array interop bridge — see NUMPY_PLAN.md's full 12-phase plan |
| T | **Native libraries** (cross-cutting) | _per-case_ | 🟡 Partial | `ctypes` now supports scalars, strings, real `Structure`/`byref`/`POINTER`/buffers (verified against real `kernel32` structs/output-pointer APIs — see CTYPES_PLAN.md); `CFUNCTYPE`/callbacks still out of scope; for very rich APIs a dedicated **C# wrapper/shim** is still the fallback |

Legend: ✅ done · 🔴 in progress/next · ⚪ planned · 🟡 partial/close.

Scenarios 4–9, the full backlog collected with the author, are now **all done** (see below): **4**
(HTTP client), **5** (MQTT subscribe), **6** (MQTT broker), **7** (AMQP/RabbitMQ), **8** (File system
API), **9** (JSON+YAML).

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

### Scenario 2 — FastAPI API (no SQL) ✅ — **key scenario**

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
- **2d — data validation (pydantic).** ✅ **Done**, pydantic **v1** chosen (pure Python, no
  `pydantic-core` Rust wall). `import pydantic` succeeds end to end; a `BaseModel` subclass now
  constructs, validates real field types, raises real `ValidationError` on bad input, and serializes
  via `.dict()`. Getting there required building real (simplified) **custom-metaclass support** into
  the interpreter's class-statement execution (`ExecClassDef`) — the first scenario where "custom
  metaclasses are ignored" (a deliberate v1 simplification) actually blocked something, since real
  pydantic's `ModelMetaclass.__new__` must run while `class User(BaseModel): ...` executes to build
  `__config__`/`__fields__`/validators. ~~Known remaining gap: `.dict()` leaks a `__fields_set__`
  key~~ ✅ **Fixed** (`FASTAPI_PLAN.md` 4.1.11): real `__slots__`-backed per-instance storage
  (`PyClass.HasSlot`/`PyInstance.Slots`), separate from an instance's regular attribute dict even when
  `__dict__` is itself a declared slot (pydantic's own `BaseModel.__slots__ = ('__dict__',
  '__fields_set__')` pattern). **A 30-pattern real-world robustness sweep (4.5)** then probed field
  types/validators/Config options well beyond what any one sample app needed — `Optional`/`List`/
  `Dict`/nested/`Enum` fields, `@validator`/`@root_validator`, `conint`/`constr`, real ISO-string
  `datetime` fields, aliases, inheritance, `.copy()`/`.dict(exclude=/include=)`/`.schema()`,
  `Config.extra` — closing 7 real gaps it found (raw `classmethod`/`staticmethod` attribute support,
  their real type names, keyword-argument `date`/`time`/`datetime` construction,
  `time.microsecond` precision, `collections.abc.Set` isinstance support, `dict.fromkeys`,
  `inspect.getdoc`). Full phased log in `FASTAPI_PLAN.md`.
- **2e — ASGI server + FastAPI.** ✅ **Done** (a real, live end-to-end demo — some smaller-scoped
  items remain, see below). `samples/asgi_server.py` (Phase 3.2) is a real, minimal, reusable ASGI/3
  HTTP server over PySharp's own async socket I/O, verified over real HTTP (curl) against both its own
  demo app and a real, unmodified `Starlette` app. A real, unmodified
  `starlette.testclient.TestClient` (backed by real `httpx`/`httpcore`/`h11`) drives a real
  `FastAPI()` app's ASGI callable end to end — GET, POST (with a real pydantic request body, including
  the real 422 validation-error shape), PUT, DELETE, query params, and `HTTPException` all verified
  (`FASTAPI_PLAN.md` Phase 4.1.10–4.1.11). **`samples/fastapi_demo.py`** (Phase 4.2) wires a real
  `FastAPI()` app to the real `asgi_server.py` and was run live as a background process, driven
  entirely with real `curl` over real HTTP/1.1 — every route (full CRUD, typed path/query params,
  pydantic validation, `HTTPException`) matched expected output exactly, zero new bugs.
  ~~Open: WebSocket support in the sample itself~~ ✅ **Done** (Phase 4.3.1/4.3.2/4.3.3):
  `asgi_server.py` now speaks a real RFC 6455 handshake, real frame framing/masking, real
  fragmented-message reassembly, and a real closing handshake — verified live over a real socket both
  with a from-scratch WebSocket client and, separately, through a real `@app.websocket("/ws")` route
  in `fastapi_demo.py` using real starlette's own `WebSocket`/`WebSocketDisconnect`. Zero bugs found
  anywhere in this chain. ~~Still open: a real uvicorn-equivalent process manager (graceful
  shutdown)~~ ✅ **Done** (Phase 4.4): real `signal.signal()` (SIGINT/SIGTERM) backed by .NET's
  `PosixSignalRegistration`, wired into `serve()` for a real accept-then-drain shutdown — verified
  live by hand in a real terminal, plus a real end-to-end drain test over real sockets. Found and
  fixed two genuine event-loop bugs along the way (`asyncio.Event.set()` abandoning a real pending
  waiter behind a stale cancelled one; a cancelled socket op resolving its future twice). Still open:
  hot-reload on file change (a dev-only convenience, not attempted — out of scope for this sample).

Milestone outcome: first (2.0) an HTTP endpoint answering a GET on `localhost` with synchronous
handlers; then (2a–2e) the same reached in **FastAPI** compatibility, all run by PySharp — **scenario
2 is now done**: a real, unmodified FastAPI app (full CRUD, WebSockets, graceful shutdown, real
pydantic validation) served live over real HTTP entirely by PySharp, zero framework code modified.
Full scenario-2 status, including the pydantic v1 probe-driven blow-by-blow (dozens of real
interpreter/stdlib gaps found and fixed round by round, most of them general-purpose fixes rather
than FastAPI-specific ones), lives in `FASTAPI_PLAN.md`. Small, separately-scoped items intentionally
left open (not blocking "done"):
`except*` syntax (PEP 654 exception groups), a nested-import `locals()`/`globals()` scoping edge
case, hot-reload-on-file-change for `asgi_server.py` (a dev-only convenience).

### Scenario 3 — SQL access ✅

Full blow-by-blow lives in [SQL_PLAN.md](SQL_PLAN.md) — this entry is kept in sync at each
checkpoint.

- **3a — `sqlite3` DB-API ✅.** [Sqlite3Module.cs](src/PySharpLib/Modules/Sqlite3Module.cs) —
  `connect`/`Connection`/`Cursor`, `execute`/`executemany`/`executescript`, `fetchone`/`fetchmany`/
  `fetchall`, real transactions (implicit BEGIN/COMMIT matching CPython's own legacy transaction
  control, `with conn:`), `row_factory`/`sqlite3.Row`, the real PEP 249 exception hierarchy — backed
  by `Microsoft.Data.Sqlite`, added directly to `PySharpLib.csproj`. Verified live via
  [samples/sqlite_demo.py](samples/sqlite_demo.py); 13 tests in
  [Sqlite3Tests.cs](src/PySharp.Tests/M6_Stdlib/Sqlite3Tests.cs).
- **3b — Postgres ✅.** [Psycopg2Module.cs](src/PySharpLib/Modules/Psycopg2Module.cs), registered as
  `psycopg2` — same DB-API shape, backed by `Npgsql`. Unblocked 2026-08-15 (a real Azure Database
  for PostgreSQL instance); real `%s`→`$N` placeholder rewriting, psycopg2's own autocommit=False
  transaction model (Postgres has fully transactional DDL, unlike sqlite3's DDL-vs-DML heuristic), a
  faithful always-`0` `.lastrowid` (real psycopg2 never implements it — `RETURNING` + `fetchone()`
  is the real idiom), and a real SQLSTATE-class-based exception mapping. Verified live via
  [samples/postgres_demo.py](samples/postgres_demo.py); 9 tests (skippable, credential-gated) in
  [Psycopg2Tests.cs](src/PySharp.Tests/M6_Stdlib/Psycopg2Tests.cs). See SQL_PLAN.md Phase 2 for the
  full list of real gaps found and fixed (verified along the way against real psycopg2 itself, not
  just the driver).
- **3c — SQL Server ✅.** [PyodbcModule.cs](src/PySharpLib/Modules/PyodbcModule.cs), registered as
  `pyodbc` — `connect`/`Connection`/`Cursor`, real `pyodbc.Row` (tuple-*and*-attribute access),
  pyodbc's own autocommit=False transaction model (deliberately different from sqlite3's DDL-vs-DML
  heuristic — confirmed live), a real `lastrowid` via a combined-batch `SCOPE_IDENTITY()` (its own
  cross-batch scoping quirk found and worked around live), real `date`/`time`/`datetime`
  round-tripping — backed by `Microsoft.Data.SqlClient`, verified live against a real SQL Server
  LocalDB instance (`MSSQLLocalDB`, already provisioned on this machine). Verified live via
  [samples/pyodbc_demo.py](samples/pyodbc_demo.py); 11 tests in
  [PyodbcTests.cs](src/PySharp.Tests/M6_Stdlib/PyodbcTests.cs) (skip, not fail, on a machine with no
  LocalDB — see `SqlServerLocalDbFixture.cs`).

### Scenario 4 — HTTP client ✅

Full blow-by-blow lives in [HTTP_PLAN.md](HTTP_PLAN.md). The real, unmodified `requests` package
(→ `urllib3` → this project's own `http.client`) runs live: `GET`/`POST` with query params and real
JSON bodies, a `Session` following real redirects and persisting real cookies, and a caught
`requests.exceptions.HTTPError` from `raise_for_status()` — all verified against a real public
server (`httpbin.org`). urllib3's own `HTTPConnection`/`HTTPSConnection` genuinely subclass
`http.client`'s and drive it via `super()`, so a lightweight requests-shaped shim wouldn't have
worked — a real `http.client.HTTPConnection`/`HTTPSConnection`/`HTTPResponse` was built instead
([HttpClientModule.cs](src/PySharpLib/Modules/HttpClientModule.cs)), reusing the existing real
`socket`/`ssl` modules for all I/O rather than a separate raw-socket layer. Along the way, 25 real
gaps were found and fixed — about half genuinely new stdlib modules/functions (`email.errors`,
`zipfile`, `calendar.timegm`/`time.strptime`, `encodings`/`encodings.aliases`/`encodings.idna`,
`random`, `importlib.metadata`, `IOError`/`EnvironmentError`, ...), the other half real,
general-purpose interpreter bugs unrelated to HTTP specifically and never exercised by any prior
scenario (`str(bytes, encoding, errors)`'s decode overload, `hasattr(x, "__iter__")` on builtin
containers, `typing.Protocol`/`@runtime_checkable` structural `isinstance()`,
`collections.abc.Mapping`/`MutableMapping`'s missing `update`/`items`/`keys`/`values` mixins, a
`class Foo(typing.NamedTuple):` construction bug, a missing module-level `__doc__` default, a
missing `__import__` builtin). Verified live via
[samples/requests_demo.py](samples/requests_demo.py); 9 tests (against a real local TCP server, no
external dependency) in [HttpClientTests.cs](src/PySharp.Tests/M18_Http/HttpClientTests.cs).

### Scenario 5 — MQTT subscribe on a broker ✅

The script [samples/mqtt_subscribe.py](samples/mqtt_subscribe.py) performs a **real MQTT round-trip**:
it connects to a public broker (`test.mosquitto.org:1883`, plaintext), subscribes to a unique topic,
publishes 3 JSON messages there and receives them back via `on_message`. **No interpreter changes**:
paho's subscribe side already ran since scenario 1 (which used `client.subscribe`). It confirms the
MQTT/network engine is solid outside the IoT Hub case too. Prerequisite: `pysharp install paho-mqtt`.

### Scenario 6 — MQTT broker (server) ✅

The script [samples/mqtt_broker_demo.py](samples/mqtt_broker_demo.py) is a real, hand-rolled MQTT
3.1.1 broker — unlike scenarios 1/1b/5 (a real, unmodified paho-mqtt/aiomqtt *client* talking to
somebody else's broker), this is the *server* side. Built directly on this project's own
`socket`/`asyncio`/`struct`/`threading` — the same async-socket-server pattern already proven by
`samples/asgi_server.py` (scenario 2's `loop.sock_accept`/`sock_recv`/`sock_sendall` on a
non-blocking socket), applied to the MQTT wire protocol instead of HTTP: real fixed-header/
remaining-length variable-int parsing, real CONNECT/CONNACK/SUBSCRIBE/SUBACK/PUBLISH/PUBACK/
PINGREQ/PINGRESP/UNSUBSCRIBE/UNSUBACK framing, real `+`/`#` topic-filter wildcard matching, real
fan-out to every currently-connected matching subscriber. The broker runs in a background thread
(its own independent `asyncio.run()` event loop — the same thread-isolation semantics
FASTAPI_PLAN.md Phase 4.2 already established); two **real, unmodified** `paho.mqtt.client`
instances (the same PyPI package already verified against a real Azure IoT Hub and a real public
broker in scenarios 1/5) connect to it over a real loopback TCP socket and exchange 3 messages —
a genuine wire-protocol round trip, not an in-process shortcut. **Zero interpreter changes were
needed**: every primitive this exercises (`socket`, `asyncio`'s thread-isolated event loop,
`struct`, `threading.Thread`/`Lock`) was already solid from prior scenarios — the only bug found
was in the *demo script itself* (a startup race: publishing before the broker had processed the
subscriber's SUBSCRIBE, fixed by waiting for a real SUBACK via `on_subscribe` before publishing,
the same discipline any real MQTT client needs against any real broker). 4 new tests (including a
full real pub/sub round trip with no external network dependency, since both the broker and the
clients are local) in
[MqttBrokerSampleTests.cs](src/PySharp.Tests/M20_MqttBroker/MqttBrokerSampleTests.cs).

### Scenario 7 — AMQP / RabbitMQ ✅

Full blow-by-blow lives in [AMQP_BROKER_PLAN.md](AMQP_BROKER_PLAN.md). The script
[samples/amqp_broker_demo.py](samples/amqp_broker_demo.py): no real, publicly reachable
AMQP test broker exists the way `test.mosquitto.org` does for MQTT, and no Docker/local RabbitMQ
instance was available — so, following the exact same strategy scenario 6 used for MQTT, the
*server* side is a real, hand-rolled AMQP 0-9-1 broker on this project's own
`socket`/`asyncio`/`struct`/`threading`, and a **real, unmodified `pika`** (PyPI's pure-Python AMQP
0-9-1 client) drives it over a real loopback TCP socket: the real 8-byte protocol header, real
Connection.Start/Start-Ok/Tune/Tune-Ok/Open/Open-Ok negotiation, real Channel.Open, real
Queue.Declare, real Basic.Consume/Cancel, real Basic.Publish (method frame + content-header frame +
content-body frame(s)), real Basic.Deliver fan-out to a registered consumer, and a real
Channel.Close/Connection.Close shutdown handshake.

Unlike scenario 6, this one needed real interpreter/stdlib work — 8 distinct gaps, found in the
usual "run it, see what breaks" order:

- **Two new, real (if scoped) C# modules real pika imports unconditionally at load time**: `ast`
  (`literal_eval`, a genuine recursive-descent walk of this project's own parser output — never
  actually invoking anything, so a non-literal expression correctly raises `ValueError`, not runs)
  and `numbers` (the real ABC numeric tower — `int`/`bool`/`float` recognized against
  `Integral`/`Real`/etc. via the same duck-typed-ABC mechanism `collections.abc.Iterable`/`Set`
  already use).
- **A third new module, `heapq`** (`heappush`/`heappop`/`heapify`/`heappushpop`/`heapreplace`, a
  direct port of real CPython's own sift-up/sift-down algorithm using this project's own
  `interp.Compare` so heap elements can be arbitrary `__lt__`-comparable objects) — pika's own
  `select_connection.py` keeps its connection-timeout queue in one.
- **`ABCMeta(name, bases, namespace)`** (the classic dynamic-class-creation call, real because
  `ABCMeta` genuinely *is* a subclass of `type`) raised `TypeError: ABCMeta() takes no arguments` —
  found via real pika's own `compat.py`. Fixed by special-casing it in `Interp.Call` the same way
  the `type(...)` builtin itself already handles its own 3-arg form.
- **`defaultdict` was missing `__delitem__`/`clear`/`pop`/`popitem`/`setdefault`/`update`** — only
  `__getitem__`/`__setitem__`/`keys`/`values`/`items`/`get` existed. Found via real pika's own
  connection-teardown path (`del self._fd_events[...]`).
- **`bytes.split()` (no separator) didn't exist** — only the explicit-separator form did. Real
  CPython's no-arg form splits on runs of ASCII whitespace, discarding empties; found via real
  pika's own `credentials.py` (`as_bytes(start.mechanisms).split()`, splitting the real
  space-separated SASL mechanism list straight off the wire).
- **`OSError`-family exceptions never actually carried `.errno`/`.strerror`/`.filename`** — the
  values were folded into `.args` (or, worse, a single pre-formatted string) but never set as real
  attributes, since `PyErr.MakeInstance` (the fast, generic exception constructor) deliberately
  never runs `__init__`. Found via real pika's own `io_services_utils.py` reading `caught_exc.errno`
  off a real `BlockingIOError` from a non-blocking `connect()` in progress. Fixed with a new
  `PyErr.MakeOSError` helper (used at every real errno-carrying construction site) plus two general
  guarantees any OSError now gets regardless of how it was built: `.errno`/`.strerror`/`.filename`
  default to `None` (matching real CPython), and `OSError.__str__` formats as
  `"[Errno N] strerror: 'filename'"` when a real errno is set, falling back to the ordinary
  args-based formatting otherwise.
- **`select.select()` only recognized socket objects, never raw integer file descriptors** — real
  CPython's `select.select()` accepts either. Found via real pika's own `SelectPoller`, which
  tracks connections purely by `fileno()` int and calls `select.select(fd_list, ...)` directly;
  previously every such call silently saw an empty selectable set and always timed out (surfacing
  as a spurious "TCP connection attempt timed out"). Fixed by resolving a raw fd back to its real
  `Socket` via the same fd registry `fileno()`/asyncio's `add_reader`/`add_writer` already use.
- **`socket.getsockopt()` didn't exist at all** — the classic post-nonblocking-connect
  `getsockopt(SOL_SOCKET, SO_ERROR)` check (how real code learns whether a `connect()` actually
  succeeded once `select()`/`poll()` reports the fd writable) raised `AttributeError`. Fixed via
  .NET's own `Socket.GetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Error)`.
- Also (in the demo script itself, not the interpreter): pika's `BlockingConnection.close()` sends
  a real `Basic.Cancel` for every active consumer before closing the channel, and blocks
  indefinitely waiting for `Basic.Cancel-Ok` — the broker's first cut had no handler for it at all,
  which silently hung `close()` forever. Fixed by adding real `Basic.Cancel`/`Basic.Cancel-Ok`
  handling to the broker.

12 new tests (including a full real publish/subscribe round trip between two real
`pika.BlockingConnection` clients and the hand-rolled broker, all local/no network) in
[AmqpBrokerSampleTests.cs](src/PySharp.Tests/M21_AmqpBroker/AmqpBrokerSampleTests.cs) and
[AmqpInterpreterFixesTests.cs](src/PySharp.Tests/M21_AmqpBroker/AmqpInterpreterFixesTests.cs).

### Scenario 8 — File system API ✅

Full blow-by-blow lives in [FILESYSTEM_PLAN.md](FILESYSTEM_PLAN.md). The script
[samples/filesystem_demo.py](samples/filesystem_demo.py) is a real file-organizer: it builds a real
temp directory tree, walks it (`os.walk`), finds files two different ways (`glob.glob("**/*.py",
recursive=True)` and `pathlib.Path.rglob("*.py")`), packages a release directory (`shutil.copy2`,
`shutil.copytree`), then cleans up and reorganizes it (`shutil.rmtree`, `shutil.move`,
`Path.iterdir()`, `shutil.which`, `shutil.disk_usage`) — all verified against the real filesystem, no
stubs. Two new C# modules were built from scratch, since neither existed before: **`glob`**
([GlobModule.cs](src/PySharpLib/Modules/GlobModule.cs) — a real segment-by-segment directory walk,
`*`/`?`/`[...]` translated to regex, real recursive `**` support) and **`shutil`**
([ShutilModule.cs](src/PySharpLib/Modules/ShutilModule.cs) — thin, real wrappers over
`File.Copy`/`Directory.CreateDirectory`/`Directory.Delete`/`File.Move`/`DriveInfo`). Along the way, a
pervasive, previously-unexercised gap surfaced: **no `os`/`os.path` function coerced a path-like
(`__fspath__`) argument** — every prior scenario had only ever passed plain strings. Fixed with a new
`OsModule.PathArg(interp, o)` helper, now used by every path-taking function in both `os` and
`os.path` after a full-file rewrite. `pathlib.Path` also gained `glob`/`rglob`/`iterdir`/`relative_to`
and real ordering (`__lt__`/`__le__`/`__gt__`/`__ge__`, needed for `sorted()` over a list of `Path`
objects — real pathlib compares by parts tuple, string comparison of the normalized path matches for
every case reachable here). 7 new tests in
[FilesystemTests.cs](src/PySharp.Tests/M19_Filesystem/FilesystemTests.cs).

### Scenario 9 — JSON + YAML (de)serialization ✅

The script [samples/config_yaml.py](samples/config_yaml.py) loads a **YAML** configuration, inspects
it (correct types: int/bool/str/null, lists, nested mappings) and converts it back and forth between
YAML and JSON verifying the round-trip. `json` was already present; a **C# `yaml` module** was added
([YamlModule.cs](src/PySharpLib/Modules/YamlModule.cs)) with `safe_load`/`load`/`safe_dump`/`dump` over
a **practical PyYAML subset**: block mapping/sequence with indentation, flow style (`[..]`/`{..}`),
typed scalars, quoting, comments, the `---` marker. Out of scope for v1: block scalars `|`/`>`,
anchors/aliases, explicit tags, multiple documents. Covered by [YamlTests.cs](src/PySharp.Tests/M6_Stdlib/YamlTests.cs).

### Scenario 12 — Array computing (numpy shim) ✅

The script [samples/numpy_demo.py](samples/numpy_demo.py) runs a realistic numpy session end-to-end:
construction/dtypes, real strided views (a slice or `.T` mutating the source array), broadcasting,
reductions, boolean masking, basic linear algebra, and a seeded `np.random`. `numpy` here is a real
C# **`numpy`-shaped shim** over this repo's own `ndarray` type — real numpy is a compiled CPython C
extension a from-scratch interpreter cannot load, so this is a from-scratch reimplementation of
numpy's *observable behavior* (verified against real numpy's own documented semantics throughout),
not a wrapper around the real library. `ndarray` is a `PyClass` + C# wrap (`NdArrayData`), the same
pattern the `socket` module already used, so arithmetic/indexing/iteration reuse the interpreter's
existing dunder dispatch with only two small core changes: `Interp.CompareExpr` now returns a
comparison's raw dunder result instead of always collapsing to `bool` (so `arr1 < arr2` returns a
real array, not a `bool`), and a `PyOps.PyEquals` bug where `NaN` incorrectly equaled itself via a
reference-identity fast path. See **NUMPY_PLAN.md** for the full 12-phase plan (dtypes/promotion,
indexing, broadcasting, reductions, ufuncs, shape manipulation, linear algebra, interop, real
strided views) and every phase's own verification notes. Covered by
[src/PySharp.Tests/M14_Numpy](src/PySharp.Tests/M14_Numpy/) (120+ tests).

### Scenario 13 — ORM (SQLAlchemy) ✅

The real, unmodified **SQLAlchemy 2.0.51** (`py3-none-any` wheel, no C-extension dependency by
default) runs live against this project's own real `sqlite3` module: `declarative_base()`, a mapped
class, `Base.metadata.create_all(engine)` (real `CREATE TABLE` DDL), `Session.add()`/`.commit()` (a
full real INSERT flush, including the `insertmanyvalues`/RETURNING-clause machinery), and
`session.execute(select(...)).scalars().all()`/`session.get(...)` all produce exactly the expected
real values round-tripped through a real SQLite in-memory database. See **ORM_PLAN.md** for the full
2-phase plan and the ~30 real, general interpreter gaps found and fixed along the way — none
SQLAlchemy-specific: real `class Foo(dict/list/set/str/int): ...` subclassing, `__slots__` descriptor
semantics, PEP 487 `__init_subclass__`, the general descriptor protocol (including on plain functions
themselves, `func.__get__`), real name mangling, metaclass `__init__` dispatch and metaclass-level
operator overloading, `instance.__dict__ = ...` whole-namespace replacement, and a real `abc.ABCMeta`
base for the `type` pseudo-class hierarchy. Verified live via
[src/PySharp.Tests/M22_Orm](src/PySharp.Tests/M22_Orm/). **Postgres 🟡**: driven against a real Azure
Database for PostgreSQL instance via SQLAlchemy's `postgresql+psycopg2://` dialect (reusing this
project's own real `psycopg2` shim, Scenario 3's 3b) — connection, real DDL (`create_all`/`drop_all`,
including a real `has_table()` round trip), and `session.add()`/`.commit()` reaching real INSERT SQL
compilation all verified live; one deep wall remains inside SQLAlchemy 2.0's own `insertmanyvalues`
sentinel/batch-size machinery (a `ZeroDivisionError`, not yet root-caused). The originally-planned
pure-Python `pg8000` dialect was tried first and abandoned — it needs a real module system unification
(constructing a native module object from Python-level `types.ModuleType(...)`) this interpreter
doesn't have yet. See ORM_PLAN.md Phase 3 for the full list of real gaps found and fixed getting this
far (including a genuine concurrency bug in `threading.Condition` and a general zero-arg `super()`
fix).

### Cross-cutting — Native libraries 🟡

General rule: **a native library is invoked from C#**, so it is exposed to Python either (a) via
`ctypes`, or (b) by creating a dedicated **C# wrapper/shim** that presents an idiomatic Python API
over the .NET/native lib. It is the same strategy as scenario 3 (`sqlite3` is a shim on a .NET
driver).

**`ctypes` (see CTYPES_PLAN.md)**: beyond scalar arguments/returns and `char*`/`wchar*` strings
(verified against real `kernel32`/`msvcrt` DLLs), `ctypes` now has real `Structure` (real per-field
storage, real natural-alignment layout — computed automatically, not hand-specified), `byref`,
`POINTER`, and `create_string_buffer`/`create_unicode_buffer`. Every value (scalar or struct) is
backed by a real C# `byte[]`, not `Marshal.AllocHGlobal` — `byref()` pins that same managed array for
one native call, so a native function's writes land directly in it with no separate marshal-back
step. Verified against two real Windows APIs that need structs/output pointers: `GetSystemInfo`
(struct fields checked against well-known fixed OS constants — page size 4096, allocation
granularity 65536, `PROCESSOR_ARCHITECTURE_AMD64` == 9 — not just "didn't crash") and
`GetComputerNameW` (a `byref` `DWORD` + a `create_unicode_buffer` round-trip). Found and fixed a
real static-mutable-`PyClass`-field race along the way: xUnit runs tests in parallel, and a shared
static field reassigned on every `import ctypes` let one test's concurrent module creation silently
overwrite another test's in-flight class identity — fixed by making every ctypes-specific class a
real local variable threaded through as a parameter, never a static field (the same class of bug
`FASTAPI_PLAN.md`'s `GenericAliasModule` races were). **Still out of scope**: `CFUNCTYPE`/callbacks
(native code calling back into Python — a separate, larger chunk needing careful delegate-lifetime
management), by-value struct passing (real x64 ABI register-vs-stack rules), generic `ctype * N`
array syntax.

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
| 2e — starlette + ASGI + real FastAPI | **Dozens more real gaps closed** getting real starlette 1.4.1 + anyio, then real fastapi 0.99.1, running end to end: new stdlib modules `shlex`, `contextvars`, `importlib`(`.util`), `textwrap`, `subprocess` (`Popen` on `System.Diagnostics.Process`), `tempfile`, `queue` (thread-safe, `BlockingCollection`-backed), `secrets`, `concurrent.futures.Future`, `html`; real `match`/`case` structural pattern matching (PEP 634, full parser + interpreter support); real **async generators** (`PyAsyncGenerator`, hybridizing yield- and await-suspension) powering `contextlib.asynccontextmanager` and WebSocket streaming helpers for real; a real **recursion-depth guard** (`RecursionError` at CPython's default 1000, on a real 64MB-stack execution thread); real `__slots__`-backed per-instance storage (`PyClass.HasSlot`/`PyInstance.Slots`) closing pydantic's `__fields_set__` leak; a real `memoryview` builtin; real PEP 604 (`X \| Y`)/PEP 585 (`tuple[int, str]`) runtime support; three real, generically-important concurrency/threading bugs (`Importer.ImportAbsolute` holding its lock across a recursive *execute*, letting a module-level generator deadlock a second thread; two non-thread-safe `Dictionary`s in `GenericAliasModule` corrupting under real parallel test execution, fixed with `ConcurrentDictionary`/`[ThreadStatic]`); real `asyncio.current_task()`/`Task`-is-a-`Future` fixes; real `signal.signal()` (SIGINT/SIGTERM via .NET `PosixSignalRegistration`) for graceful ASGI-server shutdown. Full phased blow-by-blow (every fix with its own regression test, ~120 new tests total across Phases 3–4) in `FASTAPI_PLAN.md` | `Modules/` (several new), `Interpretation/Interp.cs`, `Runtime/Async.cs` (`PyAsyncGenerator`), `Runtime/PyClass.cs`/`PyInstance.cs` (slots), `Parsing/Parser.cs`+`Ast.cs` (`match`/`case`); samples [asgi_server.py](samples/asgi_server.py), [fastapi_demo.py](samples/fastapi_demo.py); tests `M16_FastApi`; full log in `FASTAPI_PLAN.md` |

With `co_varnames` (names) + `__annotations__` (types, including `'return'`) the **signature is
complete**: the framework injects every parameter, treating unannotated ones as `str` (like FastAPI)
— superseded in practice once real FastAPI/pydantic took over parameter injection/validation for the
actual scenario-2 target app (2e), though `http_api.py`'s own hand-rolled injector still works as
documented for the pre-FastAPI "walking skeleton" stage (2.0/2.0+).

**Scenario 2 is now fully done.** The handful of items noted along the way as intentionally
out-of-scope: `except*` syntax (PEP 654 exception groups — `BaseExceptionGroup`/`ExceptionGroup` exist
as real types, but the `except*` syntax itself isn't parsed), a nested-import `locals()`/`globals()`
scoping edge case, and hot-reload-on-file-change for `asgi_server.py` (a dev-only convenience). None
of these blocked the real, live, end-to-end FastAPI milestone.

---

## Distance from CPython (gap analysis)

Four independent axes. Compatibility with "any PyPI package" would require closing almost all of them
— which is why the goal stays *per-scenario*, not universal.

### Axis A — Language

| Supported | Missing (out of scope for v1) |
|---|---|
| arbitrary ints, floats, str/bytes, list/tuple/dict/set + comprehensions, f-strings, functions (defaults/`*args`/`**kwargs`/kw-only/decorators/closures/`global`/`nonlocal`), classes (C3 MRO, `super`, dunders, property, static/classmethod), exceptions, `with`, generators (`yield`/`yield from`), **`async`/`await`/`async for`/`async with` (coroutines)**, **real async generators** (`async def` with `yield` — a hybrid `PyAsyncGenerator` combining generator-style yield-suspension with coroutine-style await-suspension on one dedicated thread; real `__aiter__`/`__anext__`/`athrow`-driven `StopAsyncIteration`, and `contextlib.asynccontextmanager` can now actually be *entered*, not just defined), import system, function introspection (`__annotations__`, `__code__`), complex numbers (`complex`, not the `1j` literal), **custom metaclasses** (real, simplified — `class X(Y, metaclass=M)` calls `M.__new__`; no multi-metaclass conflict resolution, no metaclass `__init__` dispatch), **`match`/`case` structural pattern matching** (PEP 634 — real soft-keyword parsing + full pattern semantics: literal/capture/wildcard/value/sequence/mapping/class/or/as patterns, guards), real `object.__eq__`/`__ne__`/`__hash__`/`__repr__`/`__str__` default dunders (directly/unbound-accessible, not just hardcoded fallbacks), **real recursion-depth guard** (runaway recursion raises `RecursionError`, matching CPython's default limit, instead of crashing the process), real `memoryview` (bytearray-backed views share real underlying storage), `isinstance`/`issubclass` accepting a real union type (`X \| Y`) as the 2nd argument | `exec()`/`eval()`, `1j` complex literal syntax, exception groups (`except*`), `generator.send(v)` with a value, dunders as attributes of builtin *types*, real `__slots__` (separate per-slot storage — every instance attribute lives in the same dict today, slotted or not), real `class X(dict):` subclass storage (instances of a `dict` subclass aren't backed by real dict storage unless the subclass defines its own `__getitem__`/`__setitem__`) |

### Axis B — Stdlib

Implemented **~75 modules** against CPython's **~200** (plus `pyodbc`, a real PyPI package — not
stdlib — given the same native-C#-shim treatment as `yaml`). Present today: `sys`, `os`, `os.path`, `glob`, `shutil`, `time`, `platform`,
`errno`, `io` (incl. `TextIOWrapper`), `warnings`, `copy`, `socket`, `ssl`, `select`, `threading`, `asyncio` (incl. real `Runner`/`Task`/protocols hierarchy), `struct`, `hashlib`,
`hmac`, `base64`, `string`, `urllib(.parse/.request)`, `uuid`, `json`, `yaml`, `collections`
(`Counter`/`ChainMap`/`deque`), `collections.abc`, `enum`, `functools`, `math`, `logging`, `ctypes`,
`re` (real regex engine, incl. `pos`/`endpos` and `Match.groups(default)` positionally), `datetime`, `ipaddress`, `pathlib`, `weakref`, `pickle`, `colorsys`,
`decimal`, `itertools`, `operator`, `types`, `abc`, `contextlib` (incl. `asynccontextmanager` at
decoration time), `inspect` (incl. a real `isfunction` fix — async/generator functions were
previously misclassified — and real coroutine-state constants/`getcoroutinestate`), `shlex`, `contextvars`, `importlib`, `importlib.metadata`, `textwrap`, `signal`,
`concurrent.futures`, `stat`, `subprocess`, `tempfile`, `http`, `http.client`, `http.cookies`,
`http.cookiejar`, `email.utils`, `email.message`, `email.errors`, `encodings` (`.aliases`/`.idna`),
`html`, `traceback`, `mimetypes`, `secrets`, `array`, `queue`, `sqlite3`, `zipfile`, `calendar`,
`random`, `pyodbc`, `ast` (`literal_eval`), `numbers`, `heapq`; real (not stub) `typing` (incl.
`Protocol`/`@runtime_checkable` structural `isinstance()`) and `dataclasses`; stub `__future__`.

**High-priority missing**: a Postgres DB-API module (scenario 3b, blocked on server availability).

### Axis C — Native extensions (C/Rust)

The structural wall. Packages like numpy, pandas, psycopg2, cryptography, orjson, **pydantic-core** are
binaries compiled for CPython: **no Python-in-C# interpreter can load them as they are**. Possible
strategies, all *per-package*:

1. use a **pure-Python fallback** if the package offers one (often not);
2. **reimplement the API in C#** as a native module/shim (via `Microsoft.Data.Sqlite`, `Npgsql`,
   `NativeLibrary`, …);
3. `ctypes` for calls to native DLLs with simple signatures.

There is no *generic* path without embedding CPython — which the project chose not to do.

**numpy** — the hardest single case, tackled with a dedicated phased plan: see
[NUMPY_PLAN.md](NUMPY_PLAN.md) — ✅ **all 12 phases done** (scenario 12). A C# `numpy`-shaped shim
(`ndarray` as a `PyClass` + C# wrap, exactly like the `socket` module, so arithmetic/indexing/
iteration reuse the interpreter's existing dunder dispatch with no core changes): construction,
`float64`/`int64`/`bool` dtypes with real promotion, indexing/slicing as real strided views
(Phase 12.1 — basic indexing/`.T`/`reshape`/`ravel`/`expand_dims`/`squeeze` share the source buffer),
broadcasting, reductions, ufuncs, shape manipulation, basic linear algebra, `np.random`, and a
two-way .NET array interop bridge.

### Axis D — Packaging / pip

The mini-pip installs **only pure wheels** (`py3-none-any`) and **does not resolve dependencies**
(`requires_dist` ignored). A bounded, low-risk improvement: read the transitive dependencies and
install them (with marker parsing), still rejecting non-pure wheels but with a **clean error** (today
`install numpy` exits with an unhandled CLR exception — see [TODO.md](TODO.md)).

---

## Developer experience & documentation roadmap

Not a "distance from CPython" gap — new capabilities around the interpreter, not language/stdlib
compatibility itself.

### VSCode debugger

**Status: ⚪ planned, not started.** Turn `pysharp run` into a first-class debuggable target from
the editor (breakpoints, step over/into/out, call stack, variable inspection, watch expressions,
a debug-console REPL) instead of only a console host.

- **Real substrate already exists**: the interpreter already tracks a genuine per-exception call
  stack (`PyRaise.Traceback`, a `List<PyFrameInfo>` — file/line/function + the real scope for
  variable inspection, already consumed by `traceback.format_exc()`/`sys.exc_info()`) and
  `Interp.InnermostFrame` for live frame introspection. This is exactly the substrate a Debug
  Adapter Protocol (DAP) server needs for `stackTrace`/`scopes`/`variables` requests — not starting
  from zero.
- **The real engineering lift**: today the eval loop runs straight through to completion or an
  unhandled exception — it never actually *pauses* mid-execution. Real breakpoint support means
  teaching `Interp`'s statement-execution loop to check a breakpoint set and suspend (blocking the
  interpreter thread until a `continue`/`next`/`stepIn`/`stepOut` command arrives from the DAP
  client) — that's the hard part, not the protocol plumbing around it.
- **Scope**: a small dedicated project (e.g. `PySharp.DebugAdapter`) implementing at minimum
  `launch`/`attach`, `setBreakpoints`, `continue`/`next`/`stepIn`/`stepOut`, `stackTrace`, `scopes`,
  `variables`, `evaluate`; a minimal VSCode debugger contribution (`.vscode/launch.json` support, or
  a small extension) pointing at it.

### "Python embedding for C# devs" — a short course

**Status: ⚪ planned, not started.** A doc (or short series) teaching C# developers what "loading a
real CPython extension" actually requires — directly prompted by this session's own live Q&A on why
`numpy`'s `.pyd`/`.so` can't just be P/Invoked into like a normal native DLL.

- **Content outline**: `.pyd`/`.so` binary anatomy (PE/COFF vs ELF, the `PyInit_<module>` entry
  point convention, ABI-tagged filenames); the CPython C-API surface a real embedder needs
  (`PyObject`'s fixed memory layout, reference counting, `tp_*` type slots, the GIL,
  `Py_Initialize`); why "P/Invoke into a `.pyd`" is not a shortcut around embedding CPython — it
  *is* embedding CPython, just through the back door; the contrast with a **plain-C-ABI** native
  library (no embedded Python object model — e.g. SQLite, OpenBLAS/LAPACK), which real P/Invoke
  genuinely does handle, and which is exactly this project's own Axis C strategy
  (`Microsoft.Data.Sqlite`/`Npgsql`-style shims, and the `numpy.linalg` plan in
  [NUMPY_PLAN.md](NUMPY_PLAN.md) Phase 10).
  Why this project deliberately does **not** embed CPython (see Axis C above: "no *generic* path
  without embedding CPython — which the project chose not to do") — this doc explains and justifies
  that foundational decision, it doesn't revisit it.
- **Where it lives**: a new top-level doc (e.g. `EMBEDDING.md`), cross-referenced from Axis C above
  and from [NUMPY_PLAN.md](NUMPY_PLAN.md)'s own linalg section.

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
  already-pinned `httpx==0.23.3`. **`import httpx` succeeds**, and real bytes-pattern `re` support ✅
  (`re.compile(rb"...")` — real CPython's `re` matches `bytes` as well as `str`; every entry point
  now threads a bytes-vs-str mode through, backed by a lossless Latin-1 byte↔codepoint mapping since
  .NET's `Regex` only operates on `string`) got `TestClient` construction all the way to actually
  issuing a request. Along the way: real `isinstance()` recognition of `dict` as `Mapping` plus
  structural duck-typing for `Iterable`/`Iterator`/`Container`/`Sized`/`Callable`/`Hashable` (a real,
  silent correctness bug beyond the immediate crash — real httpx's `Headers.__init__` branches on
  `isinstance(headers, Mapping)` to pick `.items()` vs. iterating bare keys); real
  `MutableMapping.pop`/`popitem`/`setdefault`/`clear` mixins, unified by identity between
  `collections.abc` and `typing` (previously two unrelated bare placeholders); real
  `namedtuple._replace`, with the two separate, drifted namedtuple implementations
  (`typing.NamedTuple`/class-based vs. `collections.namedtuple`) finally unified onto one generator;
  `pathlib.Path.expanduser()`; `asyncio.Task.get_name`/`set_name`; `bytes.count()`; `parse_qs`/
  `parse_qsl` coercing `None` to `''` (matching real CPython). **The event-loop architecture wall is
  now fixed for real** ✅: `PyEventLoop._running` is `[ThreadStatic]`, explicitly re-adopted into
  every coroutine/generator/async-generator's own dedicated internal thread via a new
  `PyEventLoop.AdoptRunning` (mirroring `LogicalThread.Adopt`'s existing propagation exactly) — but
  deliberately *not* propagated into a genuine `threading.Thread.start()`, so a real independently-
  started thread (like anyio's own `start_blocking_portal`) correctly gets its own independent loop
  scope instead of corrupting the caller's. Verified against both the simple nested-`asyncio.run`
  case and, since that alone wasn't enough, the *exact* cross-thread dispatch primitive real anyio's
  own `run_sync_from_thread` uses (`asyncio.get_running_loop()` out of a background thread via a
  `concurrent.futures.Future`, then `loop.call_soon_threadsafe(...)` into that still-suspended loop)
  — confirmed via 25 consecutive clean full-suite runs (more than the usual 15, given how deep and
  sensitive this specific change is). **New frontier — substantially larger in scope than anything
  else in this whole chain**: past the event-loop fix, a real `TestClient` request now hangs inside
  anyio's own `TaskGroup.start_soon`, which reads/writes **private, undocumented `asyncio.Task`
  attributes** (`_must_cancel` and likely others) for precise cancellation semantics — a
  fundamentally different kind of gap than everything else found so far: every previous fix targeted
  real CPython's documented public behavior; this needs replicating a slice of CPython's actual
  C-level `Task` state machine, not started this round. Phase 4.2 (a real target sample app) not
  started. 6/7/8 to do; native cross-cutting partial.
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
  against a real `FastAPI()` app now gets deep into `httpcore`/`h11`'s own import chain. Then: real
  **bytes-pattern `re`** ✅ (`re.compile(rb"...")` — every entry point now threads bytes-vs-str mode
  through, backed by a lossless Latin-1 mapping) got a real `TestClient` request nearly all the way
  through. Also found and fixed along the way: real `isinstance(dict, Mapping)` plus structural
  `Iterable`/`Iterator`/`Container`/`Sized`/`Callable`/`Hashable` duck-typing ✅ (a real silent
  correctness bug, not just a crash — real httpx's `Headers.__init__` picks the wrong branch without
  it); real `MutableMapping.pop`/`popitem`/`setdefault`/`clear` ✅, unified between `collections.abc`
  and `typing`; real `namedtuple._replace` ✅, with the two drifted namedtuple implementations
  unified onto one generator; `pathlib.Path.expanduser()` ✅; `asyncio.Task.get_name`/`set_name` ✅;
  `bytes.count()` ✅; `parse_qs`/`parse_qsl` None-coercion ✅. **The event-loop architecture wall
  fixed for real** ✅: `PyEventLoop._running` is now `[ThreadStatic]`, propagated into every
  coroutine/generator's own dedicated thread the same way `LogicalThread` already is, but *not* into
  a genuine `threading.Thread.start()` — so anyio's real `start_blocking_portal` (a real independent
  thread running its own `asyncio.run()` loop) no longer corrupts the caller's loop. Verified against
  real anyio's own cross-thread dispatch primitive (`asyncio.get_running_loop()` out of a background
  thread + `loop.call_soon_threadsafe(...)` into it), confirmed via 25 consecutive clean full-suite
  runs. **New frontier — substantially larger in scope**: past that fix, a real `TestClient` request
  now hangs inside anyio's own `TaskGroup.start_soon`, which reads/writes private, undocumented
  `asyncio.Task` attributes (`_must_cancel` and likely others) for cancellation semantics — needs
  replicating a slice of CPython's actual `Task` internals, not just its public API; a fundamentally
  different, more open-ended kind of gap than anything else found in this chain. Not started this
  round.
- **The `Task`-internals wall closed for real, and the milestone reached**: real
  `_must_cancel`/`_fut_waiter`/`Task.cancel()`/`get_coro()` ✅ (including a real correctness bug caught
  during verification — `t.cancelled()` incorrectly stayed `False` after a real cancellation, now
  fixed to match CPython's own `Task.__step`); real PEP 654 `BaseExceptionGroup`/`ExceptionGroup` ✅
  (`except*` syntax deliberately out of scope — anyio itself never uses it); real
  `Coroutine`/`Generator`/`Awaitable` ABC duck-typing for `isinstance()` ✅, extending 2.2.1's own
  `Mapping`/`Iterable` mechanism (found underneath what first looked like a cosmetic exception-display
  bug); `loop.get_task_factory()`/`set_task_factory()` ✅. Then, past a real anyio `TaskGroup` working
  end to end, ~5 more real gaps stood between that and an actual HTTP response: real `io.BytesIO`
  position tracking (`seek`/`read`/`truncate`) ✅; the `dict()`/`dict.update()` `keys()`-mapping
  protocol ✅ (any object with a `keys()` method, not just a literal `dict`); real `generator.send
  (value)` semantics ✅ (previously any non-`None` value was unconditionally rejected); a generator's
  `return value` actually reaching `StopIteration.value` ✅ (a real, previously-silent bug — the return
  value was being discarded entirely, not just missing an attribute); `email.message.
  Message.get_content_charset` ✅; `codecs.BOM_*` constants ✅. **`TestClient(app).get(...)` now
  round-trips a real request through the full real fastapi/starlette/pydantic/httpx/httpcore/h11/anyio
  stack, correct status code and JSON body.** Full blow-by-blow in `FASTAPI_PLAN.md` Phase 4.1.10.
- **POST/PUT/DELETE/query-params/`HTTPException` verified against real `TestClient`, zero new bugs —
  then real `__slots__`-backed storage closed the last documented Phase-2 gap.** A pydantic request
  body validates and serializes correctly (including the real 422 error shape), `HTTPException`
  round-trips status/detail, query params parse correctly. The one real gap found — a route returning
  a `BaseModel` directly leaked `__fields_set__` into the JSON — is now fixed: `__slots__`-declared
  attributes (`PyClass.HasSlot`/`PyInstance.Slots`) get real per-instance storage separate from the
  regular attribute dict, even when `__dict__` is itself a declared slot (pydantic's own
  `BaseModel.__slots__ = ('__dict__', '__fields_set__')` pattern), recognized in all four real
  `__slots__` shapes (string/tuple/list/set — pydantic's own metaclass computes a set). A
  `copy.copy`/`copy.deepcopy` regression this surfaced (only ever copied the regular dict, silently
  emptying any slots-only object like pydantic's real `FieldInfo`) was caught and fixed before it
  shipped. Full blow-by-blow in `FASTAPI_PLAN.md` Phase 4.1.11.
- **Phase 4.2 done: a real FastAPI app runs live, over real HTTP, entirely on PySharp.**
  `samples/fastapi_demo.py` wires a real, unmodified `FastAPI()` app (full CRUD, typed path/query
  params, a real pydantic request body, a real `HTTPException`) to the real, reusable
  `samples/asgi_server.py`. Started as a real background process and driven entirely with real `curl`
  over a real HTTP/1.1 connection — every route matched hand-derived expected output exactly, zero new
  bugs found, no interpreter changes needed. This is the live, curl-able version of the
  GET/POST/PUT/DELETE milestone 4.1.10/4.1.11 already verified in-process via `TestClient`.
- **A real `locals()`/`globals()` nested-import scoping bug fixed (Phase 4.3).** `import anyio` from
  inside a function body raised a spurious `NameError: name '__value' is not defined` — real anyio's
  own `__init__.py` top-level `for __value in locals().values(): ...; del __value` idiom got the
  *importing function's* locals instead of anyio's own module dict, because `locals()`/`globals()`
  used `Interp.CurrentFrame` (which deliberately skips module-level frames to find the nearest
  enclosing function call — correct for `super()`'s own need, wrong here). Fixed by switching both to
  `Interp.InnermostFrame`, correctly reflecting whatever code is running right now regardless of what
  triggered it. `super()` itself untouched. A pre-existing, separate, smaller gap (`locals()` inside a
  class body — no Frame is pushed for class-body execution at all) was found but deliberately not
  pursued (nothing real reaches it). Full blow-by-blow in `FASTAPI_PLAN.md` Phase 4.3.
- **Real WebSocket support for the live ASGI server (Phase 4.3.1).** `samples/asgi_server.py`'s
  `serve()` now speaks a genuine RFC 6455 handshake (SHA1+base64 on `Sec-WebSocket-Key`) and real
  binary frame framing/masking both ways, bridged to the same real ASGI `websocket` scope/receive/
  send protocol already verified against starlette (Phase 3.1.11) — any real ASGI app works
  unmodified, not just the sample's own dependency-free demo. Verified live over a real socket with a
  from-scratch Python WebSocket client (handshake, sequential text messages, a binary frame,
  ping/pong, an extended-length frame, a clean close) — zero bugs found, every value matched a
  hand-derived expectation, including RFC 6455's own canonical worked example as an independent
  cross-check. Full blow-by-blow in `FASTAPI_PLAN.md` Phase 4.3.1.
- **WebSocket hardening: real fragmentation + a real closing handshake (Phase 4.3.2).** Closed the two
  v1 simplifications 4.3.1 had deliberately left open: large messages a real client fragments across
  several frames are now correctly reassembled (a control frame like a ping arriving *between*
  fragments, explicitly legal per RFC 6455, no longer disturbs the in-progress message), and receiving
  a client close frame now echoes a real close-frame reply back before the connection actually ends,
  completing the real RFC 6455 closing handshake instead of just going silent. Also fixed two smaller
  defensive gaps found on review: a handshake missing `Sec-WebSocket-Key` entirely now gets a real 400
  instead of crashing, and calling `receive()` again after a disconnect stays well-defined instead of
  reading a closing socket. Every behavior verified by hand, live over a real socket, before any test
  was written. Full blow-by-blow in `FASTAPI_PLAN.md` Phase 4.3.2.
- **WebSocket threaded through `fastapi_demo.py` itself (Phase 4.3.3).** A real `@app.websocket("/ws")`
  route using real starlette's own `WebSocket`/`WebSocketDisconnect` — the same class already verified
  against hand-built ASGI triples — now runs for the first time over `asgi_server.py`'s real
  raw-socket RFC 6455 implementation. Verified live: real handshake, two echoed messages, a
  client-initiated close correctly completing the real closing handshake, and (separately) a real
  abrupt disconnect correctly raising and catching starlette's own `WebSocketDisconnect` server-side —
  zero bugs found. Full blow-by-blow in `FASTAPI_PLAN.md` Phase 4.3.3.
- **Real graceful shutdown: `signal.signal()` + two genuine event-loop bugs found building it
  (Phase 4.4).** Real `signal.signal()`/`getsignal()`/`SIG_DFL`/`SIG_IGN`, backed by .NET's own
  `PosixSignalRegistration` — verified live by the author's own hand in a real interactive terminal
  (this session's sandbox has no usable console for automated Ctrl+C testing, confirmed via a minimal
  PySharp-independent probe hitting the same non-delivery). Caught and fixed a real bug from that same
  manual test: `SystemExit` raised inside a signal handler surfaced as a raw .NET stack trace instead
  of a clean process exit. Built real graceful shutdown on top in `samples/asgi_server.py`'s `serve()`
  (stop accepting new connections immediately on SIGINT/SIGTERM, drain in-flight ones up to 10s) — and
  while verifying the drain logic against real sockets, found and fixed two genuine, previously-latent
  event-loop bugs: `asyncio.Event.set()` could silently abandon a real pending waiter behind a stale
  cancelled one (a real, reproducible hang, not a theoretical gap), and a cancelled `sock_accept`/
  `sock_recv`/`sock_sendall` could resolve its future a second time when the underlying real socket
  operation later completed — both now fixed at the root, plus general hardening so an exception
  escaping any scheduled callback degrades to a stderr message instead of hanging the whole loop. Full
  blow-by-blow in `FASTAPI_PLAN.md` Phase 4.4.
- **pydantic v1 robustness sweep: 30 real-world field/validator patterns probed, 7 real gaps found
  and closed (Phase 4.5).** The author's own call — sharing this project means it needs to be
  robust — picked over starting scenario 3. Two rounds (16 then 14 real-world patterns: `Optional`/
  `List`/`Dict`/nested/`Enum` fields, `Field()` constraints, `Union`, `default_factory`,
  `parse_obj`/`.json()`, `orm_mode`, `@validator`/`@root_validator` (plain, `pre=True`, multi-field),
  `conint`/`constr`, real ISO-string `datetime` fields, `Field(alias=...)`, model inheritance,
  `.copy(update=...)`, `ValidationError.errors()`, `Config.extra`, `.dict(exclude=/include=)`,
  `.schema()`) — 25/30 already worked correctly; 7 real, previously-latent gaps found and fixed: raw
  `classmethod`/`staticmethod` objects didn't support `.__func__` or arbitrary attribute assignment
  (breaking `@validator`/`@root_validator` internals), their real type name/`isinstance()` behavior
  was wrong (breaking pydantic's own field-vs-validator classification), `date`/`time`/`datetime`
  only accepted positional arguments (breaking every real ISO-string date/time/datetime field, since
  pydantic's own parser constructs them entirely by keyword), `time.microsecond` silently truncated
  sub-millisecond precision, `collections.abc.Set`/`MutableSet`/`typing.AbstractSet` had no
  isinstance duck-typing support (breaking `.dict(exclude=/include=)`), `dict.fromkeys` didn't exist,
  and `inspect.getdoc` didn't exist (breaking `.schema()`). Full blow-by-blow in `FASTAPI_PLAN.md`
  Phase 4.5.
- Tests: **1072 green** (up from 547 — pydantic v1 + starlette/anyio + match/case probe-driven work
  across `FASTAPI_PLAN.md`, plus aiomqtt/other work in between, plus 13 new sqlite3 tests + 11 new
  pyodbc tests + 9 new http.client tests), confirmed via 7 consecutive full-suite runs.
- `sqlite3` (scenario 3a): a real DB-API 2.0 shim over `Microsoft.Data.Sqlite` —
  `connect`/`Connection`/`Cursor`, real transactions matching CPython's own legacy transaction
  control, `row_factory`/`sqlite3.Row`, the real PEP 249 exception hierarchy. Full blow-by-blow in
  `SQL_PLAN.md` Phase 1.
- `pyodbc` (scenario 3c): a real DB-API 2.0 shim over `Microsoft.Data.SqlClient`, verified live
  against a real SQL Server LocalDB instance — real `pyodbc.Row` (tuple + attribute access),
  pyodbc's own autocommit=False transaction model, a real `lastrowid` via a combined-batch
  `SCOPE_IDENTITY()` (a real cross-batch-scoping gotcha found and fixed live), real
  `date`/`time`/`datetime` round-tripping. Full blow-by-blow in `SQL_PLAN.md` Phase 3.
- `http.client` (scenario 4): a real, subclassable HTTPConnection/HTTPSConnection/HTTPResponse —
  the real, unmodified `requests` package (→ `urllib3`) runs live over real HTTPS. 25 real gaps
  found and fixed along the way, about half genuine new stdlib modules and half general interpreter
  bugs unrelated to HTTP (`str(bytes, encoding, errors)`, `hasattr` on builtin containers,
  `typing.Protocol` structural `isinstance()`, `Mapping`/`MutableMapping` mixins, a `NamedTuple`
  construction bug, and more). Full blow-by-blow in `HTTP_PLAN.md`.
- File system API (scenario 8): new **`glob`** and **`shutil`** C# modules built from scratch (neither
  existed before), plus a pervasive fix — `os`/`os.path` never coerced path-like (`__fspath__`)
  arguments (e.g. a real `pathlib.Path` passed to `os.path.relpath`), fixed with a new
  `OsModule.PathArg` helper used across a full-file rewrite of `OsModule.cs`. `pathlib.Path` gained
  `glob`/`rglob`/`iterdir`/`relative_to` and real ordering operators (needed for `sorted()` over
  `Path` objects). Verified live via `samples/filesystem_demo.py`. Full blow-by-blow in
  `FILESYSTEM_PLAN.md`.
- File system API (scenario 8) (see full entry above): **1079 green** (up from 1072 — 7 new
  filesystem tests), confirmed via 5 consecutive full-suite runs.
- MQTT broker (scenario 6): a real, hand-rolled MQTT 3.1.1 broker on this project's own
  `socket`/`asyncio`/`struct`/`threading` (see full entry above) — the first scenario since 5 to
  need **zero interpreter changes**, every primitive it touches was already solid. Tests:
  **1083 green** (up from 1079 — 4 new broker tests, including a full real pub/sub round trip
  between two real paho-mqtt clients and a hand-rolled broker, all local/no network), confirmed
  via 5 consecutive full-suite runs.
- AMQP / RabbitMQ (scenario 7) (see full entry above): a real, hand-rolled AMQP 0-9-1 broker
  driving a real, unmodified `pika` client — 3 new modules (`ast`, `numbers`, `heapq`) plus real
  fixes to `ABCMeta`'s 3-arg call, `defaultdict`'s missing mixin methods, `bytes.split()`'s no-arg
  form, `OSError.errno`/`.strerror`/`.filename`, and `select.select()`/`getsockopt()` for raw file
  descriptors. Tests: **1095 green** (up from 1083 — 12 new tests), confirmed via multiple
  consecutive full-suite runs.

_Update these numbers at every milestone._

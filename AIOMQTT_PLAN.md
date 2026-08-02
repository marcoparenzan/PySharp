# aiomqtt support — Azure IoT Hub device, async — a step-by-step plan

**Goal.** Get [samples/iothub_device_aiomqtt.py](samples/iothub_device_aiomqtt.py) — the async
counterpart of scenario 1 ([samples/iothub_device_mqtt.py](samples/iothub_device_mqtt.py)) — running
end-to-end under PySharp, using the **real `aiomqtt` package downloaded from PyPI unmodified**
(2.5.1 at the time of writing; formerly `asyncio-mqtt`). Same method as every other scenario: run the
real script, fix what breaks, repeat. See ROADMAP.md's "Method: scenario-driven development" and
"Process to add a scenario".

**Why this is its own plan and not a two-line diff.** `aiomqtt` only depends on `paho-mqtt` (already
vendored, scenario 1) and `typing_extensions` (only on Python <3.11), but internally it drives a
sizeable slice of `asyncio` that PySharp's `asyncio` module does not implement yet: `Queue`, `Lock`,
`Event`, `Semaphore`, `wait`/`FIRST_COMPLETED`, and a real event-loop reactor (`add_reader`/
`add_writer`/`call_soon_threadsafe`/`run_in_executor`). It also needs a `contextlib` module, which
does not exist in PySharp at all yet. That is the same class of gap as `NUMPY_PLAN.md` — hence the same
kind of phased, checkbox-driven plan.

---

## Key architecture decisions (read first)

- **What's already there (verified by reading `src/PySharpLib/Modules/AsyncioModule.cs` and
  `Runtime/Async.cs`).** `asyncio.run`/`sleep`/`gather`/`create_task`/`ensure_future`/`wait_for`/
  `get_running_loop`/`get_event_loop`/`Future`, plus `CancelledError`/`InvalidStateError`/
  `TimeoutError`. Coroutines run on a dedicated thread with a semaphore handshake (`PyCoroutine`),
  driven by `PyEventLoop` (a single-threaded ready-queue + timer-heap loop, `CallSoon`/`CallLater`,
  already thread-safe via a lock + `SemaphoreSlim` wake).
- **What's missing (verified by grepping `aiomqtt/client.py` 2.5.1 for every `asyncio.*` and running
  a real import probe against a real PyPI install — see Phase 0):** `contextlib` (whole module),
  `asyncio.Queue`/`Lock`/`Event`/`Semaphore`, `asyncio.wait` + `FIRST_COMPLETED`, `loop.add_reader`/
  `remove_reader`/`add_writer`/`remove_writer`, `loop.call_soon_threadsafe`, `loop.run_in_executor`.
  First actual error hit when importing real `aiomqtt` in PySharp today: `ModuleNotFoundError: No
  module named 'contextlib'`.
- **Lock/Event/Semaphore/Queue = `PyClass` + C# state**, exactly like `socket`/`ndarray`: the class
  dict holds `__aenter__`/`__aexit__` (for `async with`) and the blocking methods as builtin
  coroutine-callable functions; the actual wait/wake state lives in a small C# object per instance.
  No new interpreter core changes — `async with` on user-level `__aenter__`/`__aexit__` already works
  (landed with async/await, [[async-asyncio-scenario2]]).
- **Waiting = park a continuation on `PyEventLoop`, don't block a thread.** `Lock`/`Event`/`Semaphore`/
  `Queue` should all suspend the same way `await` already suspends a coroutine (semaphore handshake on
  the coroutine's own thread), and get resumed via `PyEventLoop.CallSoon` when the resource becomes
  available — mirror the existing `PyFuture`/`PyTask` wake path, do not invent a second mechanism.
- **`add_reader`/`add_writer` reuse `Socket.Select`, not a new abstraction.** `select.select()`
  ([SelectModule.cs](src/PySharpLib/Modules/SelectModule.cs)) already resolves PySharp socket objects
  to `System.Net.Sockets.Socket` and polls with `Socket.Select`. `fileno()`
  ([SocketModule.cs:372](src/PySharpLib/Modules/SocketModule.cs)) already returns the raw OS handle
  (`Socket.Handle`) as the int fd `add_reader`/`add_writer` receive. The only missing piece is a
  process-wide `handle -> Socket` registry (populated where sockets are created/closed in
  `SocketModule`) so a bare int fd can be resolved back to a `Socket`, plus a poller thread on
  `PyEventLoop` that `Socket.Select`s the registered fds each tick and `CallSoon`s the ready callbacks
  back onto the loop.
- **`run_in_executor` reuses the existing thread+resume pattern.** Run the callable on the CLR thread
  pool, resolve a `PyFuture` via `CallSoon` when it finishes — same shape as how a coroutine's own
  thread reports completion today, just with `ThreadPool.QueueUserWorkItem` instead of a dedicated
  `Thread`.
- **`contextlib` v1 scope**: only `contextmanager` (decorator turning a generator function into a
  `with`-usable object) and `suppress(*exceptions)` — the two aiomqtt actually calls. No
  `asynccontextmanager`, `ExitStack`, `AbstractContextManager` unless a later scenario needs them.
- **Test milestone folder**: `src/PySharp.Tests/M15_Aiomqtt/` (`M14_Numpy` is already claimed by
  `NUMPY_PLAN.md`).
- **Package install**: `aiomqtt` is a **pure-Python wheel** with no extra runtime deps beyond
  `paho-mqtt`, so `pysharp install aiomqtt` already works today (verified: installed 2.5.1 for real
  from PyPI with the existing `PackageInstaller`, no changes needed there).

## Execution rules (how to run this plan)

1. **One step at a time**, each a small change with a visible deliverable (a passing test).
2. **Ground every phase in a real run**, not a read-through: after each phase, re-run the probe/sample
   against the real `aiomqtt` package and record the *next* error before planning the next phase — the
   catalog above is accurate as of 2026-08-01 but is not guaranteed exhaustive past Phase 1.
3. **Keep the suite green** after every step (`dotnet test`). Never leave it red between steps.
4. MIT header on every new file; match surrounding code style.
5. Commit per step or per small group; bump the package version once per phase and re-pack to the
   local NuGet feed when a phase completes ([[nuget-local-feed]]).
6. Update this file: tick the box, add a one-line note if a decision was made or the catalog above was
   wrong about something.
7. When the scenario lands, update ROADMAP.md (new "Scenario 1b" row/section) and RELEASE_NOTES.md,
   and delete the "STATUS — target script" note at the top of the sample.

---

## Phase 0 — Groundwork ✅ (done while writing this plan)

- [x] 0.1 Write the target script
  [samples/iothub_device_aiomqtt.py](samples/iothub_device_aiomqtt.py): async counterpart of scenario
  1, using `aiomqtt.Client` as an async context manager, `async for message in client.messages`, same
  SAS/connection-string helpers, same D2C/C2D/twin flow.
- [x] 0.2 Confirm `pysharp install aiomqtt` works against real PyPI (it does — 2.5.1, pure Python,
  only pulls in `paho-mqtt` which is already vendored).
- [x] 0.3 Confirm the first failure importing real `aiomqtt`: `ModuleNotFoundError: No module named
  'contextlib'`.
- [x] 0.4 Add `AiomqttInstallFixture` in `src/PySharp.Tests/M15_Aiomqtt/` mirroring
  `PahoInstallFixture` ([PipInstallTests.cs:58](src/PySharp.Tests/M7_Pip/PipInstallTests.cs)): installs
  `aiomqtt` into a temp site-packages dir for the test class. One smoke test: `import aiomqtt` fails
  with the contextlib `ModuleNotFoundError` (documents the starting gap; flip to an import-success
  assertion once Phase 1 lands).

## Phase 1 — `contextlib` ✅

- [x] 1.1 New `src/PySharpLib/Modules/ContextlibModule.cs`: `contextmanager(gen_func)` (decorator;
  the returned object drives the wrapped generator manually through `__enter__`/`__exit__`, including
  re-raising into the generator via `.throw()` semantics on exception so `try/finally` inside the
  generator runs) and `suppress(*exceptions)` (a context manager whose `__exit__` returns `True` when
  the raised exception is an instance of one of the given types). Register in
  `StdlibModules.RegisterAll`. Required a core addition: `PyGenerator` had no way to inject an
  exception at a suspended `yield` (only plain resume via `MoveNext`), so `gen.throw()` couldn't be
  emulated and a `with`-body exception would silently skip the generator's `finally`. Added
  `PyGenerator.ThrowInto` (`Runtime/PyGenerator.cs`), refactored alongside `MoveNext` onto a shared
  `Resume` helper — same thread/semaphore handshake, just with a pending-exception flag the generator
  thread's `Yield()` checks before returning. `contextmanager.__exit__` uses it directly; a Python-level
  `gen.throw()` was **not** added to `GeneratorMethods.Table` (nothing in this scenario calls it).
- [x] 1.2 Tests: `M6_Stdlib/ContextlibTests.cs` (6 tests) — normal enter/exit, `finally` runs and the
  exception still propagates, the generator can suppress a `with`-body exception via its own
  `except`, `contextmanager` used as a bound instance method (mirrors aiomqtt's actual usage),
  `suppress` swallowing a listed exception, `suppress` letting an unlisted one through.
- [x] 1.3 Re-run the Phase 0 probe; record the next error. Actual (the pre-Phase-1 catalog was not
  exhaustive, as flagged in the execution rules): `from types import TracebackType` — PySharp had no
  `types` module at all — then `sys.version_info >= (3, 11)` — PySharp's `version_info` didn't support
  rich comparison against a tuple. Fixed both: `MiscModules.CreateTypes()` (minimal `types` module:
  `TracebackType`/`FunctionType`/`ModuleType`/`GeneratorType`, registered in `StdlibModules.cs`), and
  `__lt__`/`__le__`/`__gt__`/`__ge__`/`__eq__` on `sys.version_info` in `SysModule.cs` (delegates to
  `Interp.Compare` against a `PyTuple`, the same tuple-ordering CPython uses). Also added
  `typing.Concatenate`/`typing.Self`/`typing.TypeAlias` (plain placeholders) and `typing.ParamSpec`
  (callable like `TypeVar`) — `aiomqtt/types.py` needed them. Tests: `M5_Imports/ImportTests.cs`
  (`Version_info_compares_against_a_tuple`, `Types_module_exposes_TracebackType`). After these fixes,
  **`import aiomqtt` fully succeeds** (`M15_Aiomqtt/AiomqttSmokeTests.Import_succeeds`). Next error,
  constructing a `Client` inside `asyncio.run()`: `AttributeError: 'module' object has no attribute
  'Lock'` — exactly Phase 2's starting point, confirming the original catalog from here on.

## Phase 2 — `asyncio.Lock` / `asyncio.Event` / `asyncio.Semaphore` ✅

- [x] 2.1 Implemented in `AsyncioModule.cs`, `PyClass`-with-wrap like `socket`/`contextlib`'s context
  manager (`LockWrap`/`EventWrap`/`SemWrap` behind a shared `"__wrap__"` instance-dict key): `Lock`
  (`acquire`/`release`/`locked`, `__aenter__`=`acquire`/`__aexit__`), `Event` (`set`/`clear`/`is_set`/
  `wait`), `Semaphore`/`BoundedSemaphore` (`acquire`/`release`/`locked`, `async with`). Confirmed the
  "park a continuation" idea needed no new primitive: `acquire()`/`wait()` just return a `PyFuture`
  (already-resolved if free, or queued and resolved later by `release()`/`set()`) — the exact algorithm
  CPython's own `asyncio.Lock`/`Semaphore` use internally, and it needed zero changes to
  `Runtime/Async.cs`. `__aexit__`/other "returns None but must be awaitable" spots use a small
  `DoneFuture(value)` helper (an already-resolved `PyFuture`).
- [x] 2.2 Tests: `M10_Async/AsyncioSyncPrimitivesTests.cs` (7 tests) — `Lock` serializes two
  coroutines in acquire order, `locked()` reflects state, `release()` without `acquire()` raises
  `RuntimeError`; `Event.wait()` unblocks after `set()` from another coroutine and returns immediately
  if already set; `Semaphore(2)` caps concurrent holders at 2 across 5 workers;
  `BoundedSemaphore` raises `ValueError` on an excess `release()`.
- [x] 2.3 Re-ran the Client-construction probe: next error is exactly the predicted Phase 3 start —
  `AttributeError: 'module' object has no attribute 'Queue'`.

## Phase 3 — `asyncio.Queue` ✅

- [x] 3.1 `asyncio.Queue`/`LifoQueue` (+ `QueueFull`/`QueueEmpty`) in `AsyncioModule.cs`, same
  `PyClass`-with-wrap shape as Phase 2. `put`/`get` return a `PyFuture` (already-resolved if the queue
  has room/an item, parked otherwise); `AddItem`/`WakePutter` hand items directly from a putter to a
  waiting getter (or vice versa) without an intermediate poll — the same "hand off directly" pattern
  Phase 2's `Lock`/`Semaphore` release uses.
- [x] 3.2 Tests: `M10_Async/AsyncioQueueTests.cs` (8 tests) — immediate get, FIFO producer/consumer,
  get blocking until another coroutine puts, `put_nowait`/`get_nowait` past capacity raising
  `QueueFull`/`QueueEmpty`, a bounded `put` blocking until a `get` frees a slot, `qsize`/`empty`/`full`,
  `LifoQueue` ordering.
- [x] 3.3 **Found and fixed a pre-existing race, surfaced by the added test volume**: `dotnet test`
  hung (180s+) running the full suite, though every new test passed in isolation.
  `PyEventLoop.Running` is a single **process-wide** static (deliberately not `[ThreadStatic]` — a
  coroutine's own background thread needs to see it too), so two `asyncio.run()` calls from different
  test classes running in parallel — xUnit's default across collections — stomp on each other's
  "current loop" and deadlock. `AsyncServerTests` already had a `DisableParallelization` collection for
  a different stated reason (CPU starvation); renamed it `asyncio-run`, documented the real constraint,
  and added every `asyncio.run()`-driving test class to it (`AsyncioTests`, `AsyncioSyncPrimitivesTests`,
  `AsyncioQueueTests`, `M15_Aiomqtt/AiomqttSmokeTests`). Confirmed fixed: 4 consecutive full-suite runs,
  653 tests, ~2s each, no hang. **This constraint applies to every future asyncio.run()-based test —
  add new ones to the `asyncio-run` collection.**
- [x] 3.4 Re-ran the probe: `aiomqtt.Client("example.com", identifier="dev1")` now constructs
  **fully successfully** inside `asyncio.run()` (`M15_Aiomqtt/AiomqttSmokeTests.
  Client_constructs_inside_a_running_loop`) — Queue was the last thing `__init__` needed. Pushed one
  step further with a live probe (`async with aiomqtt.Client("test.mosquitto.org") as client:`): next
  error is `AttributeError: 'AbstractEventLoop' object has no attribute 'run_in_executor'` in
  `__aenter__` — Phase 5's territory, reached before Phase 4's `asyncio.wait` (that one only fires
  later, inside `client.messages` iteration). Doing Phase 4 next anyway, per plan order — it's small
  and self-contained, and `wait`/`FIRST_COMPLETED` is needed regardless before Phase 6 can iterate
  `client.messages`.

## Phase 4 — `asyncio.wait` + `FIRST_COMPLETED` ✅

- [x] 4.1 `asyncio.wait(aws, *, return_when=..., timeout=...)` in `AsyncioModule.cs`: materializes the
  iterable via `PyOps.Iterate`, wraps each item with `AsyncRuntime.EnsureFuture` (accepts a bare
  `Future` alongside a `Task`, exactly how aiomqtt calls it — `(task, self._client._disconnected)`),
  and resolves an outer `PyFuture` to `(done, pending)` `PySet`s once the `return_when` condition is
  met (`FIRST_COMPLETED`/`FIRST_EXCEPTION`/`ALL_COMPLETED`, the last being the default). `FIRST_COMPLETED`/
  `FIRST_EXCEPTION`/`ALL_COMPLETED` registered as plain strings (matches real CPython — they *are*
  strings there too). `done`/`pending` as `PySet` needed no new hashing support: `PyOps.PyHash`/
  `PyEquals` already fall back to CLR reference identity for arbitrary objects, exactly Python's
  default `Task`/`Future` semantics.
- [x] 4.2 Tests: `M10_Async/AsyncioWaitTests.cs` (4 tests) — `FIRST_COMPLETED` returns as soon as the
  fast task finishes with the slow one left in `pending`; the `done` set actually contains the
  finished task and its result is readable; `ALL_COMPLETED` is the default; a bare `Future` mixed
  with a `Task` in the same `wait()` call.

## Phase 5 — event-loop reactor: `add_reader`/`add_writer`, `call_soon_threadsafe`, `run_in_executor` ✅

- [x] 5.1 `call_soon_threadsafe`: thin alias over the already-thread-safe `PyEventLoop.CallSoon`.
- [x] 5.2 `run_in_executor(None, fn, *args)`: `ThreadPool.QueueUserWorkItem`, resolves a `PyFuture` via
  `CallSoon` on completion (success or exception). **Documented, not solved, limitation**: unlike
  coroutine/generator bodies (handshake-gated so only one ever executes at a time — see the
  `Runtime/Async.cs` file header), the offloaded callable runs genuinely concurrently with whatever
  else the loop dispatches meanwhile; no interpreter-wide execution lock exists. Safe for what this
  scenario needs (offloading a blocking call that doesn't race with other coroutines over shared
  Python state); flagged in a code comment as out of scope for a general fix here.
- [x] 5.3 Added a `handle -> Socket` registry to `SocketModule` (`RegisterHandle`/`TryResolveHandle`),
  populated lazily inside the existing `fileno()` builtin (every caller of `add_reader`/`add_writer`
  calls `fileno()` first to get the fd, so this needed no changes at the socket-creation call sites).
  Added `AddReader`/`RemoveReader`/`AddWriter`/`RemoveWriter` to `PyEventLoop`: a background poller
  thread that `Socket.Select`s the registered fds each tick (same primitive `SelectModule` already
  uses) and `CallSoon`s the ready callback back onto the loop.
- [x] 5.4 Tests: `M10_Async/AsyncioReactorTests.cs` (4 tests) — `call_soon_threadsafe` resolves a
  future; `run_in_executor` runs a blocking call and returns its result; `add_reader` fires when data
  arrives on a real loopback socket pair; `add_writer` fires when a socket becomes writable.
- [x] 5.5 **Found and fixed a real double-fire race** while writing the `add_reader` test: the poller
  thread runs independently of the loop thread, so it could call `Socket.Select` again and re-detect
  the *same still-undrained* socket as ready before the loop thread had even run the first scheduled
  callback (which would have drained it) — the callback then fired twice, and the second `recv()` hit
  `BlockingIOError`. Fixed with an in-flight set per fd (`_inFlightReaders`/`_inFlightWriters`):
  a fd is excluded from the next `Select()` from the moment its callback is `CallSoon`'d until that
  callback has actually run. Verified with 5 consecutive clean runs of the reactor tests after the fix
  (they reproduced the race before it).
- [x] 5.6 **Found and fixed real interpreter bugs via the live connect probe** (`async with
  aiomqtt.Client("test.mosquitto.org") as client:` against the real public broker — not just imports):
  - `isinstance(x, int)` was `False` for `enum.IntEnum` members (`EnumModule` gives them `__eq__`/
    `__int__`/arithmetic but `isinstance` never special-cased them). paho's `ConnackCode(IntEnum)`
    compared against a `ReasonCode` via `isinstance(other, int)` inside `ReasonCode.__eq__`; the
    always-`False` check meant `reason_code == mqtt.CONNACK_ACCEPTED` was `False` even on a real
    successful connect, so aiomqtt raised `MqttConnectError` on **every** connection, success or not.
    Fixed in `Builtins.TypeMatchesBuiltinName` (`"int"` now also matches an `IntEnum`-subclass
    instance). Regression test added to the existing `IntEnum_members_behave_like_ints`.
  - `dataclasses.dataclass` was a no-op stub (class returned unchanged, no generated `__init__`).
    aiomqtt wraps **every incoming message's topic** in `Topic(str)`, a
    `@dataclass(frozen=True) class Topic(Wildcard)` with zero fields of its own (inherits `value: str`
    from `Wildcard`) — so this broke the very first received message (`Topic() takes no arguments`),
    not just an unused edge case. Implemented real field-driven `__init__`/`__repr__`/`__eq__`
    generation in `MiscModules.ApplyDataclass`, walking `cls.Mro` base-to-derived so a subclass that
    adds no fields still inherits its base's (exactly the `Topic(Wildcard)` shape), `__post_init__`
    support, and a frozen `__setattr__` guard. Tests: `M6_Stdlib/DataclassesTests.cs` (5 tests).
  - Implementing frozen surfaced a second, adjacent bug: `Interp.SetAttr`'s `__setattr__` dispatch
    only accepted a user-defined `PyFunction`, silently ignoring a class's builtin (`PyBuiltinFunction`)
    `__setattr__` — asymmetric with every other dunder dispatch path (`__getattr__`, `__enter__`, …),
    which already handle both via `PyBoundMethod`. This is exactly the pattern the frozen-dataclass
    guard needs (like `socket`'s other dunders). Fixed by widening the check to
    `PyFunction or PyBuiltinFunction`; confirmed safe — `object.__setattr__` already existed as a
    builtin with the *same* default behavior the fallback path already had, so no other class's
    behavior changes.
  - `loop.create_task()`/`asyncio.create_task()` strictly required a `PyCoroutine`, but
    `Queue.get()`/`Lock.acquire()`/etc. return an already-awaitable `PyFuture` directly (a deliberate
    implementation shortcut, not a real coroutine body) — so aiomqtt's `MessagesIterator.__anext__`
    (`loop.create_task(self._queue.get())`) failed with `TypeError: a coroutine was expected`.
    Relaxed both to go through `AsyncRuntime.EnsureFuture` (already used by `ensure_future`/`gather`),
    accepting a bare `Future` alongside a `Task`. Regression test in `AsyncioQueueTests.cs`.
- [x] 5.7 **Full live round-trip confirmed working against the real `test.mosquitto.org` broker**:
  connect, subscribe, concurrent publish + `async for message in client.messages` iteration, clean
  disconnect — the core aiomqtt scenario runs end-to-end. (Manual probe script, not yet a committed
  test — Phase 6 wires this into `samples/iothub_device_aiomqtt.py` and decides the test story for a
  live-network run.)

## Phase 6 — wire it to the real `aiomqtt` scenario ✅

- [x] 6.1 Offline tests (no network) in `M15_Aiomqtt/AiomqttSmokeTests.cs`, mirroring
  [IoTHubSampleTests.cs](src/PySharp.Tests/M9_IoTHub/IoTHubSampleTests.cs) (5 tests total): import
  succeeds; `Client(...)` constructs inside a running loop; the sample imports as a module without
  running `main()`; its SAS/connection-string helpers match the sync sample's; a real
  `aiomqtt.Topic("...")` constructs correctly (regression pin for the dataclasses fix).
- [x] 6.2 Ran the real end-to-end flow against `test.mosquitto.org` (manual probe scripts, port 1883
  plain — see 5.6/5.7) and found two more real gaps beyond Phase 5's list, both fixed:
  - `ssl.CertificateError` didn't exist. paho's TLS path (`_ssl_wrap_socket`) has
    `except ssl.CertificateError:` as its *first* handler when wrapping the socket; evaluating an
    `except` clause's type looks up the name regardless of which exception (if any) it ends up
    matching, so this crashed with `AttributeError` before paho's real TLS logic ever ran. Real
    CPython: `CertificateError` is a deprecated alias for `SSLCertVerificationError`. Fixed the same
    way in `SslModule.cs` (`d["CertificateError"] = CertVerificationErrorClass`).
  - The predicted `X | None` annotation / `ProtocolVersion.V311.value` gaps from the original catalog
    **never actually surfaced** — turned out not to be load-bearing for this scenario. Noted here so a
    future session doesn't go looking for problems that don't exist.
  - What looked like a bug at first *wasn't one*: connecting over TLS to `test.mosquitto.org:8883`
    failed with `SSLError: ... certificate was rejected`. Traced with raw `ssl.wrap_socket` probes:
    `www.google.com:443` (a properly CA-signed host) wrapped fine, confirming PySharp's TLS/cert
    validation is correct; `openssl s_client -connect test.mosquitto.org:8883` showed the server
    presents a cert signed by Mosquitto's own private test CA (`issuer=...OU=CA, CN=mosquitto.org`),
    not a publicly trusted one — no client should accept that without explicitly loading their CA cert
    or disabling verification. **Confirmed as PySharp correctly rejecting an untrusted certificate**,
    not a defect. This is exactly why the sample script hardcodes port 8883 with default (CA-verifying)
    TLS: it targets Azure IoT Hub, which uses a properly chained public cert, same as scenario 1's sync
    sample already proved end-to-end. The live pub/sub round trip in 5.7 used plain port 1883 instead
    (matching scenario 5's precedent for broker probes), which is why TLS itself needed this separate,
    dedicated check.
- [x] 6.3 Full real async round trip confirmed end-to-end (5.7): connect, subscribe, concurrent
  publish + `async for message in client.messages`, clean disconnect, all against the real public
  broker. TLS handshake/cert validation itself was proven correct in isolation (6.2).
- [x] 6.4 **Verified end-to-end against the author's real Azure IoT Hub** (SAS auth,
  `samples/config.json`, `pysharp run iothub_device_aiomqtt.py samples/config.json`) — and found one
  more real bug on the way, invisible until this exact run: the sample **hung forever** right after
  printing `[main] connecting to ...`, never reaching `[mqtt] connected`. The sync (`paho-mqtt`)
  sample against the *same* hub worked instantly, which ruled out network/auth/TLS-cert issues and
  pointed at something specific to the async reactor.
  - **Root cause**: `SslModule.cs`'s `fileno()` returned the underlying raw socket's handle, but —
    unlike `SocketModule.cs`'s own `fileno()` — never called `SocketModule.RegisterHandle()` on it.
    `add_reader`/`add_writer` only ever receive that bare int fd (the asyncio API shape) and resolve
    it back to a real `Socket` through `SocketModule`'s handle registry (`PyEventLoop.IoPollLoop`,
    `Runtime/Async.cs`). For a TLS-wrapped socket the fd never got registered, so the poller's
    `TryResolveHandle` always failed, the fd was silently dropped from every `Socket.Select` call, and
    the reader callback that would deliver CONNACK (and everything after it) never fired — a permanent
    hang. This path is TLS-only, which is exactly why 5.7's plaintext `test.mosquitto.org:1883` round
    trip never exercised it and 6.2's TLS check (a bare `wrap_socket` connect probe, no reactor
    involved) didn't either — it took a real `async with aiomqtt.Client(..., tls_context=...)` run to
    surface it.
  - **Fix**: one line in `SslModule.cs`'s `fileno()` — call `SocketModule.RegisterHandle(sock)` before
    returning the handle, mirroring what the plain `socket` module already does.
  - **Debugging note for future sessions**: found via temporary `Console.Error` instrumentation in
    `AddReader`/`IoPollLoop` (stderr, so it doesn't pollute the captured Python stdout) — one line
    logging every `AddReader` call, one logging every fd `Socket.Select` actually fired on. The
    `AddReader fd=...` line appeared exactly once and the fired-reader line never did, which is what
    pointed straight at "never resolves" rather than "resolves but Select never sees it ready"
    (a real alternative hypothesis at the time: `SslWrap`'s own decrypted-data buffer being invisible
    to a raw-socket-level `Socket.Select`, which paho's own `pending()`-draining loop in
    `_on_socket_open`'s callback already guards against — worth remembering if a *different* TLS hang
    ever shows up post-registration). All diagnostics were removed before committing the real fix.
  - Re-ran after the fix: full success — connect, twin GET (received the live desired/reported
    document), reported-properties PATCH (204), three D2C telemetry sends, 30s listen window, clean
    disconnect. `dotnet test` stayed green (670/670) throughout.

## Phase 7 — docs

- [x] 7.1 ROADMAP.md: added a "Scenario 1b — Azure IoT Hub device, async (aiomqtt)" row to the
  scenarios table, a full section modeled on the Scenario 1 writeup (right after it), an
  "Interpreter evolution log" row listing every addition/fix, and updated the progress indicator line.
- [ ] 7.2 RELEASE_NOTES.md entry + package version bump. **Deliberately left undone**: the project's
  own convention (see NUMPY_PLAN.md's execution rules) is to bump `<Version>` and re-pack to the local
  NuGet feed (`D:\Dev\NuGetLocalFeed`) once a phase/scenario completes — both are side-effecting
  (publishing to the user's local feed) and RELEASE_NOTES.md has historically only grown at
  version-bump time (it currently has one entry, `v1.0.0`, despite the `.csproj`s already reading
  `1.4.1`). Not something to guess at; ask the user for the version number before touching either.
- [x] 7.3 Removed the "STATUS — target script" note from the top of
  [samples/iothub_device_aiomqtt.py](samples/iothub_device_aiomqtt.py); replaced with a short note on
  what's actually been verified (see 6.3).
- [x] 7.4 README.md "Verified scenarios and limits": updated the stdlib module list (`contextlib`,
  `types`, real `dataclasses`, the new `asyncio` surface), the "Done scenarios" line, and removed
  `contextlib`/`types` from the "modules missing today" list.

---

## Progress indicator

**All 7 phases done, including the real Azure IoT Hub run (6.4).** Core scenario proven end-to-end
against a real public MQTT broker, then against the author's own Azure IoT Hub — which surfaced one
more real bug (`SslModule.fileno()` not registering its handle, hanging every TLS-backed `add_reader`/
`add_writer`; see 6.4) invisible to every prior check, since neither the plaintext broker round trip
nor the TLS-connect-only check exercised a full TLS run through the reactor. Every gap/bug recorded
above was found by actually running real code (the real `aiomqtt` package, real generated test
traffic, a real broker, a real Azure hub), not guessed — including several that weren't in the
original Phase-0 catalog (the catalog explicitly wasn't assumed exhaustive past Phase 1; see the
execution rules). Full test count added across all phases: contextlib (6), Lock/Event/Semaphore (7),
Queue (9, incl. the create_task/Future regression), wait (4), reactor (4), IntEnum isinstance (+1 to
an existing test), dataclasses (5), aiomqtt smoke (5) — suite went from 635 to 670, all green.
Only 7.2 (RELEASE_NOTES/version bump) remains, deliberately deferred pending the author's input.

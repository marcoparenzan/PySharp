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
- [ ] 0.4 Add `AiomqttInstallFixture` in `src/PySharp.Tests/M15_Aiomqtt/` mirroring
  `PahoInstallFixture` ([PipInstallTests.cs:58](src/PySharp.Tests/M7_Pip/PipInstallTests.cs)): installs
  `aiomqtt` into a temp site-packages dir for the test class. One smoke test: `import aiomqtt` fails
  with the contextlib `ModuleNotFoundError` (documents the starting gap; flip to an import-success
  assertion once Phase 1 lands).

## Phase 1 — `contextlib`

- [ ] 1.1 New `src/PySharpLib/Modules/ContextlibModule.cs`: `contextmanager(gen_func)` (decorator;
  the returned object drives the wrapped generator manually through `__enter__`/`__exit__`, including
  re-raising into the generator via `.throw()` semantics on exception so `try/finally` inside the
  generator runs) and `suppress(*exceptions)` (a context manager whose `__exit__` returns `True` when
  the raised exception is an instance of one of the given types). Register in
  `StdlibModules.RegisterAll`.
- [ ] 1.2 Tests (`M15_Aiomqtt` or `M6_Stdlib`, whichever fits): a `@contextlib.contextmanager`
  decorated generator used in `with ...:` for both the normal and the exception path; `with
  contextlib.suppress(KeyError): raise KeyError()` doesn't propagate; an unlisted exception still does.
- [ ] 1.3 Re-run the Phase 0 probe; record the next error (expected: further into `dataclasses`/`enum`
  territory inside `aiomqtt/client.py`, or straight to a missing `asyncio.Queue`/`Lock`/etc. name).

## Phase 2 — `asyncio.Lock` / `asyncio.Event` / `asyncio.Semaphore`

- [ ] 2.1 Implement in `AsyncioModule.cs` per the "park a continuation" decision above: `Lock`
  (`acquire`/`release`/`locked`, usable as `async with lock:`), `Event` (`set`/`clear`/`is_set`/
  `wait`), `Semaphore`/`BoundedSemaphore` (`acquire`/`release`, `async with`).
- [ ] 2.2 Tests: two coroutines contending on a `Lock` (the second only proceeds after the first
  releases); `Event.wait()` unblocks after `set()` from another coroutine; `Semaphore(2)` caps
  concurrent holders at 2.

## Phase 3 — `asyncio.Queue`

- [ ] 3.1 `asyncio.Queue` (+ `LifoQueue`, `QueueFull`, `QueueEmpty`) with `put`/`put_nowait`/`get`/
  `get_nowait`/`qsize`/`empty`/`full`, `maxsize`-bounded, waiters parked via the Phase 2 machinery.
- [ ] 3.2 Test: producer/consumer coroutines over a bounded `Queue`; `put_nowait` past `maxsize` raises
  `QueueFull`.

## Phase 4 — `asyncio.wait` + `FIRST_COMPLETED`

- [ ] 4.1 `asyncio.wait(aws, *, return_when=...)` returning `(done, pending)` sets of Tasks/Futures;
  `asyncio.FIRST_COMPLETED`/`ALL_COMPLETED`/`FIRST_EXCEPTION` constants (`aiomqtt` only ever uses
  `FIRST_COMPLETED`, but all three are cheap to add together).
  matches CPython instead of copying its structure).
- [ ] 4.2 Test: two tasks (a fast one, a slow one), `asyncio.wait(..., return_when=FIRST_COMPLETED)`
  returns as soon as the fast one finishes, with the slow one still in `pending`.

## Phase 5 — event-loop reactor: `add_reader`/`add_writer`, `call_soon_threadsafe`, `run_in_executor`

- [ ] 5.1 `call_soon_threadsafe`: thin alias over the already-thread-safe `PyEventLoop.CallSoon`.
- [ ] 5.2 `run_in_executor(None, fn, *args)`: `ThreadPool.QueueUserWorkItem(fn)`, resolve a `PyFuture`
  via `CallSoon` on completion (success or exception).
- [ ] 5.3 Add a `handle -> Socket` registry to `SocketModule` (populate on socket creation, remove on
  close). Add `add_reader(fd, cb, *args)`/`remove_reader(fd)`/`add_writer(fd, cb, *args)`/
  `remove_writer(fd)` to `PyEventLoop`: a background poller thread that `Socket.Select`s the currently
  registered read/write fds each tick (same primitive as `SelectModule`) and `CallSoon`s the callback
  for whichever fds came back ready.
- [ ] 5.4 Tests: `add_reader` on a real loopback TCP socket pair (write end sends bytes, the registered
  callback fires and can read them); `add_writer` symmetrically; `remove_reader` stops further
  callbacks from firing.

## Phase 6 — wire it to the real `aiomqtt` scenario

- [ ] 6.1 Offline tests (no network) in `M15_Aiomqtt`, mirroring
  [IoTHubSampleTests.cs](src/PySharp.Tests/M9_IoTHub/IoTHubSampleTests.cs): `import aiomqtt` fully
  succeeds; `aiomqtt.Client("host", identifier="dev1", username=..., password=..., tls_context=...)`
  constructs without raising; `samples/iothub_device_aiomqtt.py` imports as a module without running
  `main()` (same pattern as the sync sample's first test).
- [ ] 6.2 Fix whatever the *real* end-to-end run (against `test.mosquitto.org` first, then a real Azure
  IoT Hub, same as scenarios 1 and 5) surfaces beyond the asyncio/contextlib gap above — expect small
  follow-ups here: `dataclasses.dataclass` currently only accepts classes that define their own
  `__init__` ([MiscModules.cs:164](src/PySharpLib/Modules/MiscModules.cs)) so `Will`/`TLSParameters`
  (frozen dataclasses with defaulted fields, unused by this sample but evaluated at import time) may
  need real field-driven `__init__` generation; `X | None` annotation evaluation under
  `from __future__ import annotations`; `enum.IntEnum` member access via `ProtocolVersion.V311.value`.
  Each fix gets its own tick + test in this section as it's found — do not pre-guess further than this.
- [ ] 6.3 Live smoke test against a real Azure IoT Hub (manual, like scenario 1): confirm D2C publish,
  C2D delivery, and twin GET/reported/desired round-trip all work through `aiomqtt`.

## Phase 7 — docs

- [ ] 7.1 ROADMAP.md: add a "Scenario 1b — Azure IoT Hub device, async (aiomqtt)" row to the scenarios
  table and a short section modeled on the Scenario 1 writeup, plus an "Interpreter evolution log"
  entry for `contextlib` + the new `asyncio` primitives.
- [ ] 7.2 RELEASE_NOTES.md entry.
- [ ] 7.3 Remove the "STATUS — target script" note from the top of
  [samples/iothub_device_aiomqtt.py](samples/iothub_device_aiomqtt.py).
- [ ] 7.4 README.md: add to "Verified scenarios and limits" if that section lists scenario 1.

---

## Progress indicator

Phase 0 done. Phases 1–7 not started. Every `asyncio.*` name cataloged as missing above was verified
by grep against the real `aiomqtt==2.5.1` source (`site-packages/aiomqtt/client.py`), not guessed.

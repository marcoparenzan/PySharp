# AMQP / RabbitMQ — scenario 7 — a step-by-step plan

**Goal.** Get a real, unmodified `pika` (PyPI's pure-Python AMQP 0-9-1 client) running a real
publish/subscribe round trip under PySharp, following the same scenario-driven method as
everywhere else: real script, real gap, real fix, real test, repeat. See ROADMAP.md's "Method:
scenario-driven development".

**Status: ✅ done (2026-08-11).** `import pika` works; a real `pika.BlockingConnection` publisher
and subscriber exchange 3 messages over a real loopback TCP socket against a hand-rolled real AMQP
0-9-1 broker. Full blow-by-blow below.

---

## Key decision: hand-roll the broker too, the same way scenario 6 did for MQTT

No real, publicly reachable AMQP test broker exists the way `test.mosquitto.org` does for MQTT
(scenario 5), and no Docker/local RabbitMQ instance was available in this environment. Rather than
block scenario 7 on infrastructure that isn't available, this followed the exact strategy that
already worked for scenario 6 (MQTT broker): hand-roll the *server* side directly on this
project's own `socket`/`asyncio`/`struct`/`threading` (the same async-socket-server pattern
already proven by `asgi_server.py`), and drive it with a **real, unmodified** client library from
PyPI — so the client side is genuinely untouched, unmodified real-world code, and only the server
side (which nothing downstream depends on being "real RabbitMQ", just real AMQP 0-9-1 wire
protocol) is hand-written.

The broker ([amqp_broker_demo.py](samples/amqp_broker_demo.py)) implements real frame
framing (type/channel/size/payload/0xCE frame-end), the real Connection.Start/Start-Ok/Tune/
Tune-Ok/Open/Open-Ok negotiation, real Channel.Open, real Queue.Declare, real Basic.Consume/
Cancel, real Basic.Publish (method frame + content-header frame + content-body frame(s)), real
Basic.Deliver fan-out, and a real Channel.Close/Connection.Close shutdown handshake — a practical
v1 subset (documented in the module's own docstring), not the full AMQP 0-9-1 spec surface
(no exchange types/bindings, no QoS-2-equivalent multi-ack bookkeeping, no real SASL auth).

## Verification method

No local Python interpreter is available, so every fix below was verified by running the real
script and reading the actual traceback PySharp produced, then reasoning through real AMQP 0-9-1
protocol semantics (a well-documented, stable wire protocol) and real CPython stdlib semantics —
the same "run it, see what breaks, fix, repeat" loop used for every other scenario. One hang (not
a crash) was diagnosed via a scratch probe script with added print/flush checkpoints around each
step of `pika.BlockingConnection.close()`, isolating exactly which call blocked forever.

---

## What was found and fixed

### Two new modules real `pika` imports unconditionally at load time
- **`ast`** didn't exist — `pika/connection.py` does `import ast` at module scope (for
  `ast.literal_eval`, used to parse dict/tuple-shaped values out of AMQP URL query-string
  parameters — a path this demo's `pika.ConnectionParameters(...)` usage never actually
  exercises, but the bare `import ast` still needs a real module). Implemented as a genuine
  recursive walk of this project's own parser output (`Parser.ParseExpression`, the same one
  behind the `eval()` builtin) — never actually executing anything, so `ast.literal_eval("os.system(...)")`
  correctly raises `ValueError` rather than running it, matching real CPython's actual safety
  guarantee (not just its happy path).
- **`numbers`** didn't exist — `pika/connection.py` validates `ConnectionParameters` fields
  (`port`, `channel_max`, ...) with `isinstance(value, numbers.Integral)`/`numbers.Real`.
  Implemented as the real ABC numeric tower (`Number > Complex > Real > Rational > Integral`, real
  nominal `PyClass` inheritance so `issubclass()` works too), with `int`/`bool`/`float` recognized
  against it via the same duck-typed-ABC mechanism `collections.abc.Iterable`/`Set`/`Coroutine`
  already use (`Builtins.SatisfiesAbcByDuckType`).

### A third new module: `heapq`
- **`heapq` didn't exist** — `pika/adapters/select_connection.py` keeps its connection-timeout
  queue in a real min-heap (`heapq.heappush`/`heappop`/`heapify`). Implemented as a direct port of
  real CPython's own `Lib/heapq.py` sift-up/sift-down algorithm, using `interp.Compare` for `<` so
  heap elements can be arbitrary `__lt__`-comparable objects, not just numbers — element ordering
  matches real CPython exactly, not just "a min-heap of some kind."

### Real, general interpreter bugs (not new-module gaps — pre-existing, just never exercised)
- **`ABCMeta(name, bases, namespace)` raised `TypeError: ABCMeta() takes no arguments`.** Real
  CPython: `ABCMeta` genuinely *is* a subclass of `type`, so calling it with the same 3-arg shape
  `type(...)` accepts dynamically builds a class the same way. Found via real pika's own
  `compat.py`: `AbstractBase = abc.ABCMeta('AbstractBase', (object,), {})`. Fixed by special-casing
  `cls == AbcModule.AbcMetaClass` in `Interp.Call`'s `PyClass` branch, delegating to the same
  `TypeConstructorMethods.BuildClass` the `type(...)` builtin itself uses.
- **`collections.defaultdict` was missing `__delitem__`/`clear`/`pop`/`popitem`/`setdefault`/
  `update`** — only `__getitem__`/`__setitem__`/`__contains__`/`__len__`/`__iter__`/`keys`/
  `values`/`items`/`get` existed. Found via real pika's own connection-teardown path (`del
  self._fd_events[...]` inside `select_connection.py`).
- **`bytes.split()` (no separator) didn't exist** — only the explicit-separator form did. Real
  CPython's no-arg form splits on runs of ASCII whitespace, discarding empty pieces (the same
  "whitespace-run" mode `str.split()` already had). Found via real pika's own `credentials.py`
  (`as_bytes(start.mechanisms).split()`, splitting the real space-separated SASL mechanism list
  straight off the wire) — masked the *real* underlying connection error until fixed, since it
  fired inside the error-reporting path itself.
- **`OSError`-family exceptions never actually carried real `.errno`/`.strerror`/`.filename`
  attributes** — the values were folded into `.args` (or, worse, pre-formatted into a single
  string) but never set as real instance attributes, since `PyErr.MakeInstance` (the fast, generic
  exception constructor used everywhere) deliberately never runs `__init__` (a design established
  earlier this project, around a `JSONDecodeError` construction bug — see HTTP_PLAN.md). Found via
  real pika's own `io_services_utils.py` reading `caught_exc.errno` off a real `BlockingIOError`
  raised from a non-blocking `connect()` in progress. Fixed with:
  - a new `PyErr.MakeOSError(cls, errno, strerror, filename=None)` helper, used at every real
    errno-carrying construction site (`SocketModule.Translate`, `os.mkdir`'s `FileExistsError`,
    `subprocess`'s `FileNotFoundError` on a failed spawn);
  - a general guarantee that *every* OSError instance has real (if `None`-defaulted)
    `.errno`/`.strerror`/`.filename`/`.filename2`/`.winerror` attributes regardless of how it was
    built, mirroring the existing `__cause__`/`__context__`/`__traceback__` "None by default"
    fallback already in `Interp.TryGetAttr`;
  - a real `OSError.__str__` override: `"[Errno N] strerror"` (plus `": 'filename'"` when a
    filename is set) when a real errno/strerror pair exists, falling back to the ordinary
    args-based formatting otherwise — matching real CPython's own familiar
    `"[Errno 2] No such file or directory: 'foo.txt'"` shape.
- **`socket.getaddrinfo()` only accepted positional arguments** — real pika calls it entirely by
  keyword (`socket.getaddrinfo(host=..., port=..., family=..., ...)`), which previously hit the
  interpreter's generic "missing required argument" catch-all. Fixed to accept both forms.
- **`select.select()` only recognized socket objects, never raw integer file descriptors.** Real
  CPython's `select.select()` accepts either. Real pika's own `SelectPoller`
  (`select_connection.py`) tracks connections purely by `fileno()` int and calls
  `select.select(fd_list, ...)` directly with those ints — previously every such call silently saw
  an empty selectable set (since the raw ints didn't match the socket-object-only lookup) and
  always timed out the full requested duration, surfacing as a spurious "TCP connection attempt
  timed out" even though the real underlying connect succeeded almost instantly on loopback. Fixed
  by resolving a raw fd back to its real `Socket` via the same fd registry `fileno()`/asyncio's
  `add_reader`/`add_writer` already use (`SocketModule.TryResolveHandle`).
- **`socket.getsockopt()` didn't exist at all.** The classic post-nonblocking-connect
  `getsockopt(SOL_SOCKET, SO_ERROR)` check (how real code learns whether a `connect()` actually
  succeeded once `select()`/`poll()` reports the fd writable) raised `AttributeError`. Fixed via
  real .NET `Socket.GetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Error)`.

### One bug in the demo script itself (not the interpreter)
- **`pika.BlockingConnection.close()` hung forever.** Real pika's `BlockingChannel.close()`
  cancels every active consumer first — sending a real `Basic.Cancel` and blocking (no timeout)
  until it sees `Basic.Cancel-Ok` — *before* it proceeds to `Channel.Close`. The broker's first cut
  had no handler for `Basic.Cancel` at all (silently ignored, no reply ever sent), so `close()`
  blocked indefinitely. Diagnosed by isolating the hang to `sub_conn.close()` via a scratch probe
  script with checkpoints, then reading `blocking_connection.py`'s own `_cancel_all_consumers()`.
  Fixed by adding real `Basic.Cancel`/`Basic.Cancel-Ok` handling to the broker (not an interpreter
  fix — the interpreter faithfully ran exactly what the script told it to; the script was
  incomplete).

**Deliberately out of scope for v1** (practical-subset philosophy, matching every other module in
this project): real SASL auth (any PLAIN credentials accepted), exchange types/bindings (default
exchange / direct-to-queue only), server-generated queue names, more than one consumer per queue,
QoS-2-style multi-message `Basic.Ack(multiple=True)` bookkeeping, real heartbeats (negotiated to 0
— disabled — since this is a short-lived demo), and any real `BasicProperties` field on a
published message (property-flags is asserted to be 0, true for every call this demo makes and for
pika's own default `properties=None`; a real nonzero property-flags raises a clear
`NotImplementedError` rather than silently misparsing).

---

## Deliverables

- **Sample**: [samples/amqp_broker_demo.py](samples/amqp_broker_demo.py) — a real, hand-rolled
  AMQP 0-9-1 broker (`Broker` class, ~230 lines) plus a `main()` driving two real
  `pika.BlockingConnection` clients (publisher + subscriber) over a real loopback TCP socket.
- **Modules**: [AstModule.cs](src/PySharpLib/Modules/AstModule.cs) (new),
  [NumbersModule.cs](src/PySharpLib/Modules/NumbersModule.cs) (new),
  [HeapqModule.cs](src/PySharpLib/Modules/HeapqModule.cs) (new); real fixes spread across
  `Interp.cs` (`ABCMeta` 3-arg call, OSError `.errno`/`.strerror`/etc. "None by default"
  fallback), `PyErr.cs` (`MakeOSError`, `OSError.__str__`), `CollectionsModule.cs` (`defaultdict`
  mixin methods), `TypeMethods.cs` (`bytes.split()` no-arg form), `SocketModule.cs`
  (`getaddrinfo()` keyword args, `getsockopt()`, `Translate` using `MakeOSError`),
  `SelectModule.cs` (raw fd support), `OsModule.cs`/`SubprocessModule.cs` (also switched to
  `MakeOSError` for consistency), `Builtins.cs` (`numbers` tower duck-typing cases).
- **Tests**: [AmqpBrokerSampleTests.cs](src/PySharp.Tests/M21_AmqpBroker/AmqpBrokerSampleTests.cs)
  (3 tests, including the full real pub/sub round trip) and
  [AmqpInterpreterFixesTests.cs](src/PySharp.Tests/M21_AmqpBroker/AmqpInterpreterFixesTests.cs)
  (9 tests, one per interpreter-level fix) — 12 new tests total, all local/no network.
- Full suite green at **1095/1095**, confirmed via 5 consecutive full-suite runs (touching
  shared/core interpreter code — `Interp.Call`, `TryGetAttr`'s OSError fallback, `bytes.split` —
  warranted more than the usual handful).

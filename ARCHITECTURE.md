# PySharp interpreter architecture

This document describes how the interpreter is built and records the design decisions made during
development, with the *why* of each — so they can be revisited when the library is extracted as a
standalone component.

---

## 1. Overview

PySharp is a classic **tree-walking interpreter**, organized in the canonical pipeline:

```text
source ──► Lexer ──► tokens ──► Parser ──► AST ──► Interp (tree-walking) ──► effects/values
                                  │                     │
                                  │                     ├─ Runtime object model
                                  │                     ├─ Builtins (print, len, …)
                                  │                     └─ Importer ──► stdlib Modules (C#)
                                  └─ AstDumper (for tests)
```

Public facade: **`PyEngine`** builds the complete environment (builtins + interpreter + import
system) and exposes `Run(source)` and `CaptureOutput(source)`.

Python values are mapped to native C# types where possible (see §4): **never C# `null` as a Python
value** — absence is always `PyNone.Instance`.

---

## 2. Lexer (`Lexing/`)

- Normalizes line endings, handles **INDENT/DEDENT** with an indentation stack (tab = 8), implicit
  continuation inside parentheses and explicit continuation with a backslash.
- Recognizes numbers (int/hex/oct/bin/float/exp), strings with all prefixes (`r`, `b`, `f`, `u` and
  combinations), triple-quoted strings, and byte literals with escape decoding.
- **f-strings** are emitted as tokens with their raw content; parsing of `{expr}`, conversions and
  format specs happens in the parser (where `ParseExpression` can be reused).

**Decision**: INDENT/DEDENT computed in the lexer (not the parser) — it simplifies the parser, which
sees a token stream already structured into blocks, as in CPython.

---

## 3. Parser (`Parsing/`)

- **Recursive descent** with full operator precedences, ternary, lambda, comprehensions
  (list/set/dict/generator), slices, `*`/`**` unpacking, the walrus `:=`, `yield`/`yield from`.
- Full statements: assignments (multiple, augmented, annotated), `if/while/for/try/with/def/
  class/return/raise/import/global/nonlocal/del/assert/pass/break/continue`, decorators.
- **Annotations** are parsed but not evaluated eagerly (consistent with `from __future__ import
  annotations`, the modern default; they are evaluated lazily on access via `__annotations__`).
- `AstDumper` serializes the AST into compact s-expressions: it is the backbone of the parser tests
  (M2), which compare the dump against an expected string instead of inspecting objects.

**Decision**: generator detection at parse time (`FuncDef.IsGenerator`) via a visit that looks for
`yield` without descending into nested functions — avoids runtime ambiguity.

**Decision**: `{expr=}` (self-documenting) f-strings are expanded in the parser into
`literal-text + value` with default `repr` conversion — no special runtime support.

---

## 4. Object model (`Runtime/`)

Python value → C# mapping:

| Python | C# |
|---|---|
| `None` / `NotImplemented` / `Ellipsis` | singletons `PyNone` / `PyNotImplemented` / `PyEllipsis` |
| `bool` | `System.Boolean` |
| `int` | `System.Numerics.BigInteger` (arbitrary precision) |
| `float` | `System.Double` |
| `str` | `System.String` |
| `bytes` / `bytearray` | `PyBytes` (immutable, hashable) / `PyByteArray` |
| `tuple` / `list` | `PyTuple` / `PyList` |
| `dict` | `PyDict` (insertion order, keys with Python semantics) |
| `set` / `frozenset` | `PySet` / `PyFrozenSet` |
| `range` / `slice` | `PyRange` / `PySlice` |
| functions / classes / instances | `PyFunction`/`PyBuiltinFunction`, `PyClass`, `PyInstance` |
| exceptions | `PyInstance` of a class deriving from `BaseException` |

**Decision — `int` = BigInteger**: Python semantics require unbounded integers. The cost is carefully
handling conversions to `int`/`double` (see robustness, §9).

**Decision — unified equality/hash**: `PyEqualityComparer` implements Python semantics
(`1 == 1.0 == True`, containers by value) and is used wherever keys/members are needed, so `dict` and
`set` behave as in Python.

**Decision — `PyDict` with insertion order**: a `LinkedList` of entries + a `Dictionary` index, to
preserve order (guaranteed by the language since 3.7) without losing O(1) access.

### Classes and MRO

`PyClass` computes the **C3 linearization** of the MRO at construction. Attribute lookup follows the
MRO; method binding (functions → bound method, static/classmethod, property) happens in
`Interp.TryGetAttr`. `super()` is supported both in the explicit two-argument form and the
**zero-arg** form inside a method, thanks to `PyFunction.DefiningClass` and the frame stack.

---

## 5. Interpreter (`Interpretation/Interp.cs`)

The heart of the system (~2100 lines). Highlights:

- **Lexical environment** (`Env`): local variables with a chain toward enclosing scopes; **LEGB**
  resolution (local → enclosing → globals → builtins). `global`/`nonlocal` declarations are honored
  both on writes and **on reads** (see §9, fixed bug).
- **Control flow via C# exceptions**: `BreakSignal`, `ContinueSignal`, `ReturnSignal`, and `PyRaise`
  (which carries the Python exception instance). It is the natural idiom for a tree-walker and keeps
  the code linear.
- **Protocols**: `__iter__/__next__`, `__enter__/__exit__`, `__getitem__/__setitem__`, `__call__`,
  operators (`__add__`/`__radd__`, comparisons, etc.), `__bool__`/`__len__`.
- **Operators**: `BinaryOp`/`UnaryOp` handle builtin types first, then delegate to the dunders on
  instances (with reflected operator and `NotImplemented`).

**Decision — class scope is excluded from closures** (`Env.IsClassScope`): as in Python, a function
defined in a class does not "see" the class-body names as free variables. Without this, a `Client.socket`
method would shadow the imported `socket` module — a real bug that surfaced with paho.

---

## 6. Generators (`Runtime/PyGenerator.cs`)

**Decision — generators on a dedicated thread with a semaphore handshake**. Each generator runs its
body on a background thread; two `SemaphoreSlim` objects (`resume`/`produced`) implement the
producer/consumer protocol. `yield v` suspends the generator's thread and unblocks the consumer.

Trade-off: it is more expensive than a state machine, but for a tree-walker it is by far the simplest
and most correct way to suspend/resume execution in the middle of an arbitrary expression, without
rewriting the interpreter in CPS. `generator.send(value)` with a non-None value is not supported in
v1.

---

## 6b. Coroutines and asyncio (`Runtime/Async.cs`, `Modules/AsyncioModule.cs`)

**Decision — reuse the generator suspension model for coroutines.** An `async def` produces a
`PyCoroutine`; like a generator it runs its body on a dedicated thread and suspends at every `await`
on a pending `PyFuture` through the same `resume`/`produced` semaphore handshake. Only one coroutine
runs at any instant (the driver blocks while a step runs; the coroutine blocks while suspended), so
Python objects are never touched concurrently — cooperative single-threading, like CPython. `await`
of a coroutine **delegates** (à la `yield from`); exceptions propagate across `await`.

The `asyncio` event loop (`PyEventLoop`) is a ready queue + a timer heap (`call_later`/`sleep`) + a
semaphore for cross-thread wake-ups. Blocking I/O (`sock_accept`/`sock_recv`/`sock_sendall`) is
offloaded to the thread pool and rejoined via `call_soon`, so the loop thread and coroutine threads
never race. `PyTask` drives a coroutine, re-scheduling itself on the future it is awaiting. Trade-off:
one thread per live coroutine (same as generators) — simple and correct, not the cheapest.

---

## 7. Import system (`Importing/Importer.cs`)

Resolution order: **C# builtin modules** → paths in `SearchPaths` (`sys.path`) → wheels extracted
into `site-packages`. Cache in `Modules` (≈ `sys.modules`), registered *before* the module runs to
allow circular imports. Supports packages with `__init__.py`, relative imports (`from .base import
X`), `import a.b.c as d`, `from pkg import *` (with `__all__`).

---

## 8. .NET-backed stdlib (`Modules/`)

The standard modules are implemented in C# and registered in `StdlibModules.RegisterAll`. They are
driven by the real imports of paho-mqtt and the samples:

| Category | Modules |
|---|---|
| System | `sys`, `os`, `time`, `platform`, `errno`, `io`, `warnings`, `copy` |
| Network/TLS | `socket` (→ `System.Net.Sockets`), `ssl` (→ `SslStream` + `X509Certificate2`), `select` |
| Concurrency | `threading` (Thread, Lock/RLock, Condition, Event, Timer) |
| Binary data | `struct` (pack/unpack with endianness) |
| Cryptography | `hashlib`, `hmac`, `base64` (for SAS tokens) |
| Text/URL | `string`, `urllib.parse`, `json` (custom, `System.Text.Json`-like), `yaml` (PyYAML subset) |
| Collections | `collections` (deque, OrderedDict, defaultdict, namedtuple), `enum`, `functools` |
| Numeric | `math` |
| Interop | `ctypes` (see §10) |
| Stubs | `typing`, `dataclasses`, `__future__` |

**Decision — `socket`/`ssl`/`select` cooperate**: `SockWrap` wraps the .NET `Socket`; `ssl.wrap_socket`
builds an `SslStream` on top and buffers decrypted data to emulate the non-blocking/`pending`
semantics paho expects; `select` can distinguish `SSLSocket`s with already-buffered data (ready
immediately) from raw sockets (via `Socket.Select`).

**Decision — `enum` with interpreter support**: classes deriving from `Enum`/`IntEnum` are
transformed in `Interp.ExecClassDef` (value attributes → singleton members with `name`/`value`);
lookup-by-value (`Rc(4)`) is handled in `Interp.Instantiate`. `IntEnum` members are coercible to
integer (via `Dict["value"]` in `PyOps.AsBigInt`), which `struct.pack` relies on.

---

## 9. Robustness (bugs fixed with the conformance corpus)

The RustPython corpus surfaced, besides feature gaps, some robustness issues that were fixed because
they could have **crashed the host process**:

1. **Recursion in `repr`**: a list containing itself sent `PyOps.Repr` into StackOverflow. Added a
   `[ThreadStatic]` guard that prints `[...]`/`{...}`/`(...)`.
2. **`BigInteger → int` overflow**: slices like `a[:2**100]` and powers like `2**(10**1000)` caused
   `OverflowException`. `PySlice.Indices` now **saturates** to the `int` range; `**` has shortcuts
   for base `-1/0/1` and raises Python `OverflowError` for huge exponents.
3. **`global`/`nonlocal` on reads**: `Env.TryGet` ignored the declarations and a nested `global a`
   read an enclosing local instead of the module global.
4. **`with` + `return`/`break`**: `__exit__` was not called if the body exited with a control signal
   — a mutex deadlock in paho.

**Decision — safety net on builtins**: `Interp.Call` converts `IndexOutOfRange`/`InvalidCast` coming
from a builtin into a Python `TypeError`, so a builtin invoked with the wrong number/type of
arguments produces a clean Python error instead of a CLR exception.

---

## 10. ctypes-lite (`Modules/CtypesModule.cs`)

`CDLL("kernel32")` loads the library with `NativeLibrary`; exported functions become callable. For
the call, a **`calli` thunk** is generated at runtime with `DynamicMethod` + `EmitCalli`, with
marshalling of scalar types and strings (`c_char_p`, `c_wchar_p`). Thunks are cached per signature.
The `c_int`/`c_double`/`c_char_p`/… types and `restype`/`argtypes` are supported; struct-by-value and
callbacks are out of scope for v1.

This covers the requirement "if there is a native DLL, the program must be able to run it": tested on
`kernel32.GetTickCount64` and `msvcrt.strlen/abs/pow`.

---

## 11. Mini-pip (`PipSharpLib/`)

`PackageInstaller` (namespace `PipSharpLib`) queries the **PyPI JSON API**, selects the `py3-none-any`
wheel (pure Python only), downloads it, **verifies the sha256**, and extracts it into `site-packages`
with path-traversal checking. No dependency resolution in v1 (paho-mqtt has no mandatory runtime
dependencies for the scenario).

---

## 12. Testing

- **Incremental per-milestone tests** (M1–M9): every syntax/runtime feature has its own tests; a
  milestone closes only with green tests.
- **Conformance corpus**: 85 RustPython snippets run by `CorpusTests.cs`, split into *Supported*
  (must pass) and *Xfail* (out-of-scope-v1 features, must still fail — if one starts passing, the red
  test reminds you to promote it).
- **Real E2E**: the M9 tests verify the sample without network (SAS token compared against the C#
  HMAC); the full run was executed against a real Azure IoT Hub (D2C, C2D, twin, desired).

Total: **547 green tests**.

---

## 12b. .NET interop for embedding (`Runtime/Clr.cs`)

**Decision — foreign .NET values are wrapped, not reflected in place.** A host injects objects with
`PyEngine.SetVariable(name, obj)`; on `Run` they are marshalled into the `__main__` globals. Any value
that is not already a Python-native type becomes a `ClrObject` (instance) or `ClrType` (a `System.Type`,
for statics/construction); a method access yields a `ClrMethod` group. Explicit wrappers keep the
interpreter from ever reflecting over its own runtime types by accident.

The interpreter's dispatch points (`TryGetAttr`/`SetAttr`/`Call`/`GetItem`/`SetItem`, and
`PyOps.GetIter`) recognise these wrappers and delegate to `ClrBinder`, which uses reflection for
member access, **overload resolution** (by arity + marshalled argument types), construction, indexers
and `IEnumerable` iteration. `ClrMarshal` converts both ways (Python `int`↔`BigInteger`/`int`/…,
`float`↔`double`, `str`↔`string`, `None`↔`null`, `list`↔arrays/`List<T>`). Out of scope for v1:
`ref`/`out` params, generic-method inference, events, and Python callables as .NET delegates.

---

## 12c. Tracebacks and the trace hook (`Runtime/Frames.cs`)

**Decision — a real call stack with per-frame lines, captured lazily on unwind.** The interpreter
keeps a per-thread stack of `Frame`s (a `<module>` frame plus one per function call); `Exec` updates
the top frame's current line at each statement. When a `PyRaise` unwinds, each frame it passes through
records a `PyFrameInfo` (function, file, line, and the live `Env` for variable inspection) into
`ex.Traceback` — **innermost first**. `StopIteration`/`StopAsyncIteration` are skipped so iteration
stays cheap. `PyErr.FormatTraceback` renders the CPython-shaped string; the console host prints it.

For live observation, `Interp.Trace` (an `Action<TraceEvent>`) is invoked on each line, call, return
and unwinding exception. It runs synchronously on the interpreter thread — a debugger can block inside
it for breakpoints/stepping — and is null by default (zero overhead). This is the intended foundation
for a VS Code Debug Adapter: Line → step/breakpoint, `Scope` → Variables pane, `Traceback` → Call
Stack pane. Cross-thread note: generators/coroutines run on their own threads, so their frames form a
separate per-thread stack (a traceback does not stitch across the thread boundary in v1).

---

## 13. Known architectural limits (v1)

- Tree-walking, not bytecode: simplicity/debuggability favored over performance.
- Builtin types (`int`, `str`, …) do not expose dunder methods as attributes of the *type*
  (`int.__eq__` is unreachable), because there is no real type object behind each value.
- No `match`, complex numbers, custom metaclasses (beyond what `enum` needs), `exec()`/`eval()`.
  (`async`/`await` and an `asyncio` loop **are** now supported — see §6b.)
- Coroutines and generators use one background thread each (§6, §6b): correct but not the cheapest.

These limits are the natural starting point for future work (see `TODO.md`).

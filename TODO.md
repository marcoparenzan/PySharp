# TODO — PySharp

Open work, grouped by type. Status reflects v1.0.0 (2026-07-24).

---

## Recommended next steps

- [ ] **`.gitignore` + init repo**: exclude `config.json` (contains a device SharedAccessKey),
      `bin/`, `obj/`, `site-packages/`. Then `git init`.
- [ ] **End-to-end X.509 auth test**: register a self-signed-certificate device (thumbprint on IoT
      Hub), set `config.json` with `auth: "x509"` + cert/key paths, run the sample. The
      `ssl.load_cert_chain` code is ready and unit-tested.
- [ ] **Extract PySharpLib as a standalone NuGet library** (goal declared by the project): PySharpLib
      already has no IoT dependencies; verify that packaging pulls in nothing from the `PySharp` host
      or `PipSharpLib`, and publish `PySharpLib` as an independent package.

---

## Language features (out of scope for v1)

These are also tracked in the `Xfail` dictionary of `PySharp.Tests/Corpus/CorpusTests.cs`, which
verifies that the related snippets *keep failing* until they are implemented.

- [ ] **Dunder methods as attributes of builtin types** (`int.__eq__`, `str.__eq__`, `range.__eq__`,
      `dict.__or__`, `type(None).__dict__`, `wrapper_descriptor`). Requires a real type object behind
      each builtin value. *Impact: builtin_int/str/set/none/dict/range, operator_comparison,
      recursion.*
- [ ] **map/filter/enumerate/zip as types** rather than functions (`type(map(...)) == map`). Requires
      dedicated iterator classes. *Impact: builtin_map/filter/enumerate.*
- [ ] **Complex numbers** (`1j`, `complex`, `complex.__pow__`). *Impact: builtin_slice, syntax_slice,
      builtin_pow.*
- [ ] **`exec()` / `eval()` / `compile()`**. *Impact: syntax_function_args, syntax_global_nonlocal
      (only the SyntaxError test part).*
- [ ] **Exception groups `except*`** (PEP 654, 3.11). *Impact: builtin_exceptions.*
- [ ] **`generator.send(value)`** with a non-None value, `throw`, `close` with full semantics.
      *Impact: syntax_generator.*
- [x] ~~**`async`/`await`**~~ — done (coroutines + `asyncio`, see RELEASE_NOTES). Still open:
      **async generators** (`yield` in `async def`), **async comprehensions**, and asyncio
      synchronization primitives (`Lock`/`Event`/`Queue`/`Semaphore`).
- [ ] **`match`/`case`** (structural pattern matching).
- [ ] **Custom metaclasses** beyond what `enum` needs.
- [ ] **Deep protocols**: `__index__`, `__trunc__`, `__instancecheck__`, the pickle protocol
      (`__setstate__`). *Impact: stdlib_math, builtin_isinstance, protocol_iternext.*
- [ ] **Modular inverse** `pow(a, -1, m)` and complex `pow` cases.

---

## Correctness / details (minor improvements)

- [ ] **Fine-grained float semantics**: signed zero in `divmod`, specific `OverflowError` cases on
      floats, big-int/float division at very high precision. *Impact: builtin_divmod, builtin_float,
      operator_div, operator_arithmetic.*
- [ ] **No double evaluation of `__bool__`** in `if a or b` / `while` (CPython's compiler test-jump
      optimization; hard for a tree-walker without special-casing if/while with a BoolOp condition).
      *Impact: syntax_short_circuit_bool.*
- [ ] **`UnboundLocalError`** distinct from `NameError` for a local variable used before assignment /
      after `del`. *Impact: syntax_del, syntax_assignment.*
- [ ] **Introspection**: real `__module__`/`__qualname__` on user classes, `property` introspection
      (fget/fset), `partial.__dict__`. *Impact: syntax_class, builtin_property, stdlib_functools.*
- [ ] **`ord(bytearray of length 1)`** and exact `chr`/`ord` error messages.
- [ ] **stdlib**: `string.Template`, `struct` details (error cases), full `struct_time`/`strftime`,
      `json` over a file-like with encoding, advanced `deque`.

---

## Stdlib to expand (need-driven)

Modules not yet present that future Python packages might need:

- [ ] `itertools` (used by some snippets and common in libraries).
- [ ] `datetime` (beyond `time`).
- [ ] `re` (regular expressions) — large, to be evaluated.
- [ ] `abc` (abstract base classes), `weakref`, `array`, `binascii`, `zlib`.

---

## Interpreter / performance (v2 evolution)

- [ ] **Pre-computed name resolution** (indexed local slots instead of per-scope dictionaries) to
      reduce the overhead of every variable access.
- [ ] **Internal bytecode compilation** (from tree-walking to a VM) if performance becomes a
      requirement.
- [ ] **Thread-less `generator`**: evaluate a C# state machine/coroutine to remove the per-generator
      thread cost.

---

## Mini-pip

- [ ] **Dependency resolution** (currently installs only the requested package).
- [ ] Support for more wheel tags beyond `py3-none-any` when compatible (e.g. `py2.py3-none-any` is
      already handled).
- [ ] `uninstall` / `list` / reading the `RECORD` for clean uninstallation.

---

## Tooling / DX

- [x] ~~Improve the host **tracebacks**~~ — done: `PyRaise.Traceback` (file/line/function + per-frame
      locals), `PyErr.FormatTraceback`, and the console host prints the full CPython-shaped stack.
- [x] ~~Execution **trace hook**~~ — done: `Interp.Trace` (Line/Call/Return/Exception events) for
      host observation; the basis for a debugger.
- [ ] **VS Code debugger (Debug Adapter Protocol)** built on `Interp.Trace` + `PyRaise.Traceback`:
      breakpoints/stepping via the Line event (block inside the hook), Variables pane from
      `TraceEvent.Scope`, Call Stack from the traceback. Needs a small DAP server process and a
      `launch.json` type. Cross-thread frames (generators/coroutines) to be stitched for a unified stack.
- [ ] REPL: history, multiline, completion.
- [ ] **AOT/self-contained** publication of the host for distribution without the SDK.

---

## Embedding / .NET interop

Done: inject .NET objects/types with `PyEngine.SetVariable`, call methods (overload resolution),
read/write properties & fields, indexers, construction, `IEnumerable` iteration, delegate calls,
two-way marshalling (see [Clr.cs](src/PySharpLib/Runtime/Clr.cs), `M11_Interop`). Open:

- [ ] **Python callables → .NET delegates**: pass a Python `def`/`lambda` where a `Func<>`/`Action<>`
      is expected (the reverse callback direction — useful for host event handlers).
- [ ] **`ref`/`out` parameters** and **generic-method** type inference in overload binding.
- [ ] **Events**: `obj.SomeEvent += handler` from Python.
- [ ] **Named/optional arguments and `params`** in `ClrBinder` overload resolution.
- [ ] Marshal Python `dict` ↔ `IDictionary<,>`, and expose `IDisposable` via `with`.

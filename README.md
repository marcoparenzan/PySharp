# PySharp

A **Python 3.x interpreter written entirely from scratch in C# (.NET 10)** — no CPython embedding —
designed to be **embedded as a library in a .NET app** and to run "pure" Python code without dragging
along a native runtime.

Key features:

- native Python object model on top of C# types (`int` = `BigInteger`, `str`, `list`/`dict`/`set`,
  classes with C3 MRO, generators, exceptions, …) — see [ARCHITECTURE.md](ARCHITECTURE.md);
- **.NET-backed stdlib** (22 modules: `socket`, `ssl`, `threading`, `asyncio`, `struct`, `json`, `yaml`,
  `collections`, `functools`, `math`, `hashlib`/`hmac`/`base64`, `urllib.parse`, …);
- a **mini-pip** that downloads and extracts pure wheels (`py3-none-any`) from PyPI;
- a **`ctypes`** module to call native DLLs through .NET's `NativeLibrary`;
- a **`PyEngine`** embedding facade to host the interpreter inside a .NET application;
- a **`pysharp` command-line tool** installable globally (a .NET global tool).

Validated by a suite of **628 tests** (unit + RustPython conformance corpus).

> **Reference sample — Azure IoT Hub.** The project's historical proving ground is an Azure IoT Hub
> device on **paho-mqtt** (downloaded from PyPI): D2C telemetry, C2D, device twin, SAS and X.509 auth,
> tested **end-to-end against a real hub**. It is **one** use case, not the goal: see
> [samples/iothub_device_mqtt.py](samples/iothub_device_mqtt.py). Other scenarios (DB, web) are
> discussed in [Verified scenarios and limits](#verified-scenarios-and-limits).

---

## Why "from scratch" and not pythonnet/CSnakes

The choice is deliberate: **the interpreter itself is the product**, meant to become a standalone
.NET library. Hosting CPython (via pythonnet or CSnakes) would have been faster but would have turned
the project into a mere bridge to a native DLL. PySharp runs "pure" Python — enough for paho-mqtt
(pure Python) and the IoT Hub sample — and opens the door to embedded/AOT scenarios where carrying
CPython around is undesirable.

Language-completeness constraint adopted during the initial development:
*"everything and only what is needed to run paho-mqtt and the sample"*. Extending it to other
scenarios means adding the missing stdlib modules (see below), not rewriting the core.

---

## Solution structure

```text
src/PySharp.slnx
├── PySharpLib/              interpreter library (standalone, no IoT/external dependency)
│   ├── Lexing/              lexer with INDENT/DEDENT, f-strings, bytes
│   ├── Parsing/             recursive-descent parser + AST + dumper
│   ├── Runtime/             object model (PyInt/BigInteger, PyStr, PyList, PyDict, PyClass, …)
│   ├── Interpretation/      tree-walking interpreter (Interp.cs)
│   ├── Builtins/            print, len, range, isinstance, super, open, slice, …
│   ├── Importing/           import system (C# builtin → sys.path → site-packages)
│   ├── Modules/             .NET-backed stdlib (socket, ssl, threading, struct, json, yaml, …)
│   └── PyEngine.cs          public embedding facade (namespace PySharpLib)
├── PipSharpLib/             mini-pip: PyPI JSON API → download wheel → extraction (namespace PipSharpLib)
├── PySharp/                 console host (Spectre.Console.Cli): run / install / repl
└── PySharp.Tests/           xUnit — incremental tests + RustPython corpus

samples/                     iothub_device_mqtt.py + config.iothub_device_mqtt.json + more
```

Rough size: ~13,000 lines of C#, 21 stdlib modules, 85 corpus snippets.

---

## Requirements

- **.NET SDK 10** (the project targets `net10.0`).
- Internet connection for `install` (downloads wheels from PyPI).
- Windows for the `ctypes` module on system DLLs (kernel32/msvcrt); the rest is cross-platform.
- For the IoT Hub e2e: an Azure IoT Hub and a registered device (or its connection string).

---

## Quick start

```powershell
# from the src/ folder
dotnet build PySharp.slnx

# version
dotnet run --project PySharp -- --version

# run a Python script
dotnet run --project PySharp -- run my_script.py

# install a pure package from PyPI into ./site-packages
dotnet run --project PySharp -- install paho-mqtt==2.1.0

# REPL
dotnet run --project PySharp -- repl
```

Or, once installed as a global tool (see below), simply `pysharp run my_script.py`.

### Example: Azure IoT Hub

```powershell
# 1. install paho-mqtt
dotnet run --project PySharp -- install paho-mqtt==2.1.0

# 2. create config.json (see samples/config.iothub_device_mqtt.json) with the device connection string
#    { "auth": "sas", "connection_string": "HostName=...;DeviceId=...;SharedAccessKey=..." }

# 3. run the sample
dotnet run --project PySharp -- run ../samples/iothub_device_mqtt.py config.json
```

The sample runs, in order: TLS+SAS connection (8883) → twin GET → reported properties →
3 D2C telemetry messages → 30s listen for C2D and desired properties.

---

## Installing as a command-line tool (`pysharp` on PATH)

The console host is packaged as a **.NET global tool**, so you can use PySharp **as your Python** — a
`pysharp` command available everywhere. Its CLI is built with [Spectre.Console.Cli](https://spectreconsole.net/).

### Install

```powershell
# from the src/ folder: build the package and install the global `pysharp` command
dotnet pack PySharp/PySharp.csproj -c Release
dotnet tool install --global --add-source PySharp/bin/Release PySharp
```

The command lands in `~/.dotnet/tools` (already on PATH with the .NET SDK). From then on, from **any**
folder:

```powershell
pysharp run my_script.py            # run a script (extra args become sys.argv)
pysharp run my_script.py a b c      # sys.argv == ['my_script.py', 'a', 'b', 'c']
pysharp install paho-mqtt==2.1.0    # install a pure PyPI package into ./site-packages
pysharp repl                        # interactive REPL
pysharp --help                      # command help
pysharp --version                   # version
```

`run` automatically prepends the script's folder and `./site-packages` to `sys.path`.

### Update / uninstall

```powershell
# rebuild and re-publish the tool (bump <Version> in PySharp.csproj for distinct versions)
dotnet pack PySharp/PySharp.csproj -c Release
dotnet tool update --global --add-source PySharp/bin/Release PySharp

# remove it
dotnet tool uninstall --global PySharp
```

### Development loop: fix → rebuild → release

When you extend the interpreter and want to re-publish the tool, bump `<Version>` in
[PySharp.csproj](src/PySharp/PySharp.csproj), then `dotnet pack` + `dotnet tool update` as above.

### Using it in Visual Studio Code

VS Code is preconfigured with a task ([.vscode/tasks.json](.vscode/tasks.json)): **Terminal → Run
Task → "PySharp: run current file"** runs the open `.py` file with `pysharp` (bind it to a keyboard
shortcut for convenience). Alternatively, with the **Code Runner** extension, in your settings:

```jsonc
"code-runner.executorMap": { "python": "pysharp run" }
```

> **Honest note.** The official *Python* extension (ms-python) **cannot** use PySharp as its "Python
> interpreter": it expects a real CPython that it introspects (debugger, IntelliSense, `python -c ...`).
> PySharp is an *alternative* interpreter, not a drop-in for `python.exe`: use it as a **runner**
> (task / Code Runner / terminal), not as the backend of the Python extension.

---

## Host commands

| Command | Description |
|---|---|
| `run <file.py> [args…]` | run a script; `sys.argv` is populated with the arguments |
| `install <pkg[==ver]>` | install a pure wheel from PyPI into `./site-packages` |
| `repl` | interactive REPL (expression → value, statement → execution); **multi-line input** supported |
| `-v`, `--version` | print the version |
| `-h`, `--help` | print command help |

### REPL: multi-line input

The REPL accepts multi-line input like CPython's. It keeps reading (showing a `...` continuation
prompt) while the input is *incomplete* — an open triple-quoted string, unbalanced `()`/`[]`/`{}`, or
a trailing `\` continuation — and a **compound block** (`def`/`class`/`if`/`for`/`while`/`try`/`with`/
`async`/decorators) is finished with a **blank line**:

```text
>>> text = """first
... second"""
>>> print(text)
first
second
>>> def double(n):
...     return n * 2
...
>>> double(21)
42
>>> (1 +
...  2)
3
```

`exit`/`quit` (or Ctrl+Z) leaves the REPL; an empty line at the main prompt does nothing; Ctrl+Z while
composing a block abandons it.

---

## Embedding the engine in a .NET app

The interpreter is designed to be **embedded as a library** in any .NET application (console, service,
ASP.NET, WPF, …): the `PySharp` host is just one consumer among many. The public facade is the
[`PyEngine`](src/PySharpLib/PyEngine.cs) class in the `PySharpLib` namespace.

### 1. Reference the library

Add a reference to the **`PySharpLib`** project alone (the interpreter has no IoT or external
dependencies). If you also want to install pure PyPI packages at runtime, add **`PipSharpLib`**.

```xml
<ItemGroup>
  <ProjectReference Include="..\PySharpLib\PySharpLib.csproj" />
  <!-- optional, only if you use the mini-pip -->
  <ProjectReference Include="..\PipSharpLib\PipSharpLib.csproj" />
</ItemGroup>
```

Requirement: `net10.0`.

### 2. Run Python code

```csharp
using PySharpLib;

var engine = new PyEngine();                       // stdout → Console.Out
engine.Run("print('hello from the .NET host')");
```

### 3. Capture the output

Pass a `TextWriter` to the constructor (or assign `engine.Interp.Out`):

```csharp
using PySharpLib;

var sw = new StringWriter();
var engine = new PyEngine(sw);
engine.Run("for i in range(3): print(i)");
string output = sw.ToString();                     // "0\n1\n2\n"

// shortcut for one-shot / test usage:
string s = PyEngine.CaptureOutput("print(2 ** 10)"); // "1024\n"
```

### 4. Exchange data with the script

`Run` returns the `__main__` `PyModule`: read global variables from its `Dict` (values are native C#
types — `BigInteger`, `string`, `bool`, `double`, `PyList`, `PyDict`, …).

```csharp
using System.Numerics;
using PySharpLib;

var engine = new PyEngine();
var module = engine.Run("result = sum(range(101))");

if (module.Dict.TryGet("result", out var value))
    Console.WriteLine((BigInteger)value);          // 5050
```

You can also **call a Python-defined function from C#** through `Interp.Call`:

```csharp
var engine = new PyEngine();
var module = engine.Run("def greet(name): return f'hello {name}'");

module.Dict.TryGet("greet", out var fn);
var res = engine.Interp.Call(fn, new object[] { "Marco" });
Console.WriteLine((string)res);                    // "hello Marco"
```

To populate the `sys.argv` seen by the script, use `engine.Interp.Argv`.

### 5. Injecting .NET objects into the script (host interop)

Expose any **.NET object** (or a `System.Type`) to the script with `engine.SetVariable(name, obj)`.
Inside Python the value is used **idiomatically** — method calls, property/field access, indexing,
iteration and construction — with automatic marshalling between Python and .NET types (Python `int`
↔ `BigInteger`/`int`/`long`/…, `float` ↔ `double`, `str` ↔ `string`, `None` ↔ `null`, `list` ↔
arrays/`List<T>`; any other .NET object is wrapped transparently).

```csharp
using PySharpLib;

public sealed class Weather
{
    public string City { get; set; } = "Trento";
    public double TempC(int day) => 20.0 + day;          // a method with an argument
    public string[] Forecast => new[] { "sun", "rain" };  // a property to iterate
}

var engine = new PyEngine();
engine.SetVariable("weather", new Weather());             // inject an instance
engine.SetVariable("Math", typeof(System.Math));          // inject a type (statics + ctors)

engine.Run("""
    print(weather.City)              # -> Trento           (property read)
    weather.City = "Bolzano"         #                      (property write)
    print(weather.TempC(3))          # -> 23.0             (method call, int -> double)
    for f in weather.Forecast:       #                      (iterate a .NET string[])
        print(f)
    print(Math.Sqrt(144))            # -> 12.0             (static method on an injected type)
    """);
```

What works today: instance & static **methods** (with overload resolution by arity/type),
**properties** and **fields** (read/write), **indexers** (`obj[key]`), **constructors**
(`Type(args)` on an injected `Type`), **iteration** over any `IEnumerable`, and calling an injected
**delegate** (`Func<>`/`Action<>`) as a function. Values you inject are also readable back from
`module.Dict` after `Run`. Out of scope for now: `ref`/`out` parameters, generic-method type
inference, events, and passing Python functions as .NET delegates.

### 6. Imports, site-packages and mini-pip

Add folders to `sys.path` via `engine.Importer.SearchPaths`; from there the script can import `.py`
modules and extracted pure packages (e.g. `paho-mqtt`).

```csharp
using PipSharpLib;
using PySharpLib;

// (one-off) download a pure wheel from PyPI into ./site-packages
new PackageInstaller("site-packages").Install("paho-mqtt==2.1.0");

var engine = new PyEngine();
engine.Importer.SearchPaths.Add("site-packages");  // makes paho.mqtt importable
engine.Importer.SearchPaths.Insert(0, "scripts");  // your own modules folder
engine.Run("import paho.mqtt.client as mqtt");
```

### 6. Error handling, tracebacks and variable inspection

Python exceptions surface as **`PyRaise`** (namespace `PySharpLib.Runtime`); syntax errors as
**`PySyntaxError`** (namespace `PySharpLib`). A `PyRaise` carries a **traceback**: the call stack
captured as the exception unwound, so you know **where** the error happened and can **inspect the
variables** in scope at every level — essential when the interpreter is embedded in your app.

```csharp
using PySharpLib;
using PySharpLib.Runtime;

try
{
    engine.Run("""
        def level_two(x):
            return x / 0            # error here
        def level_one(n):
            return level_two(n)
        level_one(10)
        """, "script.py");
}
catch (PyRaise ex)
{
    // 1. A ready-made, CPython-shaped traceback string:
    Console.Error.WriteLine(PyErr.FormatTraceback(ex));
    //    Traceback (most recent call last):
    //      File "script.py", line 5, in <module>
    //      File "script.py", line 4, in level_one
    //      File "script.py", line 2, in level_two
    //    ZeroDivisionError: division by zero

    // 2. Or walk the frames yourself (innermost first) and inspect state:
    var innermost = ex.Traceback![0];
    Console.WriteLine($"{innermost.Function} @ {innermost.File}:{innermost.Line}");
    foreach (var kv in innermost.Locals().Entries)          // variables at the error site
        Console.WriteLine($"    {kv.Key} = {kv.Value}");
}
catch (PySyntaxError ex)
{
    Console.Error.WriteLine($"SyntaxError: {ex.Message} (line {ex.Line})");
}
```

Each `PyFrameInfo` exposes `Function`, `File`, `Line`, `IsModule`, `Locals()` (a `PyDict`; the module
globals for the top frame) and `Scope` (the live `Env`). `ex.Value` is the Python exception instance.

### 7. Observing execution live (the trace hook)

Set **`engine.Interp.Trace`** to watch execution as it happens — every line, function call/return and
unwinding exception. The callback runs **synchronously on the interpreter thread**, so a debugger can
block inside it to implement breakpoints and stepping. Left `null`, it costs nothing.

```csharp
using PySharpLib.Runtime;

engine.Interp.Trace = e =>
{
    switch (e.Kind)
    {
        case TraceEventKind.Line:
            Console.WriteLine($"→ {e.File}:{e.Line} ({e.Function})");
            // e.Scope.TryGet("x", out var x) → read a live variable here
            break;
        case TraceEventKind.Call:      Console.WriteLine($"call {e.Function}"); break;
        case TraceEventKind.Return:    Console.WriteLine($"ret  {e.Function}"); break;
        case TraceEventKind.Exception: Console.WriteLine($"exc  {e.Exception!.Class.Name}"); break;
    }
};
engine.Run("...");
```

This hook is the intended foundation for a **VS Code debugger** (Debug Adapter Protocol): a Line event
is a natural breakpoint check / step point, `e.Scope` backs the *Variables* pane, and `ex.Traceback`
backs the *Call Stack* pane. The adapter itself is not shipped yet — see [TODO.md](TODO.md).

> Threading note: generators, coroutines and each running task use a dedicated thread with a semaphore
> handshake (see [ARCHITECTURE.md](ARCHITECTURE.md) §6/§6b). The frame stack and trace events are
> per-thread, so a traceback that crosses into a generator/coroutine shows the frames of that thread;
> a single `PyEngine` instance is not meant to be shared across concurrent host threads — use one
> engine per unit of work.

---

## What the language supports

**Supported**: arbitrary-precision integers (BigInteger) and floats, strings/bytes/bytearray,
list/tuple/dict/set + comprehensions, f-strings (including `{expr=}`), functions (defaults, `*args`,
`**kwargs`, keyword-only, decorators, closures, `nonlocal`/`global`), classes (multiple inheritance
with C3 MRO, `super()`, dunders, properties, static/classmethod), exceptions
(`try/except/else/finally`, `raise from`), `with`, generators (`yield`, `yield from`),
**`async`/`await` with `async for`/`async with` (coroutines)**, an import system with packages,
`enum`, `NamedTuple`, function introspection (`__annotations__`, `__code__`).

**Out of scope for v1** (documented in [TODO.md](TODO.md) and in the `Xfail` dict of CorpusTests):
dunder methods exposed as attributes on builtin *types* (`int.__eq__`), complex numbers (`1j`),
`match`, `exec()`/`eval()`, exception groups (`except*`), `generator.send(value)`, async generators
(`yield` inside `async def`).

---

## Verified scenarios and limits

What the interpreter handles today, beyond the IoT/MQTT scenario, and what it does not. The outcomes
below are **verified by running probes** against the compiled interpreter. Progress is tracked
scenario by scenario in [ROADMAP.md](ROADMAP.md).

### Works

- **Language**: decorators, type hints (evaluated on access via `__annotations__`), classes,
  generators, exceptions, comprehensions, f-strings, real (simplified) custom metaclasses
  (`class X(Y, metaclass=M): ...` calls `M.__new__` for real).
- **Present stdlib modules**: `json`, `yaml`, `collections` (`Counter`/`ChainMap`/`deque`),
  `collections.abc`, `functools`, `enum`, `math`, `decimal`, `struct`, `socket`, `ssl`, `threading`,
  `asyncio` (incl. `Lock`/`Event`/`Semaphore`/`Queue`, `wait`, `add_reader`/`add_writer`,
  `run_in_executor`), `contextlib`, `dataclasses` (real field-driven `__init__`/`__repr__`/`__eq__`,
  `frozen=True`, `is_dataclass`), `hashlib`/`hmac`/`base64`, `urllib.parse`, `os`, `sys`, `time`, `io`,
  `string`, `types`, `re` (a real `System.Text.RegularExpressions`-backed engine), `datetime`,
  `ipaddress`, `pathlib`, `weakref`, `pickle`, `colorsys`, `itertools`, `operator`, `abc`, `inspect`
  (`signature`/`Signature`/`Parameter`); real (not stub) `typing` (`get_type_hints`, `Annotated`,
  generic-alias tracking).
- **Done scenarios**: Azure IoT Hub device (MQTT, sync **and async**), a sync FastAPI-shaped HTTP API,
  an **async FastAPI-shaped HTTP API on a real asyncio event loop**
  ([async_api.py](samples/async_api.py)), a **real, unmodified FastAPI app** — full CRUD, real
  pydantic v1 validation, WebSockets, graceful shutdown — served live over real HTTP entirely by
  PySharp ([samples/fastapi_demo.py](samples/fastapi_demo.py), scenario 2, see below), an MQTT
  subscribe round-trip, and YAML+JSON (de)serialization — see [ROADMAP.md](ROADMAP.md) and
  [samples/](samples/).
- **FastAPI (scenario 2, the roadmap's key scenario) is done**: real, unmodified `fastapi`/`starlette`/
  `anyio`/`pydantic` v1 (all from PyPI) run a real `FastAPI()` app end to end — routing, typed path/
  query params, pydantic request-body validation (incl. the real 422 error shape), `HTTPException`,
  WebSockets, and graceful shutdown (`signal.signal()`). Getting there required real (simplified)
  custom-metaclass support, real `__slots__`-backed per-instance storage, real `match`/`case`, real
  async generators, and a real recursion-depth guard — see [FASTAPI_PLAN.md](FASTAPI_PLAN.md) for the
  full probe-driven log.
- **Pure PyPI packages**: any `py3-none-any` wheel without compiled extensions (e.g. paho-mqtt,
  pydantic v1).
- **numpy**: a real C# shim (not the real numpy, which is a compiled C extension a from-scratch
  interpreter cannot load — see [NUMPY.md](NUMPY.md)) — construction, `float64`/`int64`/`bool`
  dtypes with real arithmetic promotion, indexing/slicing as real strided views, broadcasting,
  reductions, ufuncs, shape manipulation, basic linear algebra (`dot`/`matmul`/`@`, `np.linalg.norm`),
  `np.random`, and a two-way .NET array interop bridge. See [NUMPY_PLAN.md](NUMPY_PLAN.md)'s full
  12-phase plan (all phases done) and ROADMAP.md scenario 12.

### Does not work (yet)

| Scenario | Verified blocker | Feasibility |
|---|---|---|
| **Postgres** | `Npgsql` support exists in principle (same pattern as `pyodbc`/SQL Server) but is blocked on having a real Postgres server available to verify against | **Feasible, blocked on environment**: SQLite and SQL Server (via `pyodbc`/`Microsoft.Data.SqlClient`) both already work — see [ROADMAP.md](ROADMAP.md) scenario 3 and [SQL_PLAN.md](SQL_PLAN.md) |
| **Django** | a real, unmodified Django app needs WSGI, the ORM (real SQL generation + migrations, heavy metaclass use), the template engine, `django.contrib.admin`, class-based views | **Not started**: much heavier than FastAPI (scenario 2, now done) — see [ROADMAP.md](ROADMAP.md) scenario 10 |

`sqlite3` (scenario 3), `importlib`, and `email`/`http` (scenario 2, `FASTAPI_PLAN.md`) are no longer
missing — all three now exist, closing out gaps this section used to list.
Adding one means writing a module in [src/PySharpLib/Modules/](src/PySharpLib/Modules/) and
registering it in `StdlibModules.RegisterAll`.

### Running original PyPI packages

The `install` command downloads the **original** packages from PyPI, but "a PyPI package" does not
mean "any package": for one to *run*, **three conditions must hold at once**, which real packages
often violate (outcomes below verified by running the mini-pip):

| # | Constraint | What happens if it is missing |
|---|---|---|
| 1 | **Pure wheel** (`py3-none-any`, no C/Rust) | `install numpy` → rejected ("No pure-python wheel found"). Excludes pandas, psycopg2, cryptography, pydantic-core (pydantic **v2**), orjson, lxml, … — **pydantic v1** is pure Python and works |
| 2 | **Dependencies installed by hand** (the mini-pip does **not** resolve them) | `install pydantic==1.10.13` needs `install typing_extensions` done separately too; `install pydantic` (latest, v2) succeeds but its `pydantic-core` dependency can't be, since it isn't a pure wheel |
| 3 | **Transitive imports within the ~50 present stdlib modules** + syntax in the subset | `six` fails on its first line (`from __future__ import absolute_import`); `pydantic==1.10.13` now imports cleanly (see [FASTAPI_PLAN.md](FASTAPI_PLAN.md)) |

In practice a **pure, self-contained** package runs (or one whose dependencies are themselves pure)
as long as it stays within the implemented stdlib and language subset. **paho-mqtt works because it
was chosen as the design target** (pure, with no mandatory runtime dependencies, modules implemented
on purpose): it is the engineered exception, not the rule. To widen the perimeter you **add stdlib
modules**, you do not modify the core.

---

## Documentation

- [ROADMAP.md](ROADMAP.md) — distance from CPython and progress by scenarios (real scripts)
- [ARCHITECTURE.md](ARCHITECTURE.md) — architecture and log of the interpreter's design decisions
- [RELEASE_NOTES.md](RELEASE_NOTES.md) — milestone and version history
- [TODO.md](TODO.md) — open work and out-of-scope-v1 features

---

## Third-party licenses

- The test corpus in `PySharp.Tests/Corpus/snippets/` comes from
  [RustPython](https://github.com/RustPython/RustPython) (MIT license); see
  `PySharp.Tests/Corpus/RUSTPYTHON-LICENSE.txt`.
- `paho-mqtt` is downloaded from PyPI at runtime and is not included in the repository.

---

## Security note

The sample `config.json` contains a device **SharedAccessKey**: do NOT commit it. Add `config.json`
to `.gitignore` before initializing a repository.

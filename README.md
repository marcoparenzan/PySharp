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

Validated by a suite of **587 tests** (unit + RustPython conformance corpus).

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
| `repl` | interactive REPL (expression → value, statement → execution) |
| `-v`, `--version` | print the version |
| `-h`, `--help` | print command help |

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

### 6. Error handling

Python exceptions surface as **`PyRaise`** (namespace `PySharpLib.Runtime`); syntax errors as
**`PySyntaxError`** (namespace `PySharpLib`).

```csharp
using PySharpLib;
using PySharpLib.Runtime;

try
{
    engine.Run("raise ValueError('boom')");
}
catch (PyRaise ex)
{
    // ex.Value is the Python exception instance
    Console.Error.WriteLine($"{ex.Value.Class.Name}: {PyErr.FormatForClr(ex.Value)}");
}
catch (PySyntaxError ex)
{
    Console.Error.WriteLine($"SyntaxError: {ex.Message}");
}
```

> Threading note: generators use a dedicated thread with a semaphore handshake (see
> [ARCHITECTURE.md](ARCHITECTURE.md) §6); a single `PyEngine` instance is not meant to be shared
> across concurrent threads — use one engine per unit of work.

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
  generators, exceptions, comprehensions, f-strings.
- **Present stdlib modules**: `json`, `yaml`, `collections`, `functools`, `enum`, `math`, `struct`,
  `socket`, `ssl`, `threading`, `asyncio`, `hashlib`/`hmac`/`base64`, `urllib.parse`, `os`, `sys`,
  `time`, `io`, `string`; stubs for `typing` and `dataclasses`.
- **Done scenarios**: Azure IoT Hub device (MQTT), a sync FastAPI-shaped HTTP API, an **async
  FastAPI-shaped HTTP API on a real asyncio event loop** ([async_api.py](samples/async_api.py)), an
  MQTT subscribe round-trip, and YAML+JSON (de)serialization — see [ROADMAP.md](ROADMAP.md) and
  [samples/](samples/).
- **Pure PyPI packages**: any `py3-none-any` wheel without compiled extensions (e.g. paho-mqtt).

### Does not work (yet)

| Scenario | Verified blocker | Feasibility |
|---|---|---|
| **SQLite / Postgres** | the `sqlite3` module is missing (in CPython it is a C extension, not on PyPI) | **Feasible**: add a C# `sqlite3` DB-API module backed by `Microsoft.Data.Sqlite` (Postgres via `Npgsql`), following the other modules in `Modules/` |
| **FastAPI** | `async`/`await` ✅ and an `asyncio` event loop ✅ now exist (see [async_api.py](samples/async_api.py)); still missing: `re`, `datetime`, `abc`, `contextlib`, `inspect`; `pydantic-core` is compiled in Rust (the mini-pip only installs pure wheels); starlette/uvicorn assume an ASGI stack | **Partly unblocked**: async is done and a hand-rolled async web framework runs; the real FastAPI package still needs a non-compiled pydantic + ASGI. See [ROADMAP.md](ROADMAP.md) scenario 2 |

Modules missing today and required by many real scenarios: `re` (regex), `datetime`, `decimal`,
`abc`, `contextlib`, `importlib`, `itertools`, `operator`, `types`, `asyncio`, `sqlite3`. Adding one
means writing a module in [src/PySharpLib/Modules/](src/PySharpLib/Modules/) and registering it in
`StdlibModules.RegisterAll`.

### Running original PyPI packages

The `install` command downloads the **original** packages from PyPI, but "a PyPI package" does not
mean "any package": for one to *run*, **three conditions must hold at once**, which real packages
often violate (outcomes below verified by running the mini-pip):

| # | Constraint | What happens if it is missing |
|---|---|---|
| 1 | **Pure wheel** (`py3-none-any`, no C/Rust) | `install numpy` → rejected ("No pure-python wheel found"). Excludes pandas, psycopg2, cryptography, pydantic-core, orjson, lxml, … |
| 2 | **Dependencies installed by hand** (the mini-pip does **not** resolve them) | `install pydantic` succeeds, but its `pydantic-core` dependency is not downloaded |
| 3 | **Transitive imports within the ~36 present stdlib modules** + syntax in the subset | `six` fails on its first line (`from __future__ import absolute_import`); `pydantic` on `import importlib` |

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

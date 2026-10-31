# PySharp — Usage

## Installation

### Prerequisites

- **.NET 11 SDK.** PySharp targets `net11.0` only.
- Internet access only for `pysharp install` (it downloads pure-Python wheels from PyPI).
- Windows for the `ctypes` module on system DLLs; the rest is cross-platform in principle (Linux and macOS are untested).

### As a global tool (`pysharp` on PATH)

From the suite installer:

```powershell
D:\Dev\Science\ScienceSharp\Install-Suite.ps1 -Tools
```

Or by hand, from the local feed:

```powershell
dotnet tool install --global PySharp --add-source D:\Dev\NuGetLocalFeed --prerelease
dotnet tool update  --global PySharp --add-source D:\Dev\NuGetLocalFeed --prerelease   # later
pysharp --version
```

From a checkout:

```powershell
cd src
dotnet pack PySharp/PySharp.csproj -c Release
dotnet tool install --global --add-source PySharp/bin/Release PySharp
```

A changed tool needs a new `<Version>` in `PySharp.csproj`, otherwise the NuGet cache keeps the old one.
Uninstall: `dotnet tool uninstall --global PySharp`.

### As a library

```powershell
dotnet add package PySharp.Interpreter --source D:\Dev\NuGetLocalFeed --prerelease
```

Companion packages: `PySharp.Numpy`, `.Pandas`, `.Matplotlib`, `.Cv2`, `.Pywt`, `.Torch`, `.Sqlite3`, `.Psycopg2`, `.Pyodbc`, `.Pip`.
Embedding (reference, run code, capture output, exchange data, inject .NET objects, trace hook) is documented in
[README.md](README.md#embedding-the-engine-in-a-net-app).

### Typed array views from source

The source test suite also requires the sibling MatSharp checkout for test-only
cross-view coverage. Run `dotnet test src/PySharp.Tests -c Release` from PySharp.
This checks every declared NDSharp dtype and Mat/Python storage round-trips.

Use a project reference to `src/PySharpLib.Numpy/PySharpLib.Numpy.csproj` with the
sibling NDSharp checkout available; no updated package was produced.
`NumpyArrayViews.ToView<double>(pythonArray)` exports exact double storage.
`NumpyArrayViews.FromView(view, DType.Float64)` imports an ndarray without copying.
Inject its wrapper through `engine.SetVariable("shared", wrapper)` before `Run`;
each `Run` creates a new main module. Do not write shared storage during execution.
Scalar coercion, dtype conversion and original wrapper identity are not provided.

### In notebooks

JupyterSharp runs `pysharp` cells through the plugin `JupyterSharp.Kernels.PySharp` (`src/JupyterSharp.Kernels.PySharp`). Build it with
`dotnet run --file D:\Dev\Science\MathemaSharp\ops\BuildPySharpKernel.cs`, or use the packaged JupyterSharp VS Code extension, which bundles it.
Cell state persists across cells; the last expression is echoed; DataFrames show as HTML; matplotlib figures show inline.

### Native libraries for `torch` and `cv2`

The default build (what the gate uses) bundles libtorch and OpenCV. The **VS Code extension does not**, to keep it small (43 MB instead of 153 MB).
Install them once with `D:\Dev\Science\ScienceSharp\Install-Natives.ps1` (they go into the NuGet cache; `-Check` shows the state). TorchSharp finds libtorch there by
itself; the PySharp kernel plugin gives OpenCvSharp a resolver for the cache. Without them, a cell that uses `torch` or `cv2` says what to run. OpenCV is Windows-only.
To build a plugin without the big natives yourself: `dotnet publish -p:PySharpExternalNatives=true` on `src/JupyterSharp.Kernels.PySharp`.

### In VS Code

| What | State |
| --- | --- |
| Run the current file | A task, `PySharp: run current file` ([.vscode/tasks.json](.vscode/tasks.json)), or Code Runner with `"python": "pysharp run"`. |
| Notebooks | Through the JupyterSharp extension, cell language `pysharp`. Works. |
| **PySharp extension** `marcoparenzan.pysharp` (in `vscode-extension/`) | Built, 0.1.0: `.pys` language with Python highlighting; Run File, Run Selection or Line, Start REPL, Install Package, status bar version. Diagnostics, IntelliSense and a debugger are not there. |

The official Python extension cannot use PySharp as its interpreter: it expects a real CPython it can introspect.

## Commands

```text
pysharp run <file.py> [args…]       run a script; the script's folder and ./site-packages go on sys.path
pysharp install <requirement>       install a pure-Python package into ./site-packages
                                    (PyPI name==version, a local .whl, or --find-links <folder>)
pysharp repl                        interactive REPL (multi-line input supported)
pysharp --help | --version
```

## First steps

```powershell
pysharp run hello.py
pysharp install paho-mqtt==2.1.0
pysharp repl
```

The language coverage, verified scenarios and known gaps are in [README.md](README.md#what-the-language-supports) and
[ROADMAP.md](ROADMAP.md). What is still undecided is in [OPENPOINTS.md](OPENPOINTS.md).

## Build and test from source

```powershell
cd src
dotnet build PySharp.slnx -c Release
dotnet test PySharp.Tests -c Release       # 1507 passed, 13 skipped at the last gate (12 need PostgreSQL, 1 a model download)
```

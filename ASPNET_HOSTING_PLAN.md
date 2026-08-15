# ASP.NET Core hosting PySharp — scenario 11 — a step-by-step plan

**Goal.** ROADMAP.md scenario 11: the *reverse* direction from every other scenario in this project.
Every other scenario is PySharp *running* Python code that happens to implement something
(a web server, an MQTT broker, an ORM client, ...). This one is the opposite: a real ASP.NET Core
(Kestrel) host **embedding PySharp as a .NET library**, calling into real Python plugin scripts from
real C# minimal-API request handlers — Python as a scripting/plugin layer inside a real production
.NET service. Ties into the standing TODO ("Extract PySharpLib as a standalone NuGet library") —
`PySharpLib.csproj` already carries real NuGet package metadata; this scenario consumes it via a
`ProjectReference` for now (packaging/publishing verification is a separate, narrower checklist item,
not blocking this one).

**Method**: same as every other scenario — a real script (here: a real ASP.NET Core app + real Python
plugin files), real gaps, real fixes, real tests, keep the full suite green.

## Design

- `samples/AspNetPySharpHost/` — a real ASP.NET Core minimal-API project (`Sdk="Microsoft.NET.Sdk.Web"`,
  `net10.0`), referencing `PySharpLib`.
- `PythonPluginHost` (`PythonPluginHost.cs`): each named plugin is a real `.py` file under
  `plugins/`, executed once (defining its top-level functions) via `PyEngine.Run` and cached — a
  request handler then calls a named function in the already-loaded module directly, no re-parsing
  per request. One shared `PyEngine`/`Interp` for the whole host: plugin modules are independent
  Python namespaces (each its own `__main__`-shaped `PyModule`), so there's no cross-plugin state
  leakage from sharing the underlying interpreter. `Reload(pluginName)` drops the cache entry, so the
  next call re-reads and re-executes the `.py` file from disk — a real, observable hot-reload with no
  host restart, the actual point of "Python as a scripting/plugin layer".
- Two real plugins: `plugins/greet.py` (string formatting + `datetime`) and `plugins/pricing.py` (a
  small tiered-discount business rule) — demonstrating the actual value proposition: real business
  logic editable by anyone who can edit a `.py` file, no C# recompile.
- Three minimal-API endpoints in `Program.cs`: `GET /api/greet/{name}`, `GET /api/pricing/quote`, and
  `POST /api/plugins/{name}/reload`. A real Python exception (`PyRaise`) surfaces as a real HTTP 400
  with the exception's own message.

## Real gaps found and fixed

- [x] **A real, general C# bug in `ClrMarshal.Unwrap`, the exact same class of bug found earlier this
  session in the ctypes callback work, but via a ternary conditional this time**: `bi >= long.MinValue
  && bi <= long.MaxValue ? (long)bi : bi` — since `long` has an *implicit* conversion *to*
  `BigInteger`, C# infers `BigInteger` as the ternary's common type for *both* branches, so the
  "fits in long" conversion silently never took effect; every caller got a raw, unconverted
  `BigInteger` back regardless of the condition. Confirmed live: `Results.Json(...)` (`System.Text.
  Json`, no `BigInteger` converter registered by default) reflection-serialized a plugin's `len(...)`
  result as `{"isPowerOfTwo":false,"isZero":false,"isOne":false,"isEven":false,"sign":1}` instead of
  a plain JSON number. Fixed by casting each branch to `(object)` explicitly, preventing the
  arm-to-arm widening. This is a real, pre-existing bug in `Unwrap` that predated this scenario —
  any embedding host calling `Unwrap` on an in-range Python int was silently getting a `BigInteger`
  back instead of a `long`, just never surfaced by an existing test until this scenario's own
  real, non-`TryToClr`-typed value flow (a Python function's return value, whose eventual .NET shape
  the host doesn't know ahead of time) exercised it end to end. Test:
  `M11_Interop/PythonToClrMarshalTests.cs`.
- [x] **A new, general embedding capability**: `ClrMarshal.ToPlainObject(object pyValue)` —
  recursively converts a Python value (typically a function's return value) into a plain,
  JSON-serializable .NET object graph (`dict`/`list`/`tuple`/`set` → `Dictionary<string, object?>`/
  `List<object?>`, recursively). Not scenario-specific — any embedding host calling into a Python
  function and not knowing its return shape ahead of time needs this, not just this one. Test:
  `M11_Interop/PythonToClrMarshalTests.cs`.
- [x] **A real, general .NET testing gotcha, not a PySharp bug**: `WebApplicationFactory`-based tests
  (a real in-process ASP.NET Core host + its own thread-pool needs) were found to intermittently hang
  the *entire* test run — not just their own test class — when run inside the same process as
  `PySharp.Tests`' own 1300+ tests, many of which dedicate a real foreground OS thread per in-flight
  generator/coroutine (`BigStack.cs`/`PyGenerator.cs`'s own execution model). Neither a dedicated
  `[Collection(DisableParallelization = true)]` nor an eager `ThreadPool.SetMinThreads(200, 200)`
  fully eliminated the intermittent hang (reproduced roughly 1 run in 3–5 across repeated full-suite
  checks). Root-caused *enough* to fix pragmatically, not fully to a single line: isolated the
  ASP.NET Core hosting tests into their own test project/assembly, `PySharp.Tests.AspNetHosting`
  (`src/PySharp.Tests.AspNetHosting/`) — its own process when run via `dotnet test`, removing the
  cross-suite thread-pressure interaction entirely rather than continuing to fight it with
  in-process mitigations. Confirmed: 3 consecutive clean runs of the isolated project (300–650ms
  each, vs. the shared-process runs that intermittently hung past 2 minutes) and 3 consecutive clean
  runs of the main `PySharp.Tests` suite (1322/1334, 12 skipped — unrelated live-Postgres tests
  without credentials set in this shell) with the ASP.NET Core references fully removed from it.

## Verification

Real, live, in-process HTTP requests via `Microsoft.AspNetCore.Mvc.Testing`'s
`WebApplicationFactory<Program>` (a real ASP.NET Core pipeline, not a mock) — 6 tests in
`src/PySharp.Tests.AspNetHosting/AspNetPySharpHostTests.cs`:

- `GET /api/greet/Ada` calls the real `greet.run` Python function and returns real, correctly-typed
  JSON (string fields, and — since the `Unwrap` fix — a real JSON number for `length`, not a
  reflection-serialized `BigInteger` object).
- `GET /api/pricing/quote?unitPrice=10&quantity=-1` — a real Python `ValueError` (from `pricing.py`'s
  own validation) surfaces as a real HTTP 400 with the exception's message.
- `GET /api/pricing/quote` at three quantities (5, 10, 100) — the real tiered-discount business rule,
  computed entirely in Python, verified at each of its three real thresholds (0%, 10%, 20%).
- `POST /api/plugins/greet/reload` followed by another `GET /api/greet/Bob` — the hot-reload endpoint
  itself succeeds and the plugin continues to work correctly afterward (the real, observable proof
  that a plugin *can* be reloaded without restarting the host).

## Status: done

Scenario 11 verified end to end. Not attempted/deliberately out of scope for v1 (no real scenario has
needed it yet): `params`/named-argument binding from an HTTP request body directly into Python
kwargs (this scenario's own endpoints marshal explicit, small argument lists by hand instead);
packaging `PySharpLib` as a *published* standalone NuGet package (the standing TODO item) — this
scenario consumes it via `ProjectReference`, which already proves the "embed PySharpLib as a .NET
library" capability the TODO cares about, independent of the separate publishing-pipeline work.

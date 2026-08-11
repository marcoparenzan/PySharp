# numpy in PySharp

`import numpy` in PySharp gets a **C# shim**, not the real numpy. This page is the user-facing
summary; see [NUMPY_PLAN.md](NUMPY_PLAN.md) for the full phased build plan and architecture notes.

## Why not the real numpy

Real numpy ships as a compiled CPython C extension (`.pyd`/`.so`). PySharp is a from-scratch
Python interpreter written in C#; it cannot load CPython's native extension binaries — no
Python-in-C#/Java/Go/Rust interpreter can, without embedding a real CPython underneath, which this
project deliberately does not do. `pysharp install numpy` fails on purpose, with a message pointing
here instead of a compiled wheel it could never actually use.

## Current status: **Phase 0 (groundwork) — not yet a usable array library**

Right now `import numpy` succeeds and `numpy.__version__` exists (so scripts that merely check
"is numpy importable" don't immediately fail), but **there is no `ndarray` yet** — no array
construction, no indexing, no math. Nothing beyond the bare import currently works.

## What's planned (see NUMPY_PLAN.md for the real, checkbox-level order)

A practical subset, not full API parity:

- `ndarray` construction (`np.array`, `np.zeros`/`ones`/`full`/`empty`, `np.arange`, `np.linspace`, ...)
- Indexing/slicing (integer, negative, multi-dimensional, boolean masking, fancy indexing) — copies
  first; real strided views are a later, optional phase
- Elementwise arithmetic and real broadcasting rules
- Reductions (`sum`/`mean`/`min`/`max`/`std`/`var`/`argmin`/`argmax`, with or without `axis=`)
- The common ufuncs (`sqrt`, `exp`, `log`, trig, `clip`, ...)
- Shape manipulation (`reshape`, `ravel`, `.T`/`transpose`, `concatenate`/`stack`, `squeeze`)
- A handful of dtypes: `float64` first, then `bool`, then `int64` with promotion rules — **not**
  `float32`/`int32`/`complex` for v1
- Basic linear algebra (`dot`/`@` for 1-D and 2-D, `norm`/`trace`/`diagonal`; `inv`/`solve`/`det`
  deferred/optional)
- Interop conveniences (`.tolist()`, iteration, `np.random` as a `System.Random`-backed shim — not
  bit-exact against real numpy's own Generator for a given seed)

## Explicit non-goals

- **Not** full NumPy API parity — this is a practical, script-driven subset, the same philosophy
  every other module in this project follows (see ROADMAP.md's "Method: scenario-driven
  development").
- **Not** C-level performance — a correctness-first C# implementation, not a vectorized native one.
- **Not** bit-exact PRNG reproducibility against real numpy's own `Generator`/`BitGenerator` for a
  given seed.
- **No** C-API, F2PY, or any way to load real compiled numpy/scipy/pandas extensions — that's the
  structural wall this shim exists to work around for the *Python-level* API surface only.

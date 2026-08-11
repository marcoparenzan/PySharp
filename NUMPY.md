# numpy in PySharp

`import numpy` in PySharp gets a **C# shim**, not the real numpy. This page is the user-facing
summary; see [NUMPY_PLAN.md](NUMPY_PLAN.md) for the full phased build plan, architecture notes, and
every phase's own verification notes.

## Why not the real numpy

Real numpy ships as a compiled CPython C extension (`.pyd`/`.so`). PySharp is a from-scratch
Python interpreter written in C#; it cannot load CPython's native extension binaries — no
Python-in-C#/Java/Go/Rust interpreter can, without embedding a real CPython underneath, which this
project deliberately does not do. `pysharp install numpy` fails on purpose, with a message pointing
here instead of a compiled wheel it could never actually use.

## Current status: all 12 phases done — a genuinely usable array library

`ndarray` construction, indexing, real strided views, broadcasting, reductions, ufuncs, shape
manipulation, dtypes/promotion, basic linear algebra, and interop conveniences all work, verified
throughout against real numpy's own documented semantics (not just "doesn't crash"). See
[samples/numpy_demo.py](samples/numpy_demo.py) for a realistic end-to-end session, and
[src/PySharp.Tests/M14_Numpy](src/PySharp.Tests/M14_Numpy/) for the 120+ regression tests.

## What's implemented (see NUMPY_PLAN.md for the full checkbox-level detail)

A practical subset, not full API parity:

- `ndarray` construction: `np.array`, `np.zeros`/`ones`/`full`/`empty`, `np.arange`, `np.linspace`,
  `np.eye`/`identity`, `.copy()` — all with `dtype=` support, plus `.astype(dtype)`.
- Three dtypes — `float64`, `bool`, `int64` — with real arithmetic promotion (`float64` > `int64` >
  `bool`; true division always `float64`); no `float32`/`int32`/`complex` in this v1.
- Indexing/slicing: integer (incl. negative), multi-dimensional, boolean masking, `np.newaxis`. Basic
  indexing (int/slice/`None`) and `.T`/`transpose()`/`reshape`/`ravel`/`expand_dims`/`squeeze` are
  **real strided views** sharing the source buffer — mutating a slice or a transpose mutates the
  original, matching real numpy. Boolean masking and `.flatten()` always copy, also matching real
  numpy.
- Elementwise arithmetic and real broadcasting rules, incl. `// %` (Python sign-of-divisor semantics)
  and bitwise `& | ^ ~` (unified across `bool`/`int64`).
- Reductions: `sum`/`mean`/`min`/`max`/`std`/`var`/`argmin`/`argmax`/`cumsum`/`cumprod`, with or
  without `axis=`.
- The common ufuncs: `sqrt`, `exp`, `log`/`log10`, trig, `floor`/`ceil`/`sign`/`round`/`clip`,
  `minimum`/`maximum`/`power`.
- Shape manipulation: `reshape`, `ravel`, `.T`/`transpose`, `flatten`, `concatenate`/`stack`/
  `vstack`/`hstack`, `expand_dims`/`squeeze`.
- Basic linear algebra: `dot`/`matmul`/`@` for 1-D and 2-D operands, `np.linalg.norm`, `trace`,
  `diagonal`. N-D batched matmul and `inv`/`solve`/`det` are deliberately deferred (not implemented).
- Interop: `.tolist()`, iteration (`for row in a`), `float`/`int`/`bool` coercion for size-1 arrays,
  `np.random` (`seed`/`rand`/`randn`/`randint`/`choice` — seedable/reproducible *within this shim*,
  not bit-exact against real numpy's own `Generator`/`BitGenerator`), and a two-way bridge to real
  .NET arrays (`a.to_clr()` / `np.array(a_dotnet_array)`).

## Explicit non-goals

- **Not** full NumPy API parity — this is a practical, script-driven subset, the same philosophy
  every other module in this project follows (see ROADMAP.md's "Method: scenario-driven
  development").
- **Not** C-level performance — a correctness-first C# implementation (a modest contiguous-float64
  fast path exists for the hottest elementwise paths, not a fully vectorized native one).
- **Not** bit-exact PRNG reproducibility against real numpy's own `Generator`/`BitGenerator` for a
  given seed.
- **No** C-API, F2PY, or any way to load real compiled numpy/scipy/pandas extensions — that's the
  structural wall this shim exists to work around for the *Python-level* API surface only.

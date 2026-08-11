# NumPy support — a long, step-by-step plan

**Goal.** Provide a usable `numpy` in PySharp by shipping a **C# `numpy`-shaped shim** (a native
module), *not* by loading the real numpy. Real numpy is a CPython **C extension** compiled against the
C-API; a Python-in-C# interpreter cannot load its `.pyd`/`.so` binaries. This is a structural wall
(see [ROADMAP.md](ROADMAP.md) Axis C), so `pysharp install numpy` will always be refused — the shim is
the answer.

**What we build.** An `ndarray` object plus the most-used construction, indexing, elementwise math,
broadcasting, reductions, ufuncs, shape ops, basic linear algebra, and a few dtypes — enough to run
real-world array code that stays within the implemented subset. **Not** full API parity, **not** C
performance, **not** views-everywhere semantics (documented per step).

---

## Key architecture decisions (read first)

- **`ndarray` = a `PyClass` with a C# wrap**, exactly like the `socket` module (`SocketClass` +
  `SockWrap`). The class dict holds builtin `__add__`/`__mul__`/`__getitem__`/`__iter__`/`__repr__`/…
  so **all arithmetic, indexing and iteration reuse the interpreter's existing dunder dispatch** — no
  changes to `Interp.BinaryOp`/`GetItem`/`GetIter`. The numeric payload lives in the instance dict
  under a key (e.g. `__ndarray__`) as an `NdArrayData`.
  - *Verified:* `Interp.BinaryOp` only falls back to dunders for `PyInstance` (Interp.cs), so the
    ndarray must be a `PyInstance` — this decision is load-bearing.
- **`NdArrayData`**: a flat C-order buffer as a `System.Array` (`double[]`/`long[]`/`bool[]`) + a
  `DType` enum + `int[] Shape` + `int[] Strides` (+ optional base for views). Element get/set is
  dtype-dispatched. Adding a dtype = adding buffer cases, not a redesign.
- **dtype rollout**: `float64` first (Phases 1–7); `bool` when comparisons arrive (Phase 4); `int64`
  and promotion in Phase 8. `float32`/`int32`/`complex` are out of scope for v1.
- **views vs copies**: start with **copies** for slices (simpler, correct); add real strided views in
  a later, optional phase — documented at the step that makes the choice.
- **Module name**: register as `numpy`; users write `import numpy as np` themselves. Add `np` nowhere.

## Execution rules (how to run this plan)

1. **One step at a time.** Each checkbox below is a single small change with a visible deliverable.
2. **Every step ships a test.** Add xUnit tests under `src/PySharp.Tests/M14_Numpy/`. Prefer running
   real Python via the `Py.Run` helper (assert on printed output), like `YamlTests`.
3. **Keep the suite green** after every step (`dotnet test`). Never leave it red between steps.
4. **MIT header** on every new file; match surrounding code style.
5. **Commit per step or per small group**; **bump the package version once per phase** (not per step)
   and re-pack to the local feed when a phase completes.
6. Update this file: tick the box, add a one-line note if a decision was made.
7. When a phase lands, add/update the **ROADMAP scenario** ("N — array computing") and RELEASE_NOTES.

## Reference sources for exact semantics

No live Python/numpy exists in this environment (see ROADMAP.md's general verification method), so
when a step's exact behavior isn't obvious from memory or the official docs, **read real numpy's own
source** instead of guessing — the same "read the real thing" discipline used throughout this whole
project (installed PyPI package source, official docs), applied here too:

- **`numpy/numpy` on GitHub** (BSD-3-Clause — permissive, compatible with this project's MIT license;
  port/adapt algorithms with attribution in the commit/comment, don't copy-paste wholesale claiming
  it as original) is the source of truth for exact edge-case behavior (broadcasting corner cases,
  dtype promotion, NaN handling, ...).
- **`numpy/_core/arrayprint.py`** specifically is real **pure Python**, not C — directly readable and
  portable as a reference for the exact array-printing algorithm (column-width alignment, precision
  rules) that Phase 1.6 deliberately simplified ("no column-width padding/alignment yet"). Revisit
  that simplification against the real algorithm when polish time comes (Phase 12).
- The **compiled ufunc inner loops** (`numpy/_core/src/umath/`) are real C calling the CPython C-API
  at the boundary — readable as an *algorithm* reference (how a given broadcast/reduction is
  structured), never usable as a binary/link dependency (that's the whole C-extension wall this plan
  exists to work around — see the next section).

## Alternatives considered and rejected

- **`Numpy.NET`** (SciSharp, github.com/SciSharp/Numpy.NET) — investigated 2026-08-11. **Not** a
  from-scratch reimplementation: it wraps the *real* NumPy via Python.NET (pythonnet), bundling an
  embedded CPython 3.7 + real NumPy 1.16 (via `Python.Included`) or requiring a local Python install
  (`Numpy.Bare.dll`). Solves the opposite problem from this plan's — "call real NumPy from a C# host"
  — not "make `import numpy` work inside a from-scratch Python-in-C# interpreter with zero CPython
  anywhere". Using it here would mean every `ndarray` a PySharp script sees is actually a real CPython
  `PyObject*` behind a pythonnet wrapper, participating in none of `Interp`'s own dunder dispatch
  without a full marshaling layer — architecturally the same "embed CPython" wall ROADMAP.md's Axis C
  already declined, just relocated into a third-party library. Rejected; the goal declared at the top
  of ROADMAP.md ("the author wants to use *only* PySharp to run their own Python") rules it out.
- **`tinynumpy`** (PyPI) — a genuine pure-Python numpy-workalike, real prior art for "reimplement a
  practical subset by hand" — but dormant since 2016 (last release 1.2.1), not tracked as a design
  reference for this plan.

---

## Phase 0 — Groundwork ✅ (2026-08-11)

- [x] 0.1 Fix `pysharp install numpy`: today it can exit with an **unhandled CLR exception** (see
  ROADMAP note). Make the mini-pip fail cleanly with the existing "no pure-python wheel" message and
  a hint: *"numpy is a C extension; use PySharp's built-in `numpy` shim (`import numpy`)."* Test.
  — *Note:* verified live; the "unhandled CLR exception" this step describes no longer reproduces
  (`install numpy` already failed cleanly with the base "no pure-python wheel" message, likely fixed
  by unrelated Axis D work between 2026-07-27 and now). Only the numpy-specific hint sentence was
  actually missing — added in `PackageInstaller.cs`.
- [x] 0.2 Create `src/PySharpLib/Modules/NumpyModule.cs` skeleton with `Create()` returning a
  `numpy` module; register it in `StdlibModules.RegisterAll`. Add `numpy.__version__ =
  "<x> (PySharp shim)"`. Test: `import numpy; print(numpy.__version__)`.
  — *Note:* version string is `"0.0.1 (PySharp shim)"` (`NumpyModule.ShimVersion`); bump this per
  the "once per phase" rule as later phases land.
- [x] 0.3 Add `NUMPY.md` (user-facing): supported subset, dtypes, non-goals, "not the real numpy".
  Link it from README's "Verified scenarios and limits".
- [x] 0.4 Create the test project folder `M14_Numpy/` with a `NumpyTests` base (an `import numpy`
  `Run` helper). One smoke test importing the module.
  — *Note:* also added a regression test for 0.1's install-hint in `M7_Pip/PipInstallTests.cs`
  (real network, matching that file's existing paho-mqtt tests).

## Phase 1 — The `ndarray` core ✅ (2026-08-11)

- [x] 1.1 `NdArrayData` C# class: `DType` enum (`Float64` only for now), flat `System.Array Buffer`,
  `int[] Shape`, `int[] Strides`, computed `Size`/`Ndim`; C-order stride computation. Unit-test the
  stride math directly (C# test, no Python).
- [x] 1.2 Build the `ndarray` `PyClass` in `NumpyModule` (empty methods for now) and a factory
  `Wrap(NdArrayData)` → `PyInstance` storing the wrap under `__ndarray__`. Not user-constructible yet.
  — *Note:* `numpy.ndarray` itself is deliberately not exposed as a module attribute yet (calling it
  with no real `__init__` would just crash confusingly) — deferred until Phase 2 makes it
  meaningfully constructible.
- [x] 1.3 Data attributes via the class: `.ndim`, `.size`. Test through a temporary `np._fromflat`
  helper (internal) that makes an array so tests can assert.
- [x] 1.4 `.shape` → tuple, `.dtype` → a dtype object with `.name` (`'float64'`). Test.
  — *Note:* dtype instances are cached singletons per `DType` (`Float64DType`), matching real
  numpy's own per-dtype identity.
- [x] 1.5 `__len__` (size of axis 0). Test. — *Note:* a 0-d (scalar) array raises a real
  `TypeError("len() of unsized object")`, matching real numpy.
- [x] 1.6 `__repr__`/`__str__`: numpy-ish formatting for 1-D and 2-D (`[1. 2. 3.]`, nested for 2-D).
  Keep it simple; refine later. Test `print(...)` output.
  — *Note:* verified live: space-separated elements (not comma-separated), a trailing `.` (no `0`)
  on whole-number floats, 2-D rows indented to align under the opening bracket. `__repr__` wraps
  `__str__` in `array(...)` without real numpy's own re-indentation for that longer prefix
  (documented simplification — "keep it simple" per this step). No column-width padding/alignment
  yet either (real numpy pads elements to a common width; deferred).

## Phase 2 — Construction ✅ (2026-08-11)

- [x] 2.1 `np.array(list)` for a **1-D** Python list of numbers → `float64` ndarray. Test.
  — *Note:* real int→float64 promotion (`np.array([1,2,3])` → `[1. 2. 3.]`), matching the dtype
  rollout (int64 is Phase 9).
- [x] 2.2 `np.array(nested list)` for **2-D/N-D**; infer shape recursively; raise `ValueError` on
  ragged input. Test both success and the ragged error.
  — *Note:* shape inferred by descending the *first* element of each level (matching real numpy),
  then a validating pass catches any ragged row AND any scalar-where-a-list-was-expected mismatch
  (`np.array([1, [2, 3]])`) with the same real `ValueError`. A bare scalar (`np.array(5.0)`)
  correctly produces a real 0-d array. `np.array(existing_ndarray)` (copy-from-ndarray) is **not**
  handled yet — out of this step's literal scope (Python list/tuple input only); flagging for a
  later phase if a real script needs it.
- [x] 2.3 `np.zeros(shape)` and `np.ones(shape)` where `shape` is an int or a tuple. Test.
- [x] 2.4 `np.full(shape, value)` and `np.empty(shape)`. Test.
  — *Note:* `empty` is deterministically zero-filled (a real C# array's own default state), not
  real numpy's genuinely uninitialized memory — documented in the code as a deliberate, safe v1
  simplification (any script relying on `empty`'s garbage contents was already relying on
  undefined behavior against real numpy too).
- [x] 2.5 `np.arange([start,] stop[, step])` (float64). Test incl. negative step.
- [x] 2.6 `np.linspace(start, stop, num=50, endpoint=True)`. Test.
  — *Note:* the exact endpoint value is written directly (not derived from the step multiplication)
  to avoid float drift at the boundary, matching real numpy's own behavior there.
- [x] 2.7 `np.eye(N[, M])` and `np.identity(n)`. Test. — *Note:* `eye` supports a rectangular
  `N != M` shape.
- [x] 2.8 `array.copy()` and `np.copy(a)`. Test independence from the source.

## Phase 3 — Indexing & slicing (copies) ✅ (2026-08-11)

- [x] 3.1 `a[i]` on 1-D → scalar (Python `float`); support negative `i`; `IndexError` out of range. Test.
- [x] 3.2 `a[i, j, ...]` N-D integer tuple index → scalar. Test.
- [x] 3.3 `a[i]` on N-D (partial index) → sub-array (copy). Test.
  — *Note:* implemented as one general mechanism, not a special case: any axis without an explicit
  index in the given tuple gets an implicit full slice (real numpy's own "partial indexing" rule),
  which is also what makes 3.5's int/slice mixing fall out for free from the same code path.
  Verified the result is a genuinely independent copy (mutating the sub-array leaves the source
  array untouched).
- [x] 3.4 1-D slice `a[start:stop:step]` → 1-D array (copy). Test incl. negative/step.
  — *Note:* reuses `PySlice.Indices(len)` (the same real start/stop/step/count normalization
  `list`/`str` slicing already uses) rather than re-deriving slice semantics.
- [x] 3.5 N-D slice `a[s1, s2, ...]` mixing ints and slices → array (copy). Test. — *Note:* see 3.3.
- [x] 3.6 Assignment `a[i] = v` and `a[i, j] = v` (scalar). Test.
- [x] 3.7 Slice assignment `a[1:3] = scalar` (broadcast scalar). Test.
- [x] 3.8 Slice assignment `a[1:3] = array` (shape must match). Test + error case.
  — *Note:* the shape check is an exact match only (real per-element broadcasting rules — e.g.
  assigning a `(3,)` array into a `(2,3)` slice — are Phase 4's job); a mismatch raises the real
  `ValueError` numpy itself raises ("could not broadcast input array from shape ... into shape ...").

## Phase 4 — Elementwise ops & broadcasting ✅ (2026-08-11)

- [x] 4.1 Add `@` (matmul) to the operator→dunder map (`__matmul__`) if missing; verify `+ - * / **`
  already map to `__add__`/… (research + tiny fix). Test that `ndarray.__add__` is reachable via `+`.
  — *Note:* `Interp.BinDunders` already mapped every operator including `@`/`__matmul__` (and their
  reflected `__r*__` forms) before this phase — no interpreter fix was actually needed, only real
  `__add__`/etc. implementations to reach. `__matmul__` itself is intentionally left unimplemented
  here (real matrix multiplication is Phase 10, linear algebra).
- [x] 4.2 Elementwise `a + b` for **same-shape** arrays (`__add__`); then `- * /` (`__sub__` etc.). Test.
- [x] 4.3 Scalar broadcasting: `a + 2`, `2 + a` (`__radd__`), same for `- * / **`. Test.
- [x] 4.4 Broadcasting shape rule — compute the broadcast shape of two shapes (right-aligned, dims
  equal or 1). Pure C# helper + unit test (no Python). — *Note:* `NumpyModule.BroadcastShape` made
  `public` specifically so it's directly C#-unit-testable, mirroring `NdArrayData`'s own Phase 1.1
  precedent.
- [x] 4.5 Broadcasted elementwise ops using stride-0 iteration over the broadcast shape. Test
  `(2,3) + (3,)`, `(2,1) + (1,3)`, and an incompatible-shape `ValueError`.
  — *Note:* a plain Python scalar is converted to a real 0-d `NdArrayData` before broadcasting, so
  `2 + arr` and `np.array(2.0) + arr` share the exact same code path — no scalar special-casing.
- [x] 4.6 Unary `-a` (`__neg__`), `abs(a)` (`__abs__`), `+a`. Test.
- [x] 4.7 `**` power elementwise and with scalar exponent. Test.
- [x] 4.8 In-place `+= -= *= /=` (may reuse binary op + rebind; document if not true in-place). Test.
  — *Note:* confirmed `Interp.ExecAugAssign` already falls back to the plain binary dunder +
  rebinding the name when no `__iadd__`/etc. is defined — no `__i*__` methods were added, so `+=`
  is genuinely **not** in-place: `y = x; x += 1` leaves `y` pointing at the original, unlike real
  numpy's actual buffer mutation. Documented as a deliberate v1 simplification (verified live and
  tested); revisit only if a real script's correctness depends on the aliasing.

## Phase 5 — Comparisons, bool arrays, masking ✅ (2026-08-11)

- [x] 5.1 Add the `Bool` dtype (a `bool[]` buffer path in `NdArrayData` get/set + repr). Test repr.
  — *Note:* this meant genericizing every place that used to hardcode `double[]` (indexing,
  `.copy()`, repr/str formatting) behind a small dtype-dispatched `GetElement`/`SetElement`/
  `MakeBuffer`/`CloneBuffer` set, rather than duplicating the whole indexing/formatting machinery a
  second time for bool. Also fixed a real Phase 2 gap found along the way: `np.array([True, False])`
  was unconditionally building a `float64` array (Phase 2 only ever had one dtype to build) instead
  of real numpy's own bool-dtype inference — `ArrayFromPython` now infers `Bool` when every leaf is
  a real Python `bool`, else `float64` (ints still promote to float — real int64 inference stays
  Phase 9's job).
- [x] 5.2 Comparisons `== != < <= > >=` (array vs array, array vs scalar) → **bool** ndarray. Test.
  — *Note:* **needed a real `Interp.cs` core change**, not just a new module: `CompareExpr` always
  forced its dunder's return value through `PyOps.Truthy`, so even a correct `ndarray.__lt__`
  returning a real bool array got collapsed to a single Python bool before `a < b` could ever see
  it. Fixed by having `Interp.Eval(CompareExpr)` return the dunder's *raw* result for an unchained
  (single-operator) comparison — matching real CPython, which never implicitly bools a plain `a < b`
  — while a genuinely chained comparison (`a < b < c`) keeps the exact same truthiness-collapsing
  short-circuit behavior as before (verified both directions live and with dedicated tests; full
  suite re-run clean before proceeding, given the blast radius of a change to core comparison
  evaluation).
- [x] 5.3 Logical `&` `|` `~` on bool arrays (`__and__`/`__or__`/`__invert__`). Test.
  — *Note:* also added `^`/`__xor__` (not explicitly listed, but trivial given the same shared
  broadcasting machinery, and real numpy has it). Rejects a non-bool-dtype operand with a real
  `TypeError` (real numpy's `&`/`|` on float arrays does real bitwise-integer operations instead —
  out of v1 scope, see the module's own dtype rollout notes). A raw Python `bool` scalar operand
  (`mask & True`) is coerced to a real 0-d `Bool` array via a dedicated `LogicalOperandData` —
  deliberately *not* shared with arithmetic's own `OperandData` (a scalar `bool` means `1.0`/`0.0`
  there, `True`/`False` here — same Python value, different real numpy semantics depending on
  context).
- [x] 5.4 `a.any()`, `a.all()`. Test.
- [x] 5.5 Boolean-mask read: `a[mask]` → 1-D array of selected elements. Test.
  — *Note:* a real bool-dtype `ndarray` used as the *entire* index is recognized as a genuinely
  different indexing mode (real numpy's boolean/fancy indexing) up front in `GetItem`/`SetItem`,
  before falling through to the axis-by-axis int/slice/tuple resolution everything else uses. v1
  scope: the mask's shape must exactly match the array's own shape (no partial/broadcast masks).
- [x] 5.6 Boolean-mask assign: `a[mask] = value`. Test. — *Note:* supports both a broadcast scalar
  and an array whose length matches the real count of `True` positions (mismatched count raises a
  real `ValueError`, matching real numpy's own message shape).
- [x] 5.7 `np.where(cond, x, y)` (broadcasted). Test. — *Note:* a real 3-way broadcast (cond, x, and
  y all broadcast together, done as two chained 2-way broadcasts — broadcasting is associative).
  Result dtype matches x/y when they agree, else falls back to `float64` (no promotion rules exist
  yet — Phase 9's job).

## Phase 6 — Reductions ✅ (2026-08-11)

- [x] 6.1 `a.sum()` / `np.sum(a)` over all elements. Test.
  — *Note:* the whole-array fold is seeded by the *first visited element* (real C-order), which
  works uniformly for sum/prod/min/max with no per-op identity needed — except an empty array has
  no first element, so sum/prod fall back to their real identity (`0.0`/`1.0`) while min/max (which
  have none in real numpy either) raise a real `ValueError`.
- [x] 6.2 `sum(axis=k)` for N-D (single axis) → reduced array. Test 2-D both axes.
  — *Note:* a shared `LineKey`/`ReduceAxisToArray` pair (keyed by "every axis except the reduced
  one", via a `ComputeStrides` over the *already-reduced* shape) backs every axis-aware reduction
  in this phase, not just sum — one real mechanism, not one per op. `NdArrayData.ComputeStrides`
  went from `private` to `internal` so this module-level machinery could reuse it instead of
  duplicating the stride formula.
- [x] 6.3 `mean` (all + axis). Test.
- [x] 6.4 `min`/`max` (all + axis) and `np.min`/`np.max`. Test.
- [x] 6.5 `prod`, `std`, `var` (all + axis). Test. — *Note:* `var`/`std` use real population variance
  (`ddof=0`, matching real numpy's own default), computed as a genuine two-pass mean-then-
  sum-of-squared-deviations (including per-axis, via the per-line mean feeding a second pass).
- [x] 6.6 `argmin`/`argmax` (all + axis). Test.
  — *Note:* ties keep the *first* occurrence (a strict `&lt;`/`&gt;` "does this beat the current
  best" comparison, matching real numpy). `axis=None` returns a real Python `int` (the flat C-order
  index); `axis=k` returns the per-line index stored in a `float64` array (no `int64` dtype exists
  yet — Phase 9's job — so the index values are real whole numbers held in the only numeric dtype
  available, a documented v1 simplification).
- [x] 6.7 `cumsum`/`cumprod` (1-D first, then axis). Test.
  — *Note:* `axis=None` flattens (real C-order) then cumulates — a 1-D array flattened is itself, so
  this covers the "1-D first" case for free. `axis=k` cumulates per line using the same `LineKey`
  mechanism; real C-order traversal visits every smaller index along *any* axis before a larger one
  (holding the other axes fixed), which is what makes a simple running-total-per-line correct
  regardless of which axis was chosen, not just the last one — verified live for both axes of a 2-D
  array before trusting the general claim.

All 7 sub-steps were also wired as module-level `np.sum`/`np.mean`/`np.min`/`np.max`/`np.prod`/
`np.std`/`np.var`/`np.argmin`/`np.argmax`/`np.cumsum`/`np.cumprod` (not just instance methods) —
real numpy has both forms and they're heavily used in practice; trivial to add given they share the
exact same underlying reduction functions.

## Phase 7 — Universal functions (ufuncs) ✅ (2026-08-11)

- [x] 7.1 A ufunc factory: apply a `Func<double,double>` elementwise, returning a new array. Internal.
  — *Note:* `ApplyUfunc` is this factory — it's just Phase 4's own `ElementwiseUnary` plus a real
  scalar fast path, so `np.sqrt(4.0)` returns a plain Python `float` (not a forced 0-d array),
  matching real numpy's own scalar-ufunc behavior.
- [x] 7.2 `np.sqrt`, `np.exp`, `np.log`, `np.log10`, `np.abs`. Test.
- [x] 7.3 `np.sin`, `np.cos`, `np.tan`, `np.arcsin/arccos/arctan`. Test.
- [x] 7.4 `np.floor`, `np.ceil`, `np.round`, `np.sign`, `np.clip(a, lo, hi)`. Test.
  — *Note:* `round`/`clip` also added as real `ndarray` methods (`a.round()`/`a.clip(lo, hi)`),
  matching real numpy's own API shape — unlike `sqrt`/`exp`/trig/etc., which real numpy only
  exposes at module level, not as methods. `round` uses real round-half-to-even (banker's
  rounding), matching real numpy's actual default — free, since .NET's own `Math.Round` defaults to
  `MidpointRounding.ToEven` too.
- [x] 7.5 `np.minimum`, `np.maximum`, `np.power` (binary, broadcasted). Test. — *Note:* reuses
  Phase 4's `ElementwiseOp` directly (the same broadcasting machinery `+`/`-`/etc. use), just not
  reachable through an operator since Python has no `minimum`/`maximum` dunder.
- [x] 7.6 Constants: `np.pi`, `np.e`, `np.inf`, `np.nan`. Test.
  — *Note:* verifying `np.nan != np.nan` surfaced a real, general-purpose interpreter bug unrelated
  to numpy itself: `PyOps.PyEquals` short-circuited `==` on C# reference identity before checking
  real value semantics — correct for lists/dicts/tuples/strings (identity there genuinely implies
  equality), but wrong for `double`, since IEEE 754 requires NaN to never equal itself even when
  it's literally the same boxed object (`x = float('nan'); x == x` is really `False` in CPython —
  no identity shortcut exists at the language level for `==`). Fixed by excluding `double` from the
  identity fast path; confirmed with the full suite (1166/1166) before and after, since this is a
  shared core equality helper, not numpy-scoped code.

## Phase 8 — Shape manipulation ✅ (2026-08-11)

- [x] 8.1 `reshape(shape)` / `np.reshape` (product must match; `-1` inferred dim). Test.
  — *Note:* accepts both real call shapes (`a.reshape((2,3))` and `a.reshape(2,3)`), and shares the
  source buffer — a real *view*, verified live (mutating the reshaped result mutates the original).
  This is a deliberate, narrow exception to "copies for now": every array here is always fully
  C-contiguous (nothing yet produces a non-contiguous one), so reinterpreting the same flat buffer
  under a new shape is always exactly what a real numpy view would show — no risk of it being wrong
  without the fuller strided-view machinery Phase 12 will add.
- [x] 8.2 `ravel()` / `flatten()` → 1-D. Test.
  — *Note:* `ravel()` is a view (same reasoning as reshape); `flatten()` is a real independent copy
  — matching real numpy's own documented split, not a simplification. Verified live: mutating a
  `ravel()` result mutates the source, mutating a `flatten()` result does not.
- [x] 8.3 `.T` and `transpose(axes)` (permute strides or materialize a copy). Test 2-D and 3-D.
  — *Note:* materializes a real copy (chose "copy", the plan's own other listed option) — a genuine
  transpose *view* needs non-canonical strides decoupled from shape, which is real work Phase 12
  owns; no `axes` reverses every axis (generalizing 2-D's "swap rows and columns"), explicit `axes`
  permutes to that order. Verified against a real 3-D `transpose(1, 0, 2)`, not just 2-D.
- [x] 8.4 `concatenate([a, b], axis)`. Test.
- [x] 8.5 `stack`, `vstack`, `hstack`. Test.
  — *Note:* `stack` is built as `expand_dims` + `concatenate` along the same new axis rather than a
  separate algorithm; `vstack` promotes a real 1-D input to a row first (matching real numpy — a
  naive "concatenate along axis 0" would be wrong for 1-D inputs); `hstack` picks axis 0 vs. axis 1
  based on the real input's `ndim`.
- [x] 8.6 `expand_dims(a, axis)`, `squeeze(a)`. Test.
  — *Note:* both share the buffer (real views — inserting/removing a real size-1 axis never changes
  the underlying flat element order, so it's always safe here). `squeeze(axis=k)` raises a real
  `ValueError` if that axis's size isn't actually 1, matching real numpy.
- [x] 8.7 `np.newaxis` support in indexing (`a[:, None]`). Test.
  — *Note:* `np.newaxis` is really `None` (`np.newaxis is None` in real numpy too). This needed a
  genuine extension of Phase 3's own indexing machinery: `ResolveAxes` now returns an explicit
  per-result-axis "effective stride" array (a `None`/newaxis entry gets a real stride of 0 and
  doesn't consume a source axis) instead of every result axis implicitly mapping 1:1 to
  `d.Strides` — `GetItem`/`SetItem`/`GatherRecursive`/`ScatterRecursive`/`FlatOffset` all updated to
  use it. Verified the full round trip live: `row[:, None] + row[None, :]` (outer-sum via
  broadcasting through two different newaxis insertions) produces the exact right 2-D grid.

**A note on the view/copy split landing in this phase**: `reshape`/`ravel`/`expand_dims`/`squeeze`
are real views (safe here specifically because nothing yet produces a non-contiguous array);
`flatten`/`transpose`/`.T` are real copies (matching real numpy's own actual behavior for `flatten`,
and a deliberate "copy for now" choice for `transpose` pending Phase 12's fuller strided-view work).
This is not a shortcut avoiding a decision — it's the same distinction real numpy itself makes.

## Phase 9 — dtypes & promotion ✅ (2026-08-11)

- [x] 9.1 Add the `Int64` dtype (a `long[]` buffer path). `np.array([1,2,3])` of Python ints → int64. Test.
      — *Note:* buffer storage is `long[]`; the Python-visible boundary (`GetElement`) always hands
      back a real `BigInteger` (PySharp's own actual `int` representation), never a C# `long`, so
      `type(a[0])` on an int64 element is a real Python `int`. `ArrayFromPython`'s dtype inference:
      all-`bool` → bool, all-`int`-and-fits-`long` → int64, anything else (any float leaf present) →
      float64 — this **changes** `np.array([1, 2, 3])`'s dtype from the old Phase 2 float64-only
      simplification to a real int64, matching actual numpy; the two Phase 2 tests that asserted the
      old "promotes ints to float64" behavior were updated to assert the correct int64 behavior
      instead (`NumpyConstructionTests.cs`).
- [x] 9.2 `dtype=` keyword on `array`/`zeros`/`ones`/`arange` (accept `np.int64`/`np.float64`/`'int64'`/…). Test.
      — *Note:* also wired into `full`/`empty` (not just the four named). `arange` deliberately keeps
      its *default* dtype at float64 (unlike real numpy's int64-when-all-int-args inference) rather
      than changing every existing `np.arange(6)`-based test's printed output — an explicit `dtype=`
      still selects int64. `ParseDType`/`DTypeFromName` accept either a dtype singleton or a string
      name ('float64'/'int64'/'bool'/'bool_').
- [x] 9.3 `a.astype(dtype)`. Test.
      — *Note:* shares `CoerceTo`/`AsDType` with `dtype=` construction; always returns a real
      independent copy (even same-dtype → same-dtype), matching real numpy's own `copy=True` default.
- [x] 9.4 **Promotion rules** in binary ops: `int op float → float`, `bool op int → int`, etc. Test.
      — *Note:* `PromoteForArithmetic`: float64 wins if either operand is float64; else int64 wins
      (including bool+bool → int64, matching real numpy: `np.array([True])+np.array([True])` is a
      real int64 `[2]`, not bool). `OperandData` now preserves a Python scalar's real type
      (bool/BigInteger/double) instead of always coercing to float64, so `int64_arr + 2` promotes to
      int64 not float64. Arithmetic itself is still carried out in `double` internally regardless of
      dtype (a documented v1 simplification — precision loss only shows up past `double`'s exact
      2^53 integer range). True division (`/`) always forces float64 output regardless of promotion
      via a new optional `forceDType` parameter on `ElementwiseBinary`/`ElementwiseUnary`/
      `ElementwiseOp`/`ApplyUfunc`; the same parameter forces float64 on the genuinely-float-only
      Phase 7 ufuncs (`sqrt`/`exp`/`log`/`log10`/`sin`/`cos`/`tan`/`arcsin`/`arccos`/`arctan`/
      `floor`/`ceil`, matching real numpy), while `abs`/`sign`/`round`/`clip`/`__neg__`/`__pos__`
      preserve the source dtype (unchanged from Phase 7 — `ElementwiseUnary` already preserved
      `d.DType` before this phase).
- [x] 9.5 Integer-specific ops: floor division `//`, modulo `%`, bit ops on int arrays. Test.
      — *Note:* `//`/`%` reuse `ElementwiseOp` with natural promotion (no `forceDType`) via
      `Math.Floor(x/y)` and Python's sign-of-divisor modulo (`x - Math.Floor(x/y)*y`, not C#'s
      truncating `%`) — `Interp.BinDunders` already mapped these operators from earlier phases, so no
      interpreter change was needed. The old bool-only Phase 5.3 "logical" `&`/`|`/`^` mechanism
      (`LogicalBinary`/`LogicalOp`/`RequireBoolDType`) was **replaced** (not kept alongside) by a
      unified `BitwiseBinary`/`BitwiseOp`/`AsComparableInt64` mechanism, since bitwise AND/OR/XOR on
      0/1 values produce identical results to logical AND/OR/XOR: bool-bool stays bool, anything
      involving int64 promotes to int64, and float64 correctly still raises `TypeError` (now "no
      bitwise operator" instead of "not bool" — the existing Phase 5 regression test only checks that
      a `TypeError` is raised, not its exact message, so it still passes unmodified). `__invert__`
      now branches on dtype: logical NOT for bool, real two's-complement `~` (C#'s own `~long`
      operator) for int64, `TypeError` for float64.
- [x] 9.6 dtype objects: `np.int64`, `np.float64`, `np.bool_`; `a.dtype == np.float64`. Test.
      — *Note:* `np.float64`/`np.int64`/`np.bool_` newly exposed as module-level dtype singletons
      (previously not exposed at module level at all, only reachable via `a.dtype`). `a.dtype ==
      np.float64` works "for free" through the existing `PyOps.PyEquals` reference-equality fast
      path, since each dtype is a cached singleton (`Float64DType`/`BoolDType`/`Int64DType`) — no
      dedicated `__eq__` needed on the dtype class.

Also fixed a latent dtype-mismatch bug found while touching `Concatenate` (Phase 8): it wrote through
`SetBufferElement` using the *current loop array's own* dtype instead of the actual output buffer's
dtype — harmless while every array was float64, but would corrupt/crash on mixed-dtype input now that
more than one dtype exists. Fixed by requiring all input arrays share one dtype (`concatenate` now
raises a real `TypeError` otherwise) and using that one dtype consistently for every write.

## Phase 10 — Linear algebra (basic) ✅ (2026-08-11)

- [x] 10.1 `dot(a, b)` / `a @ b` (`__matmul__`) for 1-D·1-D (inner product). Test.
      — *Note:* `dot`/`matmul`/`@` all share one `MatMul` core (real numpy's `dot` and `@` genuinely
      agree for 1-D/2-D operands, only diverging for N-D stacked batches — see 10.4). A 1-D·1-D
      result is a real scalar, not a 0-D array (`v1 @ v2` is `32.0`, not `array(32.0)`), matching
      real numpy — unwrapped via a new `MatMulResult` helper, the same pattern `ApplyUfunc`'s own
      scalar fast path already used. `@`/`__matmul__` was already wired into `Interp.BinDunders`
      since Phase 4 but deliberately left unimplemented until now.
- [x] 10.2 matmul for 2-D·2-D (matrix product). Test.
- [x] 10.3 matmul for 1-D·2-D and 2-D·1-D. Test.
      — *Note:* a 1-D operand is treated as a `(1,n)` row or `(n,1)` column (whichever side it's on),
      matmul'd, then that synthesized axis is dropped again from the result shape — real numpy's own
      rule, reused as-is for both `dot` and `@`. `MatMulOperand` also accepts a raw nested Python list
      (not just an `ndarray`), matching real numpy's own liberal `np.dot([1,2],[3,4])`.
- [x] 10.4 `np.matmul` stacked (batched) — optional; document if deferred.
      — *Decision: deferred.* `MatMul` raises a real `ValueError` for any operand with more than 2
      dimensions. No scenario in this repo currently needs N-D batched matmul; real numpy's own
      batching rule (broadcast every leading axis, matmul the trailing two) is a genuine additional
      algorithm, not a small extension of the 1-D/2-D core here — left for a future phase if a real
      use case shows up.
- [x] 10.5 `np.linalg.norm`, `np.trace`, `np.diagonal`. Test.
      — *Note:* `np.linalg` is a real nested submodule (the same `os.path` pattern: an attribute on
      `numpy`'s own module dict, also separately registered in `StdlibModules.cs` so `import
      numpy.linalg`/`from numpy.linalg import norm` both work). `norm` (no `ord`/`axis` — out of v1
      scope) is `sqrt(sum(x_i^2))` over every element, which is simultaneously the correct formula for
      a 1-D vector's 2-norm *and* a 2-D matrix's Frobenius norm — one implementation, no `ndim`
      branch needed. `trace`/`diagonal` require a real 2-D array (documented v1 simplification) and
      share one `Diagonal` helper; `diagonal` returns a real copy (not a view — same "views are
      Phase 12's job" reasoning as `flatten`/`transpose` elsewhere in this shim). Module-level only
      (no `a.trace()`/`a.diagonal()` instance methods) — not asked for by this checklist item, kept
      out of scope.
- [x] 10.6 (Optional) back `np.linalg.inv`/`solve`/`det` with a small C# implementation or a .NET
  numerics library injected via interop. Decide and document at this step.
      — *Decision: deferred, not implemented.* A real, general `N×N` inverse/solve/determinant needs
      actual numerical linear algebra (LU decomposition with partial pivoting, condition-aware
      solving) — a meaningfully sized standalone piece of work, not a small addition to this phase's
      1-D/2-D matmul core, and nothing in this repo's own scenarios currently calls for it. Revisit as
      its own phase/PR if a real use case appears; `np.linalg.norm`/`np.trace`/`np.diagonal` above
      cover what Phase 10 actually needed.

## Phase 11 — Interop & conveniences ✅ (2026-08-11)

- [x] 11.1 `a.tolist()` → nested Python lists (round-trips `np.array(a.tolist())`). Test.
      — *Note:* a 0-D array's `tolist()` is the bare scalar itself, not a 1-element list, matching
      real numpy. Leaves are already real Python values (`GetElement`'s own boxed type) — this shim
      never had a separate "numpy scalar" wrapper type to unwrap.
- [x] 11.2 Iteration `for row in a` (yields scalars for 1-D, sub-arrays for N-D). Test.
      — *Note:* needed **no new code at all** — `PyOps.GetIter`'s existing generic fallback (an
      object with `__getitem__` but no `__iter__` gets iterated via repeated `__getitem__(0)`,
      `__getitem__(1)`, ... until `IndexError`) already produces exactly real numpy's own iteration
      behavior for `ndarray`, since Phase 3's indexing already returns a scalar for a 1-D `a[i]` and a
      real sub-array for an N-D one. Verified live and added a regression test to lock the behavior
      in, since nothing here is `ndarray`-specific and a future refactor could silently break it.
- [x] 11.3 `float(a)`/`int(a)` for size-1 arrays; `bool(a)` for size-1; `ValueError` otherwise. Test.
      — *Note:* deliberately deviates from this checklist line's own wording once real numpy's actual
      behavior was checked: `float(a)`/`int(a)` on a multi-element array is a real `TypeError` in
      real numpy ("only size-1 arrays can be converted to Python scalars"), while `bool(a)` is the
      one that's a real `ValueError` ("truth value of an array... is ambiguous") — two different
      exception types, not one `ValueError` for all three. This shim's own standing discipline
      (verify against real numpy semantics) took priority over the plan text. `int()` truncates
      toward zero regardless of source dtype (`int(np.array([-3.7]))` is `-3`).
- [x] 11.4 `np.random`: `seed`, `rand`, `randn`, `randint`, `choice` (small, deterministic with seed). Test.
      — *Note:* `numpy.random` is a real nested submodule (the `linalg`/`os.path` pattern). The RNG
      is a plain C# `System.Random` (Box-Muller for `randn`, since `Random` has no Gaussian sampler)
      — `seed(n)` makes *this shim's own* sequence reproducible run-to-run, but does not (and can't,
      without porting numpy's real Mersenne Twister/PCG64 bit-generator) reproduce real numpy's exact
      values for a given seed. That matches the checklist's own "small, deterministic with seed"
      wording, not a "bit-identical to real numpy" claim. `choice` supports `replace=True/False`;
      weighted sampling (`p=`) is out of v1 scope.
- [x] 11.5 Bridge to the .NET interop: allow a `ClrObject` wrapping a .NET array/`double[]` to be
  passed to `np.array(...)` and an ndarray to expose `.to_clr()` → `double[]`. Test.
      — *Note:* both directions are 1-D only (matching the checklist's own literal `double[]`
      wording). `np.array(clr_array)` normalizes a `ClrObject` wrapping any 1-D .NET array
      (`double[]`/`int[]`/`long[]`/`bool[]`/...) into a `PyList` up front (each element passed through
      the existing `ClrMarshal.ToPython`), then hands off to `ArrayFromPython`'s already-existing
      shape/dtype-inference machinery unchanged — so a real .NET `int[]` correctly infers `int64`,
      not just `double[]` → `float64`. `.to_clr()` converts every element through the same
      dtype-generic `AsComparableDouble` reader used elsewhere in this file, so an int64 array
      round-trips as a real `double[]` on the .NET side. Verified with real host-side C# tests
      (`PyEngine.SetVariable`/reading `PyModule.Dict` back), not just `.py` source string assertions,
      since this feature's entire point is the two-way boundary with real .NET types.

## Phase 12 — Views, performance, polish (optional / later)

- [ ] 12.1 Real strided **views** for slices and `.T` (share the buffer; add a `Base`), with a
  copy-on-demand fallback. Update the affected tests. Document the semantics change.
- [ ] 12.2 Fast paths for contiguous float64 (avoid per-element boxing; tight loops). Benchmark.
- [ ] 12.3 `samples/numpy_demo.py` — a realistic script (construct, broadcast, reduce, matmul,
  masking) run end-to-end by PySharp; verify with the console host.
- [ ] 12.4 ROADMAP: add scenario "N — array computing (numpy shim)"; RELEASE_NOTES entry; README
  "Verified scenarios" update; bump packages and re-pack; reinstall the tool.
- [ ] 12.5 Sweep the numpy corpus-style snippets (a handful adapted from numpy's quickstart) as an
  end-to-end conformance test group.

---

## Rough size & sequencing notes

- Phases 1–7 deliver a genuinely useful float64 numpy (construct → index → math → broadcast →
  reduce → ufunc). That is the **minimum viable numpy** and a good first release milestone.
- Phase 4.4/4.5 (broadcasting) and Phase 10 (matmul) are the algorithmically meatiest; everything
  else is mostly plumbing over `NdArrayData`.
- Nothing here requires changing the interpreter core **if** the `ndarray = PyInstance of a PyClass`
  decision holds (only the tiny `@ → __matmul__` operator-map check in 4.1 might).
- Suggested first milestone tag: **numpy-mvp** after Phase 7; second: **numpy-linalg** after Phase 10.

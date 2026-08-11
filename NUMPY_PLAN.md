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

## Phase 5 — Comparisons, bool arrays, masking

- [ ] 5.1 Add the `Bool` dtype (a `bool[]` buffer path in `NdArrayData` get/set + repr). Test repr.
- [ ] 5.2 Comparisons `== != < <= > >=` (array vs array, array vs scalar) → **bool** ndarray. Test.
- [ ] 5.3 Logical `&` `|` `~` on bool arrays (`__and__`/`__or__`/`__invert__`). Test.
- [ ] 5.4 `a.any()`, `a.all()`. Test.
- [ ] 5.5 Boolean-mask read: `a[mask]` → 1-D array of selected elements. Test.
- [ ] 5.6 Boolean-mask assign: `a[mask] = value`. Test.
- [ ] 5.7 `np.where(cond, x, y)` (broadcasted). Test.

## Phase 6 — Reductions

- [ ] 6.1 `a.sum()` / `np.sum(a)` over all elements. Test.
- [ ] 6.2 `sum(axis=k)` for N-D (single axis) → reduced array. Test 2-D both axes.
- [ ] 6.3 `mean` (all + axis). Test.
- [ ] 6.4 `min`/`max` (all + axis) and `np.min`/`np.max`. Test.
- [ ] 6.5 `prod`, `std`, `var` (all + axis). Test.
- [ ] 6.6 `argmin`/`argmax` (all + axis). Test.
- [ ] 6.7 `cumsum`/`cumprod` (1-D first, then axis). Test.

## Phase 7 — Universal functions (ufuncs)

- [ ] 7.1 A ufunc factory: apply a `Func<double,double>` elementwise, returning a new array. Internal.
- [ ] 7.2 `np.sqrt`, `np.exp`, `np.log`, `np.log10`, `np.abs`. Test.
- [ ] 7.3 `np.sin`, `np.cos`, `np.tan`, `np.arcsin/arccos/arctan`. Test.
- [ ] 7.4 `np.floor`, `np.ceil`, `np.round`, `np.sign`, `np.clip(a, lo, hi)`. Test.
- [ ] 7.5 `np.minimum`, `np.maximum`, `np.power` (binary, broadcasted). Test.
- [ ] 7.6 Constants: `np.pi`, `np.e`, `np.inf`, `np.nan`. Test.

## Phase 8 — Shape manipulation

- [ ] 8.1 `reshape(shape)` / `np.reshape` (product must match; `-1` inferred dim). Test.
- [ ] 8.2 `ravel()` / `flatten()` → 1-D. Test.
- [ ] 8.3 `.T` and `transpose(axes)` (permute strides or materialize a copy). Test 2-D and 3-D.
- [ ] 8.4 `concatenate([a, b], axis)`. Test.
- [ ] 8.5 `stack`, `vstack`, `hstack`. Test.
- [ ] 8.6 `expand_dims(a, axis)`, `squeeze(a)`. Test.
- [ ] 8.7 `np.newaxis` support in indexing (`a[:, None]`). Test.

## Phase 9 — dtypes & promotion

- [ ] 9.1 Add the `Int64` dtype (a `long[]` buffer path). `np.array([1,2,3])` of Python ints → int64. Test.
- [ ] 9.2 `dtype=` keyword on `array`/`zeros`/`ones`/`arange` (accept `np.int64`/`np.float64`/`'int64'`/…). Test.
- [ ] 9.3 `a.astype(dtype)`. Test.
- [ ] 9.4 **Promotion rules** in binary ops: `int op float → float`, `bool op int → int`, etc. Test.
- [ ] 9.5 Integer-specific ops: floor division `//`, modulo `%`, bit ops on int arrays. Test.
- [ ] 9.6 dtype objects: `np.int64`, `np.float64`, `np.bool_`; `a.dtype == np.float64`. Test.

## Phase 10 — Linear algebra (basic)

- [ ] 10.1 `dot(a, b)` / `a @ b` (`__matmul__`) for 1-D·1-D (inner product). Test.
- [ ] 10.2 matmul for 2-D·2-D (matrix product). Test.
- [ ] 10.3 matmul for 1-D·2-D and 2-D·1-D. Test.
- [ ] 10.4 `np.matmul` stacked (batched) — optional; document if deferred.
- [ ] 10.5 `np.linalg.norm`, `np.trace`, `np.diagonal`. Test.
- [ ] 10.6 (Optional) back `np.linalg.inv`/`solve`/`det` with a small C# implementation or a .NET
  numerics library injected via interop. Decide and document at this step.

## Phase 11 — Interop & conveniences

- [ ] 11.1 `a.tolist()` → nested Python lists (round-trips `np.array(a.tolist())`). Test.
- [ ] 11.2 Iteration `for row in a` (yields scalars for 1-D, sub-arrays for N-D). Test.
- [ ] 11.3 `float(a)`/`int(a)` for size-1 arrays; `bool(a)` for size-1; `ValueError` otherwise. Test.
- [ ] 11.4 `np.random`: `seed`, `rand`, `randn`, `randint`, `choice` (small, deterministic with seed). Test.
- [ ] 11.5 Bridge to the .NET interop: allow a `ClrObject` wrapping a .NET array/`double[]` to be
  passed to `np.array(...)` and an ndarray to expose `.to_clr()` → `double[]`. Test.

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

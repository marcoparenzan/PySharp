# ctypes deepening — a short, step-by-step plan

**Goal.** ROADMAP.md's scenario T ("Native libraries", cross-cutting) is 🟡 Partial: `ctypes`
currently only supports scalar arguments/returns (ints, floats, `char*`/`wchar*` strings) — real,
verified against real Windows DLLs (`kernel32`, `msvcrt`), but "pointers to structs and callbacks are
out of scope for v1" per `CtypesModule.cs`'s own doc comment. Most *rich* native APIs need at least
structs and output pointers (`GetSystemInfo`, `GetComputerNameW`, …) — this plan closes that gap.

**Scope for this round**: `ctypes.Structure` (real per-field storage, real layout), `ctypes.byref`,
`ctypes.POINTER`, `create_string_buffer`/`create_unicode_buffer`. **Deliberately out of scope**:
by-value struct passing (real ABI complexity — x64 struct-by-value has register-vs-stack rules this
shim doesn't need yet), `CFUNCTYPE`/callbacks (a separate, larger chunk — native code calling back
into a Python function needs careful delegate-lifetime management; revisit as its own follow-up if a
real scenario needs it), generic `ctype * N` array syntax (needs `PyClass`-level operator dispatch
the interpreter doesn't have; `create_*_buffer` covers the dominant real use case instead).

**Design decision**: every ctypes value (`c_int`, `c_ulong`, a `Structure` subclass, …) is backed by a
real C# `byte[]` buffer (not `Marshal.AllocHGlobal`) — reading/writing a field decodes/encodes bytes
at a computed offset (`BitConverter`), and passing something `byref` pins that same managed array
(`GCHandle.Alloc(..., Pinned)`) for the duration of the native call. A native function's writes land
directly in the pinned managed buffer, so there's no separate "marshal back" step — reading a field
afterward already sees the update. This reuses the *same* buffer identity throughout a value's
lifetime (unlike the current scalar `c_*.__init__`, which just stores a raw Python value in
`.Dict["value"]` — that changes here, but observably `.value` still reads/writes the same way).

## Phase 1 — Structure, byref, POINTER, buffers ✅ (2026-08-11)

- [x] 1.1 Redesign `c_*` scalar instances to be buffer-backed (`byte[]` sized to the type, `.value`
  becomes a real read/write through it) instead of a bare `.Dict["value"]`. Keep existing
  `CtypesTests.cs` green (no observable behavior change for plain scalar use).
      — *Note:* only the numeric/pointer-sized `c_*` types (`c_int`, `c_ulong`, `c_void_p`, …) became
      buffer-backed. `c_char_p`/`c_wchar_p` deliberately stayed exactly as before (still
      `.Dict["value"]`-based, not buffer-backed) — they're used almost exclusively as direct call
      arguments, where the existing string-marshalling already did the right thing, and real
      out-parameter string buffers are `create_string_buffer`/`create_unicode_buffer` instead (a
      cleaner match to how real ctypes scripts actually use them). All 7 pre-existing tests passed
      unchanged.
- [x] 1.2 `ctypes.Structure` base class: subclasses declare `_fields_ = [("x", c_int), ...]`; real
  per-field storage via `__getattr__`/`__setattr__` dispatch against a computed offset table (natural
  alignment, no explicit packing). `ctypes.sizeof(SomeStruct)`/`ctypes.sizeof(instance)` both work.
      — *Note:* also supports real positional and keyword field initialization
      (`POINT(3, 4)`/`POINT(x=10, y=20)`), matching real ctypes' own `Structure.__init__`. The
      natural-alignment auto-layout algorithm produced the *exact* real Windows `SYSTEM_INFO` layout
      (48 bytes) with zero hand-tuning — confirms the algorithm matches the real C compiler's own
      layout rules for the structs this shim will actually be asked to describe.
- [x] 1.3 `ctypes.byref(x)` — pins `x`'s buffer for one call; works for both scalar `c_*` and
  `Structure` instances.
- [x] 1.4 `ctypes.POINTER(sometype)` — a type factory usable in `argtypes`/`restype`; a
  `POINTER`-typed argument accepts a `byref(...)` result or `None`.
- [x] 1.5 `ctypes.create_string_buffer(n)` / `create_unicode_buffer(n)` — a real mutable native
  buffer, the idiomatic ctypes way to receive a string a native function writes into.
      — *Note:* matches real ctypes' actual type split — `create_string_buffer(...).value` is real
      `bytes`, `create_unicode_buffer(...).value` is a real `str` (caught and fixed before shipping:
      an early version returned `str` for both, verified wrong by checking what real ctypes actually
      returns, not just "does it print a string"). Accepts an int size, a `str`, or `bytes` as the
      initializer.
- [x] 1.6 Verify against two real Windows APIs requiring structs/pointers, end to end (not just
  "doesn't crash" — decode and print real field values, confirm they're sane):
  `kernel32!GetSystemInfo` (fills a `SYSTEM_INFO` struct via a pointer) and
  `kernel32!GetComputerNameW` (fills a wchar buffer + updates a `byref` `DWORD` size). Tests in
  `M8_Ctypes`.
      — *Note:* verified against real, fixed OS constants, not just "didn't crash": `dwPageSize` ==
      4096, `dwAllocationGranularity` == 65536, `wProcessorArchitecture` == 9
      (`PROCESSOR_ARCHITECTURE_AMD64`) — all well-known, independently-checkable values on any real
      x64 Windows machine. **Found and fixed a real bug this same verification pass surfaced**: the
      original design used mutable `static PyClass` fields (`StructureClass`/`ByRefClass`/
      `CharBufferClass`) reassigned on every `Create()` call (once per `import ctypes`); xUnit runs
      tests in parallel, so one test's concurrent `import ctypes` silently overwrote another test's
      in-flight class identity mid-script, breaking `cls.Mro.Contains(structureClass)`-style checks
      with a confusing `TypeError`. Fixed by making every ctypes-specific class a real local variable
      inside `Create()`, threaded through as an explicit parameter to every method that needs it —
      never a static field. Confirmed fixed via 6 consecutive clean `M8_Ctypes` runs plus 3 consecutive
      clean full-suite runs (1233/1233).
- [x] 1.7 Docs: ROADMAP.md scenario T status update, `Modules/CtypesModule.cs`'s own doc comment.

## Phase 2 — callbacks (optional, only if a real scenario needs it)

- [ ] 2.1 `ctypes.CFUNCTYPE`/`WINFUNCTYPE`: wrap a Python callable as a native function pointer
  (`Marshal.GetFunctionPointerForDelegate` on a dynamically-built delegate; the delegate must be kept
  alive — GC-rooted — for as long as native code might call it).

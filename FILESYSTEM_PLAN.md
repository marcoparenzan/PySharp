# File system API — scenario 8 — a step-by-step plan

**Goal.** Get a real file-organizer script running end-to-end under PySharp — `os.walk`, `glob`,
`shutil`, and `pathlib.Path` all touching the real filesystem together — following the same
scenario-driven method as everywhere else: real script, real gap, real fix, real test, repeat. See
ROADMAP.md's "Method: scenario-driven development".

**Status: ✅ done (2026-08-10).** [samples/filesystem_demo.py](samples/filesystem_demo.py) runs
clean end-to-end: builds a real temp directory tree, walks it, finds files two different ways
(`glob.glob(..., recursive=True)` and `Path.rglob(...)`), packages a release directory, then
reorganizes and cleans up. Full blow-by-blow below.

---

## Starting point

Before this scenario: `os`/`os.path` had a real but partial subset (no `walk`, `relpath`, fspath
coercion, ...), `pathlib.Path` had a real core (join/name/stem/suffix/parent/str/repr/exists/
is_file/is_dir/mkdir/open/read_text/write_text/expanduser/resolve) but no glob-family methods — its
own docstring already flagged this as a deliberate v1 gap — and **`shutil`/`glob` didn't exist at
all**. Established via a targeted Explore-agent survey before writing the sample script, rather than
re-reading every file by hand.

## Verification method

No local Python interpreter is available, so every fix below was verified by running the real
script and reading the actual traceback PySharp produced, then reasoning through real CPython
semantics (checked against real pathlib/glob/shutil documented behavior) before implementing —
the same "run it, see what breaks, fix, repeat" loop used for every other scenario. A standalone
probe script (`probe_glob.py`) isolated `glob`/`os.path.relpath`/`shutil.rmtree` before wiring them
into the full sample.

---

## What was found and fixed

### New modules built from scratch
- **`glob`** ([GlobModule.cs](src/PySharpLib/Modules/GlobModule.cs)) — didn't exist at all
  (`ModuleNotFoundError`). Built as a real segment-by-segment directory walk: each path component is
  either literal (checked for real existence), a wildcard (`*`/`?`/`[...]` translated to a regex and
  matched against real `Directory.EnumerateFileSystemEntries()` results), or a bare `**` (only
  wildcard-active when `recursive=True`, matching real CPython — expands to "this directory and every
  real descendant directory, at any depth, including itself"). `glob`/`iglob`/`escape`/`has_magic`
  all real, not pattern-string generators — every returned path is checked against the real
  filesystem.
- **`shutil`** ([ShutilModule.cs](src/PySharpLib/Modules/ShutilModule.cs)) — didn't exist at all.
  Built as thin, real wrappers over `File.Copy`/`Directory.CreateDirectory`/`Directory.Delete`/
  `File.Move`/`DriveInfo`: `copy`/`copy2`/`copyfile`/`copytree`/`rmtree`/`move`/`which`/
  `disk_usage`, plus a real `SameFileError`. `move` matches real CPython's own `os.rename` → `EXDEV`
  → copy-then-delete fallback for cross-volume moves, and moving into an existing directory target
  (`move(src, dst)` where `dst` is a directory moves `src` *inside* it). `which` honors `PATHEXT` on
  Windows, matching real CPython's own algorithm.

### A pervasive, previously-unexercised interpreter gap
- **No `os`/`os.path` function coerced a path-like (`__fspath__`) argument.** Every prior scenario
  had only ever passed plain strings. Surfaced live: `os.path.relpath(p, root)` where `root` is a
  real `pathlib.Path` object (the file-organizer script's own natural style) raised a misleading
  `TypeError: relpath(): invalid argument type` (the `CallCore` catch-all masking an
  `InvalidCastException` from an unconditional `(string)a[N]` cast). Fixed with a new
  `internal static string PathArg(Interp interp, object o)` helper in `OsModule.cs` — `string` as-is,
  else calls the object's real `__fspath__()` — and a full-file rewrite of `OsModule.cs` so every
  path-taking function in both `os` and `os.path` routes through it instead of casting directly.

### `pathlib.Path` glob-family and ordering
- **`Path.rglob()`/`Path.glob()` didn't exist.** Implemented by reusing `glob.iglob()`'s own real
  filesystem walk (`GlobModule.Iglob`, made `internal` for this) rather than duplicating it:
  `rglob(pattern)` == real CPython's `glob(f"**/{pattern}", recursive=True)`; `glob(pattern)` only
  walks recursively when the pattern itself contains `**` (real pathlib semantics — distinct from the
  `glob` module's own opt-in `recursive=` flag).
- **`Path.iterdir()` didn't exist** — one `Path` per real entry directly inside a directory, backed
  by `Directory.EnumerateFileSystemEntries`.
- **`Path.relative_to()` didn't exist** — computed purely from the (normalized, absolute) path
  strings, no filesystem access; raises a real `ValueError` when the path isn't actually inside
  `other`, matching real CPython.
- **`sorted()` over a list of `Path` objects crashed** (`error: Failed to compare two elements in the
  array` — a raw .NET `Array.Sort` comparer failure) since `Path` had no `__lt__`/etc. Real pathlib
  orders by the parts tuple; string comparison of the already-normalized path matches that for every
  case reachable here, so `__lt__`/`__le__`/`__gt__`/`__ge__` were added as ordinal string
  comparisons.

**Deliberately out of scope for v1** (practical-subset philosophy, matching every other module in
this project): `shutil.Error` aggregating multiple sub-failures during `copytree`/`rmtree` (a single
real exception propagates instead, since nothing reachable relies on partial-failure tolerance),
`copytree`'s `ignore_patterns`/`copy_function` customization, `shutil.make_archive`/`unpack_archive`,
symlink-specific edge cases (`os.path.islink`/`os.readlink` exist from prior work but nothing here
exercises actually creating one).

---

## Deliverables

- **Modules**: [GlobModule.cs](src/PySharpLib/Modules/GlobModule.cs) (new),
  [ShutilModule.cs](src/PySharpLib/Modules/ShutilModule.cs) (new); `OsModule.cs` (full-file rewrite,
  adding `PathArg` fspath coercion plus `walk`/`relpath`/`isabs`/`split`/`splitdrive`/`normcase`/
  `islink`/`lexists`/`samefile`/`getmtime`/`getatime`/`getctime`/`chdir`/`mkdir`/`unlink`);
  `PathlibModule.cs` (`glob`/`rglob`/`iterdir`/`relative_to`/ordering operators);
  `StdlibModules.cs` (registrations for `glob`/`shutil`).
- **Sample**: [samples/filesystem_demo.py](samples/filesystem_demo.py) — a real file-organizer:
  builds a real temp tree, walks it, finds files via `glob` and `Path.rglob`, packages a release
  directory (`shutil.copy2`/`copytree`), reorganizes it (`shutil.rmtree`/`move`), reports real disk
  usage and a real `shutil.which` hit, cleans up — every line run live against the real filesystem.
- **Tests**: [FilesystemTests.cs](src/PySharp.Tests/M19_Filesystem/FilesystemTests.cs), 7 tests
  against real temp directories (glob non-recursive/recursive, shutil copy/move/rmtree round-trip,
  disk_usage/which, `os.path` fspath coercion of a real `Path` argument, `Path.rglob`, `Path.iterdir`
  + `relative_to` + sorting, `relative_to` raising `ValueError` on a non-parent).
- Full suite green at **1079/1079**, confirmed via 5 consecutive full-suite runs (warranted given the
  `OsModule.cs` full-file rewrite touched every path-taking function's signature).

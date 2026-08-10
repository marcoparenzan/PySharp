# Copyright (c) 2026 Marco Parenzan
#
# Licensed under the MIT License. See the LICENSE file in the project
# root for full license information.

# filesystem_demo.py — a real file-organizer script exercising os/os.path/pathlib/glob/shutil
# together, run entirely by PySharp against the real filesystem (a throwaway temp directory tree
# it creates and tears down itself, so it's safe to run anywhere).
#
# Scenario 8 of the roadmap (File system API). os/io/open and a real (if partial) pathlib already
# existed; this scenario completes os.path, adds a real glob module and Path.glob()/rglob()/
# iterdir(), and adds shutil (copy/copytree/rmtree/move/which/disk_usage) — real filesystem
# operations, not stubs.
#
# Usage:  pysharp run samples/filesystem_demo.py

import os
import glob
import shutil
import tempfile
from pathlib import Path

root = Path(tempfile.mkdtemp(prefix="pysharp_fs_demo_"))
print("working in:", root)

# Build a small real project tree.
(root / "src").mkdir()
(root / "src" / "pkg").mkdir()
(root / "docs").mkdir()
(root / "build").mkdir()

(root / "src" / "main.py").write_text("print('hello')\n")
(root / "src" / "pkg" / "__init__.py").write_text("")
(root / "src" / "pkg" / "utils.py").write_text("def helper():\n    pass\n")
(root / "docs" / "readme.md").write_text("# Demo project\n")
(root / "build" / "cache.tmp").write_text("stale\n")

print("\n--- os.walk over the tree ---")
file_count = 0
for dirpath, dirnames, filenames in os.walk(root):
    rel = os.path.relpath(dirpath, root)
    for name in sorted(filenames):
        file_count += 1
        print(f"  {os.path.join(rel, name)}")
print("total files:", file_count)

print("\n--- glob: every real .py file, recursively ---")
py_files = sorted(glob.glob(str(root / "**" / "*.py"), recursive=True))
for p in py_files:
    print(" ", os.path.relpath(p, root))

print("\n--- pathlib: the same search via Path.rglob() ---")
rglob_files = sorted(str(p.relative_to(root)) for p in root.rglob("*.py"))
print(" ", rglob_files)

print("\n--- shutil: package a real release directory ---")
release = root / "release"
release.mkdir()
for py in root.rglob("*.py"):
    dest = release / py.relative_to(root / "src")
    dest.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(py, dest)
shutil.copytree(root / "docs", release / "docs")
print("release contents:")
for p in sorted(release.rglob("*")):
    if p.is_file():
        print("  ", p.relative_to(release))

print("\n--- cleanup: remove the stale build dir, move release into its place ---")
shutil.rmtree(root / "build")
shutil.move(str(release), str(root / "build"))
print("build/ now contains:", sorted(p.name for p in (root / "build").iterdir()))

usage = shutil.disk_usage(root)
print("\ndisk usage for this volume: total > used > 0:", usage.total > usage.used > 0)

python_on_path = shutil.which("pysharp") or shutil.which("dotnet")
print("shutil.which finds something real on PATH:", python_on_path is not None)

shutil.rmtree(root)
print("\ncleaned up:", not root.exists())
print("filesystem demo: ok")

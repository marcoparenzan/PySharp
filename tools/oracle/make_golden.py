"""Computes the expected stdout of every snippet under src/PySharp.Tests/Oracle/<lib>/ by running it
with REAL CPython + numpy/OpenCV/... (the tools/oracle venv). Writes <snippet>.expected next to it.
Snippets that fail under CPython are listed (and any stale .expected removed): they are either
PySharp-specific features or snippets that need fixing.

    tools/oracle/.venv/Scripts/python.exe tools/oracle/make_golden.py [subdir ...]
"""
import os, subprocess, sys, glob

root = os.path.normpath(os.path.join(os.path.dirname(__file__), "..", ".."))
base = os.path.join(root, "src", "PySharp.Tests", "Oracle")
dirs = sys.argv[1:] or [d for d in os.listdir(base) if os.path.isdir(os.path.join(base, d))]
failed = []
for d in dirs:
    for path in sorted(glob.glob(os.path.join(base, d, "*.py"))):
        env = dict(os.environ, PYTHONIOENCODING="utf-8", PYTHONHASHSEED="0", MPLBACKEND="Agg")
        r = subprocess.run([sys.executable, path], capture_output=True, text=True, encoding="utf-8", cwd=os.path.dirname(path), env=env)
        exp = path[:-3] + ".expected"
        if r.returncode != 0:
            failed.append((os.path.relpath(path, base), r.stderr.strip().splitlines()[-1] if r.stderr.strip() else "?"))
            if os.path.exists(exp): os.remove(exp)
            continue
        open(exp, "w", encoding="utf-8", newline="\n").write(r.stdout)
print("golden files written; CPython rejected", len(failed), "snippet(s):")
for p, e in failed: print("  ", p, "->", e[:150])

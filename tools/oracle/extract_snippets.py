"""One-off migration helper: pulls every Python snippet (C# raw string literal containing
`import numpy`) out of the old hand-written M14_Numpy tests into src/PySharp.Tests/Oracle/numpy/*.py,
so make_golden.py can compute their expected output with REAL numpy."""
import re, os, sys, glob

root = os.path.normpath(os.path.join(os.path.dirname(__file__), "..", ".."))
src = os.path.join(root, "src", "PySharp.Tests", "M14_Numpy")
out = os.path.join(root, "src", "PySharp.Tests", "Oracle", "numpy")
os.makedirs(out, exist_ok=True)

count = 0
for path in sorted(glob.glob(os.path.join(src, "*.cs"))):
    text = open(path, encoding="utf-8").read()
    base = os.path.splitext(os.path.basename(path))[0].replace("Numpy", "").replace("Tests", "")
    # raw string literals: opening """ then newline ... newline <indent>"""
    for m in re.finditer(r'"""\r?\n(.*?)\r?\n([ \t]*)"""', text, re.S):
        body, indent = m.group(1), m.group(2)
        if "numpy" not in body:
            continue
        lines = [l[len(indent):] if l.startswith(indent) else l.lstrip() for l in body.splitlines()]
        # nearest preceding test method name
        names = re.findall(r'public (?:async )?(?:void|Task) (\w+)\(', text[:m.start()])
        name = names[-1] if names else "snippet"
        fn = f"{base}__{name}.py"
        if os.path.exists(os.path.join(out, fn)):
            fn = f"{base}__{name}_{count}.py"
        open(os.path.join(out, fn), "w", encoding="utf-8", newline="\n").write("\n".join(lines) + "\n")
        count += 1
print(count, "snippets ->", out)

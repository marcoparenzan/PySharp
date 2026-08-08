// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using PySharpLib.Interpretation;
using PySharpLib.Runtime;

namespace PySharpLib.Modules;

/// <summary>traceback: format_exc/print_exc/format_exception, backed by the interpreter's own real
/// per-frame traceback data (PyErr.FormatTraceback — the exact same formatting the REPL/CLI use for
/// an uncaught exception), not a stub. Found via starlette's real `traceback.format_exc()`
/// (routing.py's exception-handling middleware), reachable from `import starlette`. See
/// FASTAPI_PLAN.md Phase 3.</summary>
public static class TracebackModule
{
    public static PyModule Create(Interp interp)
    {
        var m = new PyModule("traceback");
        var d = m.Dict;

        d["format_exc"] = new PyBuiltinFunction("format_exc", (interp2, _, _) =>
            interp2.CurrentHandledException is { } ex ? PyErr.FormatTraceback(ex) : "NoneType: None\n");

        d["print_exc"] = new PyBuiltinFunction("print_exc", (interp2, _, _) =>
        {
            if (interp2.CurrentHandledException is { } ex)
                Console.Error.WriteLine(PyErr.FormatTraceback(ex));
            return PyNone.Instance;
        });

        // format_exception(exc) or the legacy 3-arg format_exception(etype, value, tb) — both real
        // CPython call shapes; here `tb` is just the exception's own PyRaise (see sys.exc_info).
        d["format_exception"] = new PyBuiltinFunction("format_exception", (_, a, _) =>
        {
            PyRaise? ex = a.Length switch
            {
                >= 3 when a[2] is PyRaise pr => pr,
                >= 2 when a[1] is PyInstance legacyInst => new PyRaise(legacyInst),
                >= 1 when a[0] is PyInstance inst => new PyRaise(inst),
                _ => null,
            };
            string text = ex is not null ? PyErr.FormatTraceback(ex) : "NoneType: None\n";
            return new PyList(text.TrimEnd('\n').Split('\n').Select(line => (object)(line + "\n")));
        });

        d["format_exception_only"] = new PyBuiltinFunction("format_exception_only", (interp2, a, _) =>
        {
            var inst = a.Length >= 2 ? a[1] : a[0];
            string line = inst is PyInstance pi ? PyErr.FormatForClr(pi) : PyOps.Str(interp2, inst);
            return new PyList(new object[] { line + "\n" });
        });

        return m;
    }
}

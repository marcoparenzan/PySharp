// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using PySharpLib.Interpretation;
using PySharpLib.Runtime;

namespace PySharpLib.Modules;

/// <summary>atexit: real `register`/`unregister`, with registered callbacks actually invoked (in
/// reverse registration order, matching real CPython) when the top-level script finishes — see
/// PyEngine.Run's call to <see cref="RunAtExit"/>. The callback list lives on the `atexit` PyModule
/// instance itself (not a shared static), so it's naturally scoped per `PyEngine`/per script run —
/// the same lesson this project already learned the hard way from earlier flaky-suite concurrency
/// bugs (a process-wide shared mutable list here would race the same way under parallel test
/// execution). Found via real certifi (an httpx transitive dependency).</summary>
public static class AtexitModule
{
    private const string CallbacksKey = "__callbacks__";

    public static PyModule Create()
    {
        var m = new PyModule("atexit");
        m.Dict[CallbacksKey] = new List<(object Func, object[] Args, Dictionary<string, object>? Kwargs)>();

        m.Dict["register"] = new PyBuiltinFunction("register", (_, a, kwargs) =>
        {
            Callbacks(m).Add((a[0], a.Skip(1).ToArray(), kwargs));
            return a[0];
        });

        m.Dict["unregister"] = new PyBuiltinFunction("unregister", (_, a, _) =>
        {
            Callbacks(m).RemoveAll(c => ReferenceEquals(c.Func, a[0]) || Equals(c.Func, a[0]));
            return PyNone.Instance;
        });

        return m;
    }

    private static List<(object Func, object[] Args, Dictionary<string, object>? Kwargs)> Callbacks(PyModule m)
        => (List<(object, object[], Dictionary<string, object>?)>)m.Dict[CallbacksKey];

    /// <summary>Invokes every registered callback in reverse registration order, matching real
    /// CPython's interpreter-shutdown behavior. A callback that raises is swallowed (real CPython
    /// prints its traceback to stderr and keeps going) rather than aborting the remaining ones.</summary>
    public static void RunAtExit(Interp interp, PyModule atexitModule)
    {
        var list = Callbacks(atexitModule);
        for (int i = list.Count - 1; i >= 0; i--)
        {
            try
            {
                interp.Call(list[i].Func, list[i].Args, list[i].Kwargs);
            }
            catch (PyRaise)
            {
                // matches real CPython: an exception from one atexit callback doesn't stop the rest
            }
        }
    }
}

// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using PySharpLib.Runtime;

namespace PySharpLib.Modules;

/// <summary>bisect: real bisect_left/bisect_right (bisect)/insort_left/insort_right (insort) —
/// direct ports of CPython's own Lib/bisect.py algorithm. Found via real idna's `intranges.py`/
/// `core.py` (`bisect.bisect_left`/`bisect.bisect_right`, binary-searching a Unicode codepoint
/// range table), a real transitive dependency of httpx.</summary>
public static class BisectModule
{
    public static PyModule Create()
    {
        var m = new PyModule("bisect");
        var d = m.Dict;

        d["bisect_left"] = new PyBuiltinFunction("bisect_left", (interp, a, kwargs) =>
            new System.Numerics.BigInteger(BisectLeft(interp, a, kwargs)));
        d["bisect_right"] = new PyBuiltinFunction("bisect_right", (interp, a, kwargs) =>
            new System.Numerics.BigInteger(BisectRight(interp, a, kwargs)));
        d["bisect"] = d["bisect_right"];

        d["insort_left"] = new PyBuiltinFunction("insort_left", (interp, a, kwargs) =>
        {
            var list = (PyList)a[0];
            int i = BisectLeft(interp, a, kwargs);
            list.Items.Insert(i, a[1]);
            return PyNone.Instance;
        });
        d["insort_right"] = new PyBuiltinFunction("insort_right", (interp, a, kwargs) =>
        {
            var list = (PyList)a[0];
            int i = BisectRight(interp, a, kwargs);
            list.Items.Insert(i, a[1]);
            return PyNone.Instance;
        });
        d["insort"] = d["insort_right"];

        return m;
    }

    private static (int Lo, int Hi) Bounds(object[] a, Dictionary<string, object>? kwargs, int count)
    {
        int lo = a.Length > 2 ? (int)PyOps.AsBigInt(a[2], "lo")
            : kwargs is not null && kwargs.TryGetValue("lo", out var l) ? (int)PyOps.AsBigInt(l, "lo") : 0;
        int hi = a.Length > 3 ? (int)PyOps.AsBigInt(a[3], "hi")
            : kwargs is not null && kwargs.TryGetValue("hi", out var h) ? (int)PyOps.AsBigInt(h, "hi") : count;
        return (lo, hi);
    }

    private static int BisectLeft(Interpretation.Interp interp, object[] a, Dictionary<string, object>? kwargs)
    {
        var list = (PyList)a[0];
        object x = a[1];
        var (lo, hi) = Bounds(a, kwargs, list.Items.Count);
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            if (interp.Compare(list.Items[mid], x) < 0)
                lo = mid + 1;
            else
                hi = mid;
        }
        return lo;
    }

    private static int BisectRight(Interpretation.Interp interp, object[] a, Dictionary<string, object>? kwargs)
    {
        var list = (PyList)a[0];
        object x = a[1];
        var (lo, hi) = Bounds(a, kwargs, list.Items.Count);
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            if (interp.Compare(x, list.Items[mid]) < 0)
                hi = mid;
            else
                lo = mid + 1;
        }
        return lo;
    }
}

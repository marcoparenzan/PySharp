// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using PySharpLib.Runtime;

namespace PySharpLib.Modules;

/// <summary>operator: function form of the language operators, on top of Interp's own BinaryOp/
/// UnaryOp/GetItem so semantics (dunder dispatch, etc.) match the real operators exactly.</summary>
public static class OperatorModule
{
    public static PyModule Create()
    {
        var m = new PyModule("operator");
        var d = m.Dict;
        void Bin(string name, string op) =>
            d[name] = new PyBuiltinFunction(name, (interp, a, _) => interp.BinaryOp(op, a[0], a[1]));
        void Un(string name, string op) =>
            d[name] = new PyBuiltinFunction(name, (interp, a, _) => interp.UnaryOp(op, a[0]));

        Bin("add", "+"); Bin("__add__", "+");
        Bin("sub", "-"); Bin("__sub__", "-");
        Bin("mul", "*"); Bin("__mul__", "*");
        Bin("truediv", "/"); Bin("__truediv__", "/");
        Bin("floordiv", "//"); Bin("__floordiv__", "//");
        Bin("mod", "%"); Bin("__mod__", "%");
        Bin("pow", "**"); Bin("__pow__", "**");
        Bin("and_", "&"); Bin("__and__", "&");
        Bin("or_", "|"); Bin("__or__", "|");
        Bin("xor", "^"); Bin("__xor__", "^");
        Bin("lshift", "<<"); Bin("__lshift__", "<<");
        Bin("rshift", ">>"); Bin("__rshift__", ">>");
        Bin("eq", "=="); Bin("__eq__", "==");
        Bin("ne", "!="); Bin("__ne__", "!=");
        Bin("lt", "<"); Bin("__lt__", "<");
        Bin("le", "<="); Bin("__le__", "<=");
        Bin("gt", ">"); Bin("__gt__", ">");
        Bin("ge", ">="); Bin("__ge__", ">=");

        Un("neg", "-"); Un("__neg__", "-");
        Un("pos", "+"); Un("__pos__", "+");
        Un("invert", "~"); Un("__invert__", "~");
        // Real CPython: `inv`/`__inv__` are the older (pre-2.0) names for `invert`/`__invert__`,
        // still present in the real `operator` module for backward compat — found via real
        // sqlalchemy's own `sql/operators.py` importing `inv` directly.
        Un("inv", "~"); Un("__inv__", "~");
        Un("not_", "not");

        d["is_"] = new PyBuiltinFunction("is_", (_, a, _) => ReferenceEquals(a[0], a[1]) || Equals(a[0], a[1]));
        d["is_not"] = new PyBuiltinFunction("is_not", (_, a, _) => !(ReferenceEquals(a[0], a[1]) || Equals(a[0], a[1])));
        d["contains"] = new PyBuiltinFunction("contains", (interp, a, _) => PyOps.Contains(interp, a[1], a[0]));
        d["getitem"] = new PyBuiltinFunction("getitem", (interp, a, _) => interp.GetItem(a[0], a[1]));
        d["truth"] = new PyBuiltinFunction("truth", (interp, a, _) => PyOps.Truthy(interp, a[0]));

        d["itemgetter"] = new PyBuiltinFunction("itemgetter", (_, a, _) =>
        {
            var keys = a.ToArray();
            return new PyBuiltinFunction("itemgetter.<locals>.f", (interp2, b, _) =>
                keys.Length == 1
                    ? interp2.GetItem(b[0], keys[0])
                    : new PyTuple(keys.Select(k => interp2.GetItem(b[0], k)).ToArray()));
        });
        d["attrgetter"] = new PyBuiltinFunction("attrgetter", (_, a, _) =>
        {
            var names = a.ToArray();
            return new PyBuiltinFunction("attrgetter.<locals>.f", (interp2, b, _) =>
                names.Length == 1
                    ? GetAttrPath(interp2, b[0], (string)names[0])
                    : new PyTuple(names.Select(n => GetAttrPath(interp2, b[0], (string)n)).ToArray()));
        });
        d["methodcaller"] = new PyBuiltinFunction("methodcaller", (_, a, kwargs) =>
        {
            string name = (string)a[0];
            var extra = a.Skip(1).ToArray();
            return new PyBuiltinFunction("methodcaller.<locals>.f", (interp2, b, _) =>
                interp2.CallMethod(b[0], name, extra, kwargs));
        });

        return m;
    }

    private static object GetAttrPath(Interpretation.Interp interp, object obj, string dotted)
    {
        object cur = obj;
        foreach (var part in dotted.Split('.'))
            cur = interp.GetAttr(cur, part);
        return cur;
    }
}

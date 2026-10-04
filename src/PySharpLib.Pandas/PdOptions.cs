// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using System.Numerics;
using NDSharp.Frame;
using PySharpLib.Interpretation;
using PySharpLib.Runtime;

namespace PySharpLib.Pandas;

/// <summary><c>pd.set_option / get_option / reset_option</c> and <c>pd.options.display.*</c> for the display options the formatter honours.</summary>
internal static class PdOptions
{
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Interp, DisplayOptions> PerEngine = new();

    /// <summary>The display options of the engine running the current call: <c>pd.set_option</c> in one interpreter never leaks into another.</summary>
    public static DisplayOptions Display => PdConv.Interp is { } i ? PerEngine.GetValue(i, _ => new DisplayOptions()) : DisplayOptions.Current;

    private static readonly Dictionary<string, (Func<object> Get, Action<object> Set, Func<object> Default)> Table = new()
    {
        ["display.max_rows"] = (() => IntOrNone(Display.MaxRows), v => Display.MaxRows = ToIntOrZero(v), () => new BigInteger(60)),
        ["display.min_rows"] = (() => IntOrNone(Display.MinRows), v => Display.MinRows = ToIntOrZero(v), () => new BigInteger(10)),
        ["display.max_columns"] = (() => new BigInteger(Display.MaxColumns), v => Display.MaxColumns = ToIntOrZero(v), () => new BigInteger(0)),
        ["display.width"] = (() => new BigInteger(Display.Width), v => { Display.Width = ToIntOrZero(v); }, () => new BigInteger(80)),
        ["display.precision"] = (() => new BigInteger(Display.Precision), v => Display.Precision = ToIntOrZero(v), () => new BigInteger(6)),
        ["display.max_colwidth"] = (() => IntOrNone(Display.MaxColWidth), v => Display.MaxColWidth = ToIntOrZero(v), () => new BigInteger(50)),
        ["display.colheader_justify"] = (() => Display.ColHeaderJustify, v => Display.ColHeaderJustify = (string)v, () => "right"),
    };

    private static object IntOrNone(int v) => v == 0 ? PyNone.Instance : new BigInteger(v);
    private static int ToIntOrZero(object v) => v is PyNone ? 0 : PdConv.ToInt(v);

    private static string Normalize(string key)
    {
        if (Table.ContainsKey(key)) return key;
        var m = Table.Keys.Where(k => k.EndsWith("." + key, StringComparison.Ordinal)).ToList();
        if (m.Count == 1) return m[0];
        throw PyErr.Raise(PyErr.KeyErrorClass, $"No such keys(s): '{key}'");
    }

    public static object Get(string key) => Table[Normalize(key)].Get();
    public static void Set(string key, object value) => Table[Normalize(key)].Set(value);

    public static void Reset(string key)
    {
        if (key == "all") { foreach (var e in Table.Values) e.Set(e.Default()); return; }
        var k = Normalize(key);
        Table[k].Set(Table[k].Default());
    }

    /// <summary><c>pd.options</c>: attribute access mirrors the dotted keys (<c>pd.options.display.max_rows</c>).</summary>
    public static PyInstance OptionsObject()
    {
        var display = new PyClass("DisplayOptions", new List<PyClass>());
        foreach (var key in Table.Keys.Where(k => k.StartsWith("display.")))
        {
            var k = key;
            display.Dict[k["display.".Length..]] = new PyProperty
            {
                Getter = new PyBuiltinFunction("get", (_, _, _) => Get(k)),
                Setter = new PyBuiltinFunction("set", (_, a, _) => { Set(k, a[1]); return PyNone.Instance; }),
            };
        }
        var root = new PyClass("Options", new List<PyClass>());
        var displayObj = new PyInstance(display);
        root.Dict["display"] = new PyProperty { Getter = new PyBuiltinFunction("display", (_, _, _) => displayObj) };
        return new PyInstance(root);
    }
}

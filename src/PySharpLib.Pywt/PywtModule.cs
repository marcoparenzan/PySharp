// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using System.Numerics;
using NDSharp;
using NDSharp.Wavelets;
using PySharpLib.Importing;
using PySharpLib.Numpy;
using PySharpLib.Runtime;

namespace PySharpLib.Pywt;

/// <summary>Opt-in registration of the <c>pywt</c> module (PyWavelets' discrete transforms over NDSharp.Wavelets).</summary>
public static class PywtRegistration
{
    public static void Register(Importer importer) => importer.RegisterBuiltin("pywt", _ => PywtModule.Create());
}

internal static class PywtModule
{
    private static readonly PyClass WaveletClass = BuildWaveletClass();

    private static PyClass BuildWaveletClass()
    {
        var cls = new PyClass("Wavelet", new List<PyClass>());
        cls.Dict["__new__"] = Native.Fn("Wavelet.__new__", (i, a, k) =>
        {
            var p = new Args("Wavelet", i, a.Skip(1).ToArray(), k, "name", "filter_bank");
            return new PyInstance(cls) { Native = Wavelet.Get((string)p.Required(0)) };
        });
        cls.Dict["__init__"] = new PyBuiltinFunction("Wavelet.__init__", (_, _, _) => PyNone.Instance);
        PyProperty Prop(Func<Wavelet, object> f) => new() { Getter = new PyBuiltinFunction("<prop>", (_, a, _) => f((Wavelet)((PyInstance)a[0]).Native!)) };
        object Arr(double[] v) => new PyList(v.Select(x => (object)x)); // real pywt exposes plain Python lists
        cls.Dict["name"] = Prop(w => w.Name);
        cls.Dict["dec_lo"] = Prop(w => Arr(w.DecLo));
        cls.Dict["dec_hi"] = Prop(w => Arr(w.DecHi));
        cls.Dict["rec_lo"] = Prop(w => Arr(w.RecLo));
        cls.Dict["rec_hi"] = Prop(w => Arr(w.RecHi));
        cls.Dict["dec_len"] = Prop(w => new BigInteger(w.FilterLength));
        cls.Dict["rec_len"] = Prop(w => new BigInteger(w.FilterLength));
        cls.Dict["orthogonal"] = Prop(w => w.Orthogonal);
        cls.Dict["filter_bank"] = Prop(w => new PyTuple(new[] { Arr(w.DecLo), Arr(w.DecHi), Arr(w.RecLo), Arr(w.RecHi) }));
        cls.Dict["__repr__"] = new PyBuiltinFunction("Wavelet.__repr__", (_, a, _) => $"Wavelet({((Wavelet)((PyInstance)a[0]).Native!).Name})");
        return cls;
    }

    private static Wavelet W(object o) => o switch
    {
        string s => Wavelet.Get(s),
        PyInstance { Native: Wavelet w } => w,
        _ => throw PyErr.TypeError("wavelet must be a Wavelet object or a wavelet name"),
    };

    private static string Mode(Args p, int i) => p.Has(i) ? (string)p[i]! : "symmetric";

    public static PyModule Create()
    {
        var m = new PyModule("pywt");
        m.Dict["__version__"] = "1.8.0-pysharp (NDSharp.Wavelets)";
        m.Dict["Wavelet"] = WaveletClass;
        void Def(string name, BuiltinFn fn) => m.Dict[name] = Native.Fn(name, fn);

        Def("wavelist", (i, a, k) =>
        {
            var p = new Args("wavelist", i, a, k, "family", "kind");
            var names = Wavelet.Names.AsEnumerable();
            if (p.Has(0))
            {
                string fam = (string)p[0]!;
                names = names.Where(n => n.StartsWith(fam, StringComparison.Ordinal) && (fam is "db" or "sym" or "coif" or "bior" or "rbio" ? char.IsDigit(n[fam.Length]) : n == fam));
            }
            return new PyList(names.Select(n => (object)n));
        });
        Def("dwt_coeff_len", (i, a, k) =>
        {
            var p = new Args("dwt_coeff_len", i, a, k, "data_len", "filter_len", "mode");
            int fl = p.Required(1) is BigInteger b ? (int)b : W(p.Required(1)).FilterLength;
            return new BigInteger(Dwt.CoeffLength(p.Int(0, 0), fl, Mode(p, 2)));
        });
        Def("dwt", (i, a, k) =>
        {
            var p = new Args("dwt", i, a, k, "data", "wavelet", "mode", "axis");
            var (ca, cd) = Dwt.DwtAxis(p.ND(0), W(p.Required(1)), Mode(p, 2), p.Int(3, -1));
            return new PyTuple(new object[] { Conv.Wrap(ca), Conv.Wrap(cd) });
        });
        Def("idwt", (i, a, k) =>
        {
            var p = new Args("idwt", i, a, k, "cA", "cD", "wavelet", "mode", "axis");
            return Conv.Wrap(Dwt.IdwtAxis(p.NDOpt(0), p.NDOpt(1), W(p.Required(2)), Mode(p, 3), p.Int(4, -1)));
        });
        Def("dwt2", (i, a, k) =>
        {
            var p = new Args("dwt2", i, a, k, "data", "wavelet", "mode", "axes");
            var (ca, (ch, cv, cd)) = Dwt.Dwt2(p.ND(0), W(p.Required(1)), Mode(p, 2));
            return new PyTuple(new object[] { Conv.Wrap(ca), new PyTuple(new object[] { Conv.Wrap(ch), Conv.Wrap(cv), Conv.Wrap(cd) }) });
        });
        Def("idwt2", (i, a, k) =>
        {
            var p = new Args("idwt2", i, a, k, "coeffs", "wavelet", "mode", "axes");
            var coeffs = p.Required(0) as PyTuple ?? throw PyErr.TypeError("coeffs must be a tuple (cA, (cH, cV, cD))");
            var details = (PyTuple)coeffs.Items[1];
            return Conv.Wrap(Dwt.Idwt2(Conv.ND(coeffs.Items[0]), (Conv.ND(details.Items[0]), Conv.ND(details.Items[1]), Conv.ND(details.Items[2])), W(p.Required(1)), Mode(p, 2)));
        });
        return m;
    }
}

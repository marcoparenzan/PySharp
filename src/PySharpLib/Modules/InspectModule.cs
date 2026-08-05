// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using PySharpLib.Runtime;

namespace PySharpLib.Modules;

/// <summary>
/// inspect: v1 scope is just what real-world scenario scripts have needed so far (see
/// FASTAPI_PLAN.md) — `cleandoc` (ported from CPython) and `signature`/`Signature`/`Parameter`
/// (the FastAPI-shaped need ROADMAP.md flags, added once a real probe — pydantic v1's dependency
/// chain, not FastAPI itself yet — actually called for it). Built on `PyFunction.Params`/`Defaults`,
/// the same source `__annotations__` already reads (see Interp.cs's `case "__annotations__"`).
/// </summary>
public static class InspectModule
{
    public static readonly PyInstance Empty = new(new PyClass("_empty", new List<PyClass>()));

    private static readonly PyClass ParameterKindClass = new("_ParameterKind", new List<PyClass>());
    public static readonly PyInstance PositionalOnly = MakeKind("POSITIONAL_ONLY");
    public static readonly PyInstance PositionalOrKeyword = MakeKind("POSITIONAL_OR_KEYWORD");
    public static readonly PyInstance VarPositional = MakeKind("VAR_POSITIONAL");
    public static readonly PyInstance KeywordOnly = MakeKind("KEYWORD_ONLY");
    public static readonly PyInstance VarKeyword = MakeKind("VAR_KEYWORD");

    public static readonly PyClass ParameterClass = BuildParameterClass();
    public static readonly PyClass SignatureClass = BuildSignatureClass();

    public static PyModule Create()
    {
        var m = new PyModule("inspect");
        var d = m.Dict;

        d["cleandoc"] = new PyBuiltinFunction("cleandoc", (interp, a, _) =>
            CleanDoc(PyOps.Str(interp, a[0])));

        d["Parameter"] = ParameterClass;
        d["Signature"] = SignatureClass;
        d["signature"] = new PyBuiltinFunction("signature", (interp, a, _) => BuildSignature(interp, a[0]));

        return m;
    }

    private static PyInstance MakeKind(string name)
    {
        var inst = new PyInstance(ParameterKindClass);
        inst.Dict["name"] = name;
        return inst;
    }

    private static PyClass BuildParameterClass()
    {
        var cls = new PyClass("Parameter", new List<PyClass>());
        cls.Dict["empty"] = Empty;
        cls.Dict["POSITIONAL_ONLY"] = PositionalOnly;
        cls.Dict["POSITIONAL_OR_KEYWORD"] = PositionalOrKeyword;
        cls.Dict["VAR_POSITIONAL"] = VarPositional;
        cls.Dict["KEYWORD_ONLY"] = KeywordOnly;
        cls.Dict["VAR_KEYWORD"] = VarKeyword;
        cls.Dict["__repr__"] = new PyBuiltinFunction("Parameter.__repr__", (_, a, _) =>
        {
            var inst = (PyInstance)a[0];
            return $"<Parameter \"{inst.Dict["name"]}\">";
        });
        return cls;
    }

    private static PyClass BuildSignatureClass()
    {
        var cls = new PyClass("Signature", new List<PyClass>());
        cls.Dict["empty"] = Empty;
        return cls;
    }

    private static PyInstance MakeParameter(string name, object kind, object def, object annotation)
    {
        var inst = new PyInstance(ParameterClass);
        inst.Dict["name"] = name;
        inst.Dict["kind"] = kind;
        inst.Dict["default"] = def;
        inst.Dict["annotation"] = annotation;
        return inst;
    }

    /// <summary>Builds a Signature from a PyFunction (plain, async, or a bound method — self is
    /// dropped for bound methods, matching CPython).</summary>
    private static PyInstance BuildSignature(Interpretation.Interp interp, object callee)
    {
        var (fn, skipFirst) = callee switch
        {
            PyFunction f => (f, false),
            PyBoundMethod { Function: PyFunction f } => (f, true),
            _ => throw PyErr.TypeError($"unsupported callable for signature(): {PyOps.TypeName(callee)}"),
        };

        var parameters = new PyDict();
        object EvalOrEmpty(Parsing.Expr? expr)
        {
            if (expr is null)
                return Empty;
            try { return interp.Eval(expr, fn.Closure); }
            catch (PyRaise) { return Empty; }
        }

        var positional = fn.Params.Positional;
        for (int i = skipFirst ? 1 : 0; i < positional.Count; i++)
        {
            var p = positional[i];
            object def = fn.Defaults.TryGetValue(p.Name, out var dv) ? dv : Empty;
            parameters[p.Name] = MakeParameter(p.Name, PositionalOrKeyword, def, EvalOrEmpty(p.Annotation));
        }
        if (!string.IsNullOrEmpty(fn.Params.StarArgs))
            parameters[fn.Params.StarArgs] = MakeParameter(fn.Params.StarArgs, VarPositional, Empty, Empty);
        foreach (var p in fn.Params.KwOnly)
        {
            object def = fn.Defaults.TryGetValue(p.Name, out var dv) ? dv : Empty;
            parameters[p.Name] = MakeParameter(p.Name, KeywordOnly, def, EvalOrEmpty(p.Annotation));
        }
        if (fn.Params.KwArgs is not null)
            parameters[fn.Params.KwArgs] = MakeParameter(fn.Params.KwArgs, VarKeyword, Empty, Empty);

        var sig = new PyInstance(SignatureClass);
        sig.Dict["parameters"] = parameters;
        sig.Dict["return_annotation"] = EvalOrEmpty(fn.Returns);
        return sig;
    }

    /// <summary>Ports CPython's inspect.cleandoc: dedent using the minimum indent of lines after
    /// the first, then trim leading/trailing blank lines.</summary>
    private static string CleanDoc(string doc)
    {
        var lines = doc.Replace("\t", "        ").Split('\n').ToList();

        int margin = int.MaxValue;
        for (int i = 1; i < lines.Count; i++)
        {
            string stripped = lines[i].TrimStart();
            if (stripped.Length > 0)
                margin = Math.Min(margin, lines[i].Length - stripped.Length);
        }

        if (lines.Count > 0)
            lines[0] = lines[0].TrimStart();
        if (margin < int.MaxValue)
            for (int i = 1; i < lines.Count; i++)
                lines[i] = lines[i].Length >= margin ? lines[i][margin..] : "";

        while (lines.Count > 0 && lines[^1].Length == 0)
            lines.RemoveAt(lines.Count - 1);
        while (lines.Count > 0 && lines[0].Length == 0)
            lines.RemoveAt(0);

        return string.Join("\n", lines);
    }
}

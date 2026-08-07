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

        // Real predicates (not stubs) over PySharp's actual runtime object shapes — found via
        // starlette's/anyio's real dependency chain (route-handler introspection: is this a plain
        // function, a bound method, a generator function, a coroutine function, ...). Async
        // generators aren't a construct PySharp can produce at all (see ROADMAP.md), so
        // isasyncgenfunction/isasyncgen are real in the sense that they correctly always report
        // False, not a stub pretending otherwise. See FASTAPI_PLAN.md.
        d["isfunction"] = new PyBuiltinFunction("isfunction", (_, a, _) => a[0] is PyFunction { IsGenerator: false, IsAsync: false });
        d["ismethod"] = new PyBuiltinFunction("ismethod", (_, a, _) => a[0] is PyBoundMethod);
        d["isclass"] = new PyBuiltinFunction("isclass", (_, a, _) => a[0] is PyClass);
        d["ismodule"] = new PyBuiltinFunction("ismodule", (_, a, _) => a[0] is PyModule);
        d["isbuiltin"] = new PyBuiltinFunction("isbuiltin", (_, a, _) => a[0] is PyBuiltinFunction);
        d["isgeneratorfunction"] = new PyBuiltinFunction("isgeneratorfunction", (_, a, _) =>
            a[0] is PyFunction { IsGenerator: true, IsAsync: false });
        d["iscoroutinefunction"] = new PyBuiltinFunction("iscoroutinefunction", (_, a, _) =>
            a[0] is PyFunction { IsAsync: true });
        d["isasyncgenfunction"] = new PyBuiltinFunction("isasyncgenfunction", (_, _, _) => false);
        d["isgenerator"] = new PyBuiltinFunction("isgenerator", (_, a, _) => a[0] is PyGenerator);
        d["iscoroutine"] = new PyBuiltinFunction("iscoroutine", (_, a, _) => a[0] is PyCoroutine);
        d["isasyncgen"] = new PyBuiltinFunction("isasyncgen", (_, _, _) => false);
        d["isawaitable"] = new PyBuiltinFunction("isawaitable", (_, a, _) => a[0] switch
        {
            PyCoroutine or PyFuture => true, // PyTask derives from PyFuture
            PyInstance inst => inst.Class.TryLookup("__await__", out _),
            _ => false,
        });

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
        // Real constructor (not just the internal signature()-builder path above): pydantic's real
        // `generate_model_signature` builds extra params directly, e.g. `Parameter(param_name,
        // Parameter.KEYWORD_ONLY, annotation=field.annotation, default=field.default)`. See
        // FASTAPI_PLAN.md Phase 1.9.
        cls.Dict["__init__"] = new PyBuiltinFunction("Parameter.__init__", (_, a, kwargs) =>
        {
            var inst = (PyInstance)a[0];
            inst.Dict["name"] = a.Length > 1 ? a[1] : throw PyErr.TypeError("Parameter() missing required argument: 'name'");
            inst.Dict["kind"] = a.Length > 2 ? a[2] : throw PyErr.TypeError("Parameter() missing required argument: 'kind'");
            inst.Dict["default"] = kwargs is not null && kwargs.TryGetValue("default", out var def) ? def : Empty;
            inst.Dict["annotation"] = kwargs is not null && kwargs.TryGetValue("annotation", out var ann) ? ann : Empty;
            return PyNone.Instance;
        });
        return cls;
    }

    private static PyClass BuildSignatureClass()
    {
        var cls = new PyClass("Signature", new List<PyClass>());
        cls.Dict["empty"] = Empty;
        // Real constructor (not just the internal signature()-builder path above): pydantic's real
        // `generate_model_signature` builds one directly via `Signature(parameters=[...],
        // return_annotation=None)`. Keys `.parameters` by each Parameter's own `.name`, matching
        // signature()'s own representation (an insertion-ordered name->Parameter mapping) and real
        // CPython's Signature.parameters shape. See FASTAPI_PLAN.md Phase 1.9.
        cls.Dict["__init__"] = new PyBuiltinFunction("Signature.__init__", (interp, a, kwargs) =>
        {
            var inst = (PyInstance)a[0];
            object paramsArg = a.Length > 1 ? a[1] : (kwargs is not null && kwargs.TryGetValue("parameters", out var p) ? p : null!);
            var parameters = new PyDict();
            if (paramsArg is not null)
                foreach (var item in PyOps.Iterate(interp, paramsArg))
                    if (item is PyInstance pi && pi.Dict.TryGet("name", out var name))
                        parameters[name] = pi;
            inst.Dict["parameters"] = parameters;
            inst.Dict["return_annotation"] = kwargs is not null && kwargs.TryGetValue("return_annotation", out var ra)
                ? ra
                : Empty;
            return PyNone.Instance;
        });
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

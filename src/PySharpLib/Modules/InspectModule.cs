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
        //
        // A real bug fix: isfunction previously excluded generator *and* async functions, but real
        // CPython's isfunction is purely "is this a FunctionType" — it doesn't care whether the
        // function happens to be async or a generator (those are what iscoroutinefunction/
        // isgeneratorfunction are for). Found via starlette's real `Route.__init__`
        // (`if inspect.isfunction(endpoint_handler) or inspect.ismethod(endpoint_handler): self.app
        // = request_response(endpoint)` — routing.py): every `async def` endpoint handler failed
        // this check, so Route treated the plain handler function as if it were already a raw ASGI
        // app, calling it with `(scope, receive, send)` instead of wrapping it via
        // `request_response()` to call it correctly with just `(request)`.
        d["isfunction"] = new PyBuiltinFunction("isfunction", (_, a, _) => a[0] is PyFunction);
        d["ismethod"] = new PyBuiltinFunction("ismethod", (_, a, _) => a[0] is PyBoundMethod);
        d["isclass"] = new PyBuiltinFunction("isclass", (_, a, _) => a[0] is PyClass);
        d["ismodule"] = new PyBuiltinFunction("ismodule", (_, a, _) => a[0] is PyModule);
        d["isbuiltin"] = new PyBuiltinFunction("isbuiltin", (_, a, _) => a[0] is PyBuiltinFunction);
        // Real CPython: isroutine = isbuiltin or isfunction or ismethod or ismethoddescriptor or
        // ismethodwrapper — the last two are rare C-level slot-wrapper concepts nothing in the
        // reachable path produces, so this covers the practically relevant cases. Found via real
        // starlette's own routing.py: `get_name(endpoint)` uses it to decide whether to read
        // `endpoint.__name__` directly or fall back to `endpoint.__class__.__name__` — called while
        // constructing every real `Route`, so this blocked `FastAPI()` itself from constructing.
        d["isroutine"] = new PyBuiltinFunction("isroutine", (_, a, _) =>
            a[0] is PyBuiltinFunction or PyFunction or PyBoundMethod);
        // Real CPython unwraps a bound method to its underlying function for these checks (a bound
        // async instance method is still a coroutine function) — found via starlette's real
        // ExceptionMiddleware.http_exception, an `async def` instance method whose bound form
        // (`self.http_exception`) is_async_callable's fallback (`asyncio.iscoroutinefunction`,
        // see below) needs to recognize; missing it routed the call through the sync
        // run_in_threadpool path instead, producing an un-awaited coroutine object.
        d["isgeneratorfunction"] = new PyBuiltinFunction("isgeneratorfunction", (_, a, _) =>
            UnwrapBoundMethod(a[0]) is PyFunction { IsGenerator: true, IsAsync: false });
        // Real CPython: coroutine functions and async generator functions are mutually exclusive
        // categories (an `async def` with `yield` is only an async-gen function, never also a
        // coroutine function) — isasyncgenfunction now real (previously always False, a documented
        // limitation from before real async generators existed; found via starlette's real
        // WebSocket.iter_text/iter_bytes/iter_json, each `async def ...(self): yield ...`).
        d["iscoroutinefunction"] = new PyBuiltinFunction("iscoroutinefunction", (_, a, _) =>
            UnwrapBoundMethod(a[0]) is PyFunction { IsAsync: true, IsGenerator: false });
        d["isasyncgenfunction"] = new PyBuiltinFunction("isasyncgenfunction", (_, a, _) =>
            UnwrapBoundMethod(a[0]) is PyFunction { IsAsync: true, IsGenerator: true });
        d["isgenerator"] = new PyBuiltinFunction("isgenerator", (_, a, _) => a[0] is PyGenerator);
        d["iscoroutine"] = new PyBuiltinFunction("iscoroutine", (_, a, _) => a[0] is PyCoroutine);
        d["isasyncgen"] = new PyBuiltinFunction("isasyncgen", (_, a, _) => a[0] is PyAsyncGenerator);
        d["isawaitable"] = new PyBuiltinFunction("isawaitable", (_, a, _) => a[0] switch
        {
            PyCoroutine or PyFuture => true, // PyTask derives from PyFuture
            PyInstance inst => inst.Class.TryLookup("__await__", out _),
            _ => false,
        });

        // Real CPython's coroutine-state constants + getcoroutinestate. PySharp's PyCoroutine runs
        // its body on its own dedicated OS thread rather than CPython's single-threaded generator-
        // style suspension, so "RUNNING" (actively executing bytecode right now, observed from a
        // different thread) isn't a state this can determine precisely the way CPython's `cr_running`
        // flag can — a genuinely different question when coroutines are real OS threads, not just a
        // scoping gap. Not started -> CREATED; finished -> CLOSED; anything in between is reported
        // as SUSPENDED, which is correct for the only real use found: anyio's real `getcoroutinestate
        // (coro) in (CORO_RUNNING, CORO_SUSPENDED)` (_backends/_asyncio.py) just needs "started and
        // not finished", a distinction this preserves exactly. Found via anyio's real `from inspect
        // import CORO_RUNNING, CORO_SUSPENDED, getcoroutinestate`, reachable from `import starlette`.
        d["CORO_CREATED"] = "CORO_CREATED";
        d["CORO_RUNNING"] = "CORO_RUNNING";
        d["CORO_SUSPENDED"] = "CORO_SUSPENDED";
        d["CORO_CLOSED"] = "CORO_CLOSED";
        d["getcoroutinestate"] = new PyBuiltinFunction("getcoroutinestate", (_, a, _) =>
        {
            var coro = (PyCoroutine)a[0];
            return coro.Finished ? "CORO_CLOSED" : coro.Started ? "CORO_SUSPENDED" : "CORO_CREATED";
        });

        return m;
    }

    internal static object UnwrapBoundMethod(object obj) => obj is PyBoundMethod bm ? bm.Function : obj;

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
        // FASTAPI_PLAN.md Phase 1.9. `name`/`kind` are real CPython positional-or-keyword params
        // (only `default`/`annotation` are keyword-only, after `*`) — found via fastapi's real
        // `get_typed_signature` (dependencies/utils.py) calling `Parameter(name=..., kind=...,
        // default=..., annotation=...)` entirely by keyword.
        cls.Dict["__init__"] = new PyBuiltinFunction("Parameter.__init__", (_, a, kwargs) =>
        {
            var inst = (PyInstance)a[0];
            object Arg(string name, int pos) => pos < a.Length ? a[pos]
                : kwargs is not null && kwargs.TryGetValue(name, out var v) ? v
                : throw PyErr.TypeError($"Parameter() missing required argument: '{name}'");
            inst.Dict["name"] = Arg("name", 1);
            inst.Dict["kind"] = Arg("kind", 2);
            inst.Dict["default"] = kwargs is not null && kwargs.TryGetValue("default", out var def) ? def : Empty;
            inst.Dict["annotation"] = kwargs is not null && kwargs.TryGetValue("annotation", out var ann) ? ann : Empty;
            return PyNone.Instance;
        });
        // Real CPython's Parameter.replace(**changes): a new Parameter with the given field(s)
        // overridden, defaulting to self's current values for anything not passed. Found via real
        // pydantic v1's own generate_model_signature (utils.py): `var_kw.replace(name=var_kw_name)`.
        cls.Dict["replace"] = new PyBuiltinFunction("Parameter.replace", (_, a, kwargs) =>
        {
            var self = (PyInstance)a[0];
            object Get(string key) => kwargs is not null && kwargs.TryGetValue(key, out var v) ? v : self.Dict[key];
            return MakeParameter((string)Get("name"), Get("kind"), Get("default"), Get("annotation"));
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

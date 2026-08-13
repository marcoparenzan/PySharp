// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using PySharpLib.Interpretation;
using PySharpLib.Parsing;

namespace PySharpLib.Runtime;

/// <summary>Signature of builtin functions implemented in C#.</summary>
public delegate object BuiltinFn(Interp interp, object[] args, Dictionary<string, object>? kwargs);

public sealed class PyBuiltinFunction
{
    public string Name { get; }
    public BuiltinFn Fn { get; }

    /// <summary>Attributes assigned to the function (e.g. `setattr(fn, "__module__", "httpx")`,
    /// found via real httpx's own `__init__.py` doing exactly that to every `__all__` export —
    /// including names that resolve to a PyBuiltinFunction in this interpreter even though real
    /// CPython's equivalent is a plain Python-level function). Mirrors PyFunction.Attributes.</summary>
    public PyDict Attributes { get; } = new();

    public PyBuiltinFunction(string name, BuiltinFn fn)
    {
        Name = name;
        Fn = fn;
    }

    public override string ToString() => $"<built-in function {Name}>";
}

/// <summary>Function defined in Python (def or lambda).</summary>
public sealed class PyFunction
{
    public string Name { get; }
    public Parameters Params { get; }
    public List<Stmt>? Body { get; }        // def
    public Expr? LambdaBody { get; }        // lambda expression
    public Env Closure { get; }
    public PyModule Module { get; }
    public bool IsGenerator { get; }
    /// <summary>Defined with <c>async def</c>: calling it returns a coroutine.</summary>
    public bool IsAsync { get; init; }
    /// <summary>Defaults evaluated at definition time, in order (positional then kw-only).</summary>
    public Dictionary<string, object> Defaults { get; }
    /// <summary>Attributes assigned to the function (e.g. by functools.wraps).</summary>
    public PyDict Attributes { get; } = new();
    /// <summary>Class in which the function is defined (for zero-arg super()).</summary>
    public PyClass? DefiningClass { get; set; }
    /// <summary>Return-value annotation (-> T), null if absent. For __annotations__['return'].</summary>
    public Expr? Returns { get; init; }

    public PyFunction(string name, Parameters parameters, List<Stmt>? body, Expr? lambdaBody,
        Env closure, PyModule module, bool isGenerator, Dictionary<string, object> defaults)
    {
        Name = name;
        Params = parameters;
        Body = body;
        LambdaBody = lambdaBody;
        Closure = closure;
        Module = module;
        IsGenerator = isGenerator;
        Defaults = defaults;
    }

    public override string ToString() => $"<function {Name}>";

    /// <summary>Minimal code object for signature introspection (fn.__code__).</summary>
    public PyCode Code => new(this);
}

/// <summary>
/// Minimal "code" object (fn.__code__): exposes the parameter names for signature
/// introspection — what inspect.signature relies on. Includes only the parameters
/// (positional, *args, keyword-only, **kwargs), not the local variables.
/// </summary>
public sealed class PyCode
{
    public string Name { get; }
    /// <summary>Names in order: positional, then *args, then keyword-only, then **kwargs.</summary>
    public string[] VarNames { get; }
    /// <summary>Number of positional parameters (co_argcount).</summary>
    public int ArgCount { get; }
    /// <summary>Number of keyword-only parameters (co_kwonlyargcount).</summary>
    public int KwOnlyArgCount { get; }

    /// <summary>Real CPython `co_flags` — only the two bits anything reachable has needed so far:
    /// `CO_VARARGS` (0x04, real `*args`) and `CO_VARKEYWORDS` (0x08, real `**kwargs`), plus the two
    /// every real function always has (`CO_OPTIMIZED` 0x01, `CO_NEWLOCALS` 0x02). Found via real
    /// sqlalchemy's own `inspect_getfullargspec` (a port of CPython's own `inspect.py`), which reads
    /// exactly those two variable-arity bits to decide whether a function accepts `*args`/`**kwargs`.
    /// Generator/coroutine/async-generator bits are not tracked here (nothing reachable has needed
    /// them off `co_flags` specifically — `inspect.isgeneratorfunction`/etc. already work a different
    /// way, off the `PyFunction`/`PyGenerator` object shape directly, not `co_flags`).</summary>
    public int Flags { get; }

    public PyCode(PyFunction fn)
    {
        Name = fn.Name;
        var names = new List<string>();
        foreach (var p in fn.Params.Positional)
            names.Add(p.Name);
        ArgCount = fn.Params.Positional.Count;
        bool hasVarArgs = !string.IsNullOrEmpty(fn.Params.StarArgs);
        if (hasVarArgs)
            names.Add(fn.Params.StarArgs!);
        foreach (var p in fn.Params.KwOnly)
            names.Add(p.Name);
        KwOnlyArgCount = fn.Params.KwOnly.Count;
        bool hasVarKeywords = !string.IsNullOrEmpty(fn.Params.KwArgs);
        if (hasVarKeywords)
            names.Add(fn.Params.KwArgs!);
        VarNames = names.ToArray();
        Flags = 0x01 | 0x02 | (hasVarArgs ? 0x04 : 0) | (hasVarKeywords ? 0x08 : 0);
    }

    public override string ToString() => $"<code object {Name}>";
}

/// <summary>Method bound to an instance (or to a class for classmethods).</summary>
public sealed class PyBoundMethod
{
    public object Self { get; }
    public object Function { get; } // PyFunction or PyBuiltinFunction

    public PyBoundMethod(object self, object function)
    {
        Self = self;
        Function = function;
    }

    public override string ToString() => $"<bound method>";
}

/// <summary>Wrapper for staticmethod/classmethod/property in the class dict.</summary>
public sealed class PyStaticMethod
{
    public object Function { get; }
    /// <summary>Real CPython's `staticmethod` supports arbitrary attribute assignment (e.g. real
    /// pydantic v1's `@validator` internals do `setattr(f_cls, '__validator_config__', ...)` on a
    /// raw classmethod/staticmethod object before ever attaching it to a class). Lazily allocated —
    /// most staticmethods never get one set.</summary>
    public PyDict? Attributes { get; private set; }
    public PyDict EnsureAttributes() => Attributes ??= new PyDict();
    public PyStaticMethod(object function) => Function = function;
}

public sealed class PyClassMethod
{
    public object Function { get; }
    /// <summary>See PyStaticMethod.Attributes — same real CPython arbitrary-attribute-assignment
    /// support, needed for the same real pydantic v1 `@validator`/`@root_validator` idiom.</summary>
    public PyDict? Attributes { get; private set; }
    public PyDict EnsureAttributes() => Attributes ??= new PyDict();
    public PyClassMethod(object function) => Function = function;
}

public sealed class PyProperty
{
    public object? Getter { get; init; }
    public object? Setter { get; init; }
    public object? Deleter { get; init; }
}

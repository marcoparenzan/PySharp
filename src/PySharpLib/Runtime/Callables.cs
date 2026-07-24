using PySharpLib.Interpretation;
using PySharpLib.Parsing;

namespace PySharpLib.Runtime;

/// <summary>Signature of builtin functions implemented in C#.</summary>
public delegate object BuiltinFn(Interp interp, object[] args, Dictionary<string, object>? kwargs);

public sealed class PyBuiltinFunction
{
    public string Name { get; }
    public BuiltinFn Fn { get; }

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

    public PyCode(PyFunction fn)
    {
        Name = fn.Name;
        var names = new List<string>();
        foreach (var p in fn.Params.Positional)
            names.Add(p.Name);
        ArgCount = fn.Params.Positional.Count;
        if (!string.IsNullOrEmpty(fn.Params.StarArgs))
            names.Add(fn.Params.StarArgs!);
        foreach (var p in fn.Params.KwOnly)
            names.Add(p.Name);
        KwOnlyArgCount = fn.Params.KwOnly.Count;
        if (!string.IsNullOrEmpty(fn.Params.KwArgs))
            names.Add(fn.Params.KwArgs!);
        VarNames = names.ToArray();
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
    public PyStaticMethod(object function) => Function = function;
}

public sealed class PyClassMethod
{
    public object Function { get; }
    public PyClassMethod(object function) => Function = function;
}

public sealed class PyProperty
{
    public object? Getter { get; init; }
    public object? Setter { get; init; }
    public object? Deleter { get; init; }
}

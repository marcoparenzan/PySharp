// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using PySharpLib.Parsing;
using PySharpLib.Runtime;

namespace PySharpLib.Modules;

/// <summary>ast: real `literal_eval` — parses a string via this project's own real parser
/// (`Parser.ParseExpression`, the same one behind the `eval()` builtin) and walks the resulting
/// expression tree directly, accepting only real CPython's own literal-eval subset (numbers,
/// strings, bytes, bools, None, tuples/lists/sets/dicts of the same, and a leading unary +/- on a
/// numeric literal) — never actually executing anything, so `ast.literal_eval("os.system(...)")`
/// correctly raises `ValueError` rather than running it. Found via real `pika`'s own
/// `connection.py`, which imports `ast` unconditionally at module load time (`ast.literal_eval` is
/// used to parse dict/tuple-shaped values out of AMQP URL query-string parameters — a path this
/// project's own scenario 7 demo never actually exercises, but the bare `import ast` still needs a
/// real module to succeed). See ROADMAP.md scenario 7.</summary>
public static class AstModule
{
    public static PyModule Create()
    {
        var m = new PyModule("ast");
        m.Dict["literal_eval"] = new PyBuiltinFunction("literal_eval", (_, a, _) =>
        {
            if (a.Length == 0 || a[0] is not string source)
                throw PyErr.TypeError("literal_eval() arg must be a string");
            Expr expr;
            try
            {
                expr = Parser.ParseExpression(source);
            }
            catch (PySyntaxError ex)
            {
                throw PyErr.SyntaxLike(ex.Message);
            }
            return EvalLiteral(expr);
        });
        return m;
    }

    private static object EvalLiteral(Expr expr) => expr switch
    {
        IntLit i => i.Value,
        FloatLit f => f.Value,
        StrLit s => s.Value,
        BytesLit b => new PyBytes(b.Value),
        BoolLit b => b.Value,
        NoneLit => PyNone.Instance,
        TupleExpr t => new PyTuple(t.Items.Select(EvalLiteral).ToArray()),
        ListExpr l => new PyList(l.Items.Select(EvalLiteral)),
        SetExpr s => new PySet(s.Items.Select(EvalLiteral)),
        DictExpr d => BuildDict(d),
        UnaryExpr { Op: "-" } u => Negate(EvalLiteral(u.Operand)),
        UnaryExpr { Op: "+" } u => RequireNumeric(EvalLiteral(u.Operand)),
        _ => throw PyErr.ValueError("malformed node or string"),
    };

    private static PyDict BuildDict(DictExpr d)
    {
        var dict = new PyDict();
        foreach (var (key, value) in d.Items)
        {
            if (key is null)
                throw PyErr.ValueError("malformed node or string");
            dict[EvalLiteral(key)] = EvalLiteral(value);
        }
        return dict;
    }

    private static object Negate(object value) => value switch
    {
        System.Numerics.BigInteger i => -i,
        double d => -d,
        _ => throw PyErr.ValueError("malformed node or string"),
    };

    private static object RequireNumeric(object value) => value switch
    {
        System.Numerics.BigInteger or double => value,
        _ => throw PyErr.ValueError("malformed node or string"),
    };
}

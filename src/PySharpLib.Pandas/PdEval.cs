// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;
using NDSharp.Frame;
using PySharpLib.Interpretation;
using PySharpLib.Numpy;
using PySharpLib.Parsing;
using PySharpLib.Runtime;
using FIndex = NDSharp.Frame.Index;

namespace PySharpLib.Pandas;

/// <summary><c>DataFrame.eval</c>, <c>DataFrame.query</c> and <c>pd.eval</c>: the expression is parsed by PySharp's own Python parser, rewritten with pandas' rules
/// (<c>and</c>/<c>or</c>/<c>not</c> become <c>&amp;</c>/<c>|</c>/<c>~</c>, chained comparisons are split, <c>in</c> and <c>== [list]</c> become <c>isin</c>, <c>@name</c> reads the caller's variable,
/// backticks quote column names) and evaluated by the interpreter with the columns bound as Series.</summary>
internal static class PdEval
{
    public static readonly PyClass UndefinedVariableError = new("UndefinedVariableError", new List<PyClass> { PyErr.NameErrorClass });

    private static Args A(string fn, Interp i, object[] a, Dictionary<string, object>? k, params string[] names) => new(fn, i, a.Skip(1).ToArray(), k, names);

    // ------------------------------------------------------------------ text preparation

    /// <summary>Replaces <c>`quoted names`</c> and <c>@variables</c> (outside string literals) by plain identifiers.</summary>
    private static string Prepare(string text, Dictionary<string, string> backticks)
    {
        var sb = new StringBuilder();
        for (int p = 0; p < text.Length; p++)
        {
            char c = text[p];
            if (c is '\'' or '"')
            {
                int q = p + 1;
                while (q < text.Length && text[q] != c) { if (text[q] == '\\') q++; q++; }
                sb.Append(text, p, Math.Min(q + 1, text.Length) - p);
                p = q;
            }
            else if (c == '`')
            {
                int q = text.IndexOf('`', p + 1);
                if (q < 0) throw PyErr.Raise(PyErr.SyntaxErrorClass, "unterminated backtick in expression");
                string id = "__bt" + backticks.Count;
                backticks[id] = text.Substring(p + 1, q - p - 1);
                sb.Append(id);
                p = q;
            }
            else if (c == '&') sb.Append(" and ");
            else if (c == '|') sb.Append(" or ");
            else if (c == '@' && p + 1 < text.Length && (char.IsLetter(text[p + 1]) || text[p + 1] == '_'))
                sb.Append("__at_");
            else sb.Append(c);
        }
        return sb.ToString();
    }

    // ------------------------------------------------------------------ AST rewriting

    private static Expr Rewrite(Expr e)
    {
        switch (e)
        {
            case IntLit or FloatLit or StrLit or BoolLit or NoneLit or NameExpr: return e;
            case BinaryExpr b: return b with { Left = Rewrite(b.Left), Right = Rewrite(b.Right) };
            case UnaryExpr { Op: "not" } u: return new UnaryExpr("~", Rewrite(u.Operand)) { Line = u.Line, Col = u.Col };
            case UnaryExpr u: return u with { Operand = Rewrite(u.Operand) };
            case BoolOpExpr bo:
            {
                string op = bo.Op == "and" ? "&" : "|";
                var acc = Rewrite(bo.Values[0]);
                for (int k = 1; k < bo.Values.Count; k++) acc = new BinaryExpr(acc, op, Rewrite(bo.Values[k])) { Line = bo.Line, Col = bo.Col };
                return acc;
            }
            case CompareExpr c:
            {
                Expr? acc = null;
                Expr left = Rewrite(c.Left);
                for (int k = 0; k < c.Ops.Count; k++)
                {
                    var right = Rewrite(c.Comparators[k]);
                    string op = c.Ops[k];
                    bool listRight = c.Comparators[k] is ListExpr or TupleExpr;
                    Expr part;
                    if (op is "in" or "not in" || (op is "==" or "!=") && listRight)
                    {
                        part = new CallExpr(new AttributeExpr(left, "isin") { Line = c.Line, Col = c.Col }, new List<CallArg> { new(null, right, false, false) }) { Line = c.Line, Col = c.Col };
                        if (op is "not in" or "!=") part = new UnaryExpr("~", part) { Line = c.Line, Col = c.Col };
                    }
                    else part = new CompareExpr(left, new List<string> { op }, new List<Expr> { right }) { Line = c.Line, Col = c.Col };
                    acc = acc is null ? part : new BinaryExpr(acc, "&", part) { Line = c.Line, Col = c.Col };
                    left = right;
                }
                return acc!;
            }
            case CallExpr call:
                return call with { Func = Rewrite(call.Func), Args = call.Args.Select(a => a with { Value = Rewrite(a.Value) }).ToList() };
            case AttributeExpr at: return at with { Obj = Rewrite(at.Obj) };
            case IndexExpr ix: return ix with { Obj = Rewrite(ix.Obj), Index = Rewrite(ix.Index) };
            case SliceExpr sl: return sl with { Start = sl.Start is null ? null : Rewrite(sl.Start), Stop = sl.Stop is null ? null : Rewrite(sl.Stop), Step = sl.Step is null ? null : Rewrite(sl.Step) };
            case TupleExpr t: return t with { Items = t.Items.Select(Rewrite).ToList() };
            case ListExpr l: return l with { Items = l.Items.Select(Rewrite).ToList() };
            case IfExpExpr ie: return ie with { Cond = Rewrite(ie.Cond), Then = Rewrite(ie.Then), Else = Rewrite(ie.Else) };
        }
        throw PyErr.NotImplementedError($"{e.GetType().Name.Replace("Expr", "")} is not allowed in pandas eval/query expressions");
    }

    private static void Names(Expr e, HashSet<string> into)
    {
        switch (e)
        {
            case NameExpr n: into.Add(n.Id); break;
            case BinaryExpr b: Names(b.Left, into); Names(b.Right, into); break;
            case UnaryExpr u: Names(u.Operand, into); break;
            case CompareExpr c: Names(c.Left, into); foreach (var x in c.Comparators) Names(x, into); break;
            case CallExpr call: Names(call.Func, into); foreach (var a in call.Args) Names(a.Value, into); break;
            case AttributeExpr at: Names(at.Obj, into); break;
            case IndexExpr ix: Names(ix.Obj, into); Names(ix.Index, into); break;
            case SliceExpr sl: foreach (var x in new[] { sl.Start, sl.Stop, sl.Step }) if (x is not null) Names(x, into); break;
            case TupleExpr t: foreach (var x in t.Items) Names(x, into); break;
            case ListExpr l: foreach (var x in l.Items) Names(x, into); break;
            case IfExpExpr ie: Names(ie.Cond, into); Names(ie.Then, into); Names(ie.Else, into); break;
        }
    }

    // ------------------------------------------------------------------ functions of numexpr's vocabulary

    private static object MathFn(string name, Func<double, double> f) => new PyBuiltinFunction(name, (interp, a, _) =>
    {
        if (a.Length != 1) throw PyErr.TypeError($"{name}() takes exactly one argument");
        switch (a[0])
        {
            case PyInstance { Native: Series s }:
                return PdConv.Wrap(new Series(Column.FromDoubles(Enumerable.Range(0, s.Length).Select(r => s.Values.IsNa(r) ? double.NaN : f(ToD(s.Values[r]!))).ToArray()), s.Index, s.Name));
            default: return f(Column.ToDouble(PdConv.ToCell(a[0])!));
        }
    });

    private static double ToD(object v) => v is Ts or Td ? throw PyErr.TypeError("cannot apply a math function to datetimes") : Column.ToDouble(v);

    private static readonly (string, Func<double, double>)[] Functions =
    {
        ("sqrt", Math.Sqrt), ("exp", Math.Exp), ("log", Math.Log), ("log10", Math.Log10), ("log1p", x => Math.Log(1 + x)), ("expm1", x => Math.Exp(x) - 1),
        ("sin", Math.Sin), ("cos", Math.Cos), ("tan", Math.Tan), ("arcsin", Math.Asin), ("arccos", Math.Acos), ("arctan", Math.Atan),
        ("sinh", Math.Sinh), ("cosh", Math.Cosh), ("tanh", Math.Tanh), ("arcsinh", Math.Asinh), ("arccosh", Math.Acosh), ("arctanh", Math.Atanh),
        ("floor", Math.Floor), ("ceil", Math.Ceiling),
    };

    // ------------------------------------------------------------------ evaluation

    private sealed record Scope(Interp Interp, Env? Caller, PyDict? Locals, PyDict? Globals);

    private static object ResolveOuter(Scope sc, string id)
    {
        if (sc.Locals is not null && sc.Locals.TryGet(id, out var v)) return v;
        if (sc.Globals is not null && sc.Globals.TryGet(id, out var g)) return g;
        if (sc.Caller is not null)
        {
            try { return sc.Interp.Eval(new NameExpr(id), sc.Caller); }
            catch (PyRaise ex) when (ex.Value.Class == PyErr.NameErrorClass) { }
        }
        throw PyErr.Raise(UndefinedVariableError, $"local variable '{id}' is not defined");
    }

    /// <summary>Evaluates one expression against the frame's columns (or no frame for <c>pd.eval</c>).</summary>
    private static object EvalOne(Scope sc, DataFrame? df, string text)
    {
        var backticks = new Dictionary<string, string>();
        var ast = Rewrite(Parser.ParseExpression(Prepare(text.Trim(), backticks)));
        var names = new HashSet<string>();
        Names(ast, names);
        var module = new PyModule("<pandas.eval>", new PyDict()) { Builtins = sc.Interp.BuiltinsModule };
        var env = new Env(module) { IsGlobalScope = true };
        foreach (var (fname, f) in Functions) env.Set(fname, MathFn(fname, f));
        env.Set("abs", new PyBuiltinFunction("abs", (interp, a, _) => a[0] is PyInstance { Native: Series or DataFrame } ? interp.CallMethod(a[0], "abs", Array.Empty<object>()) : interp.Call(interp.BuiltinsModule.Dict["abs"], a)));
        foreach (var id in names)
        {
            if (id.StartsWith("__at_")) { env.Set(id, ResolveOuter(sc, id.Substring(5))); continue; }
            string label = backticks.TryGetValue(id, out var bt) ? bt : id;
            if (df is not null)
            {
                if (df.HasColumn(label)) { env.Set(id, PdConv.Wrap(df.GetColumn(df.ColumnPositions(label)[0]))); continue; }
                if (id == "index")
                {
                    env.Set(id, PdConv.Wrap(new Series(df.Index.Labels, df.Index, df.Index.Name)));
                    continue;
                }
                int lvl = Array.FindIndex(df.Index.Names.ToArray(), n => n is string s && s == label);
                if (lvl >= 0) { env.Set(id, PdConv.Wrap(new Series(df.Index.Level(lvl), df.Index, label))); continue; }
            }
            if (env.TryGet(id, out _)) continue;
            try { env.Set(id, ResolveOuter(sc, id)); continue; } catch (PyRaise ex) when (ex.Value.Class == UndefinedVariableError) { }
            if (sc.Interp.BuiltinsModule.Dict.TryGet(id, out _)) continue;
            throw PyErr.Raise(UndefinedVariableError, $"name '{label}' is not defined");
        }
        return sc.Interp.Eval(ast, env);
    }

    private static readonly Regex Assignment = new(@"^\s*(`[^`]+`|[A-Za-z_][A-Za-z0-9_]*)\s*=(?!=)\s*(.+)$", RegexOptions.Singleline);

    private static DataFrame WithColumn(DataFrame df, string label, object value)
    {
        Column col = value is PyInstance { Native: Series s } ? (Ops.SameLabels(s.Index, df.Index) ? s.Values : s.Values.Take(Ops.Reindexer(s.Index, df.Index)))
            : PdConv.IsListLike(value) ? PdConv.ToColumn(value) : Column.Infer(Enumerable.Repeat(PdConv.ToCell(value), df.NRows).ToList());
        var cols = df.Data.ToList(); var names = df.Columns.Items().ToList();
        int pos = names.FindIndex(n => Equals(Column.Key(n), Column.Key(label)));
        if (pos >= 0) cols[pos] = col; else { cols.Add(col); names.Add(label); }
        return new DataFrame(cols, new FIndex(Column.Infer(names), df.Columns.Name), df.Index);
    }

    private static Scope MakeScope(Interp i, Args p, int localIdx, int globalIdx)
        => new(i, Interp.InnermostFrame?.Env, p.Has(localIdx) ? p[localIdx] as PyDict : null, p.Has(globalIdx) ? p[globalIdx] as PyDict : null);

    private static object EvalFrame(Scope sc, DataFrame df, string text, bool allowAssign)
    {
        object result = PyNone.Instance;
        var current = df;
        bool assigned = false;
        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0) continue;
            var m = allowAssign ? Assignment.Match(line) : Match.Empty;
            if (m.Success)
            {
                string target = m.Groups[1].Value.Trim('`');
                var v = EvalOne(sc, current, m.Groups[2].Value);
                current = WithColumn(current, target, v);
                assigned = true;
            }
            else result = EvalOne(sc, current, line);
        }
        return assigned ? PdConv.Wrap(current) : result;
    }

    public static void Install(PyModule m)
    {
        PdClasses.DataFrame.Dict["eval"] = PdClasses.Fn("eval", (i, a, k) =>
        {
            var p = A("eval", i, a, k, "expr", "inplace", "local_dict", "global_dict", "resolvers", "level", "target", "engine", "parser");
            var df = PdConv.D(a[0]);
            var result = EvalFrame(MakeScope(i, p, 2, 3), df, (string)p.Required(0), true);
            if (p.Bool(1, false))
            {
                if (result is PyInstance { Native: DataFrame nd }) { df.Data.Clear(); df.Data.AddRange(nd.Data); df.Columns = nd.Columns; df.Index = nd.Index; return PyNone.Instance; }
                throw PyErr.ValueError("Cannot operate inplace if there is no assignment");
            }
            return result;
        });
        PdClasses.DataFrame.Dict["query"] = PdClasses.Fn("query", (i, a, k) =>
        {
            var p = A("query", i, a, k, "expr", "inplace", "local_dict", "global_dict", "resolvers", "level", "target", "engine", "parser");
            var df = PdConv.D(a[0]);
            var res = EvalFrame(MakeScope(i, p, 2, 3), df, (string)p.Required(0), false);
            var mask = PdSelect.TryMask(res, df.Index) ?? throw PyErr.ValueError("query() expression must evaluate to a boolean mask");
            if (mask.Length != df.NRows) throw PyErr.ValueError($"Item wrong length {mask.Length} instead of {df.NRows}.");
            var picked = df.TakeRows(PdSelect.MaskPositions(mask));
            return PdWrangle.Finish(a[0], PdConv.Wrap(picked), p.Bool(1, false));
        });
        m.Dict["eval"] = PdClasses.Fn("eval", (i, a, k) =>
        {
            var p = new Args("eval", i, a, k, "expr", "parser", "engine", "local_dict", "global_dict", "resolvers", "level", "target", "inplace");
            var sc = new Scope(i, Interp.InnermostFrame?.Env, p.Has(3) ? p[3] as PyDict : null, p.Has(4) ? p[4] as PyDict : null);
            object result = PyNone.Instance;
            foreach (var line in ((string)p.Required(0)).Split('\n')) if (line.Trim().Length > 0) result = EvalOne(sc, null, line);
            return result;
        });
    }
}

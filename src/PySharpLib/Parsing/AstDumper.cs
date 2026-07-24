// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using System.Text;

namespace PySharpLib.Parsing;

/// <summary>Serializes the AST into compact s-expressions, for tests and debugging.</summary>
public static class AstDumper
{
    public static string Dump(Module module)
        => string.Join(" ", module.Body.Select(Dump));

    public static string Dump(Stmt s) => s switch
    {
        BlockStmt b => $"(block {Join(b.Body)})",
        ExprStmt e => $"(expr {Dump(e.Value)})",
        AssignStmt a => $"(= [{Join(a.Targets)}] {Dump(a.Value)})",
        AugAssignStmt a => $"({a.Op}= {Dump(a.Target)} {Dump(a.Value)})",
        AnnAssignStmt a => a.Value is null
            ? $"(ann {Dump(a.Target)} {Dump(a.Annotation)})"
            : $"(ann {Dump(a.Target)} {Dump(a.Annotation)} {Dump(a.Value)})",
        IfStmt i => i.OrElse.Count == 0
            ? $"(if {Dump(i.Cond)} [{Join(i.Body)}])"
            : $"(if {Dump(i.Cond)} [{Join(i.Body)}] [{Join(i.OrElse)}])",
        WhileStmt w => w.OrElse.Count == 0
            ? $"(while {Dump(w.Cond)} [{Join(w.Body)}])"
            : $"(while {Dump(w.Cond)} [{Join(w.Body)}] [{Join(w.OrElse)}])",
        ForStmt f => f.OrElse.Count == 0
            ? $"(for {Dump(f.Target)} {Dump(f.Iter)} [{Join(f.Body)}])"
            : $"(for {Dump(f.Target)} {Dump(f.Iter)} [{Join(f.Body)}] [{Join(f.OrElse)}])",
        FuncDef f => $"(def{(f.IsGenerator ? "*" : "")} {f.Name} {DumpParams(f.Params)} [{Join(f.Body)}]{DumpDecorators(f.Decorators)})",
        ClassDef c => $"(class {c.Name} ({string.Join(" ", c.Bases.Select(DumpArg))}) [{Join(c.Body)}]{DumpDecorators(c.Decorators)})",
        ReturnStmt r => r.Value is null ? "(return)" : $"(return {Dump(r.Value)})",
        PassStmt => "(pass)",
        BreakStmt => "(break)",
        ContinueStmt => "(continue)",
        RaiseStmt r => r switch
        {
            { Exc: null } => "(raise)",
            { Cause: null } => $"(raise {Dump(r.Exc!)})",
            _ => $"(raise {Dump(r.Exc!)} from {Dump(r.Cause!)})",
        },
        TryStmt t => new StringBuilder("(try [")
            .Append(Join(t.Body)).Append(']')
            .Append(string.Concat(t.Handlers.Select(h =>
                $" (except{(h.Type is null ? "" : " " + Dump(h.Type))}{(h.Name is null ? "" : " as " + h.Name)} [{Join(h.Body)}])")))
            .Append(t.OrElse.Count > 0 ? $" (else [{Join(t.OrElse)}])" : "")
            .Append(t.Finally.Count > 0 ? $" (finally [{Join(t.Finally)}])" : "")
            .Append(')').ToString(),
        WithStmt w => $"(with {string.Join(" ", w.Items.Select(i => i.Target is null ? Dump(i.Ctx) : $"({Dump(i.Ctx)} as {Dump(i.Target)})"))} [{Join(w.Body)}])",
        ImportStmt i => $"(import {string.Join(" ", i.Names.Select(DumpAlias))})",
        FromImportStmt f => f.Star
            ? $"(from {new string('.', f.Level)}{f.Module} import *)"
            : $"(from {new string('.', f.Level)}{f.Module} import {string.Join(" ", f.Names.Select(DumpAlias))})",
        GlobalStmt g => $"(global {string.Join(" ", g.Names)})",
        NonlocalStmt n => $"(nonlocal {string.Join(" ", n.Names)})",
        DelStmt d => $"(del {Join(d.Targets)})",
        AssertStmt a => a.Msg is null ? $"(assert {Dump(a.Test)})" : $"(assert {Dump(a.Test)} {Dump(a.Msg)})",
        _ => $"(?{s.GetType().Name})",
    };

    public static string Dump(Expr e) => e switch
    {
        IntLit i => i.Value.ToString(),
        FloatLit f => f.Value.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
        StrLit s => $"'{s.Value}'",
        BytesLit b => $"b'{Convert.ToHexString(b.Value)}'",
        BoolLit b => b.Value ? "True" : "False",
        NoneLit => "None",
        EllipsisLit => "...",
        NameExpr n => n.Id,
        TupleExpr t => $"(tuple {Join(t.Items)})",
        ListExpr l => $"(list {Join(l.Items)})",
        SetExpr s => $"(set {Join(s.Items)})",
        DictExpr d => $"(dict {string.Join(" ", d.Items.Select(kv => kv.Key is null ? $"**{Dump(kv.Value)}" : $"{Dump(kv.Key)}:{Dump(kv.Value)}"))})",
        UnaryExpr u => $"({u.Op} {Dump(u.Operand)})",
        BinaryExpr b => $"({b.Op} {Dump(b.Left)} {Dump(b.Right)})",
        BoolOpExpr b => $"({b.Op} {Join(b.Values)})",
        CompareExpr c => $"(cmp {Dump(c.Left)}{string.Concat(c.Ops.Zip(c.Comparators, (op, cmp) => $" {op} {Dump(cmp)}"))})",
        CallExpr c => $"(call {Dump(c.Func)}{string.Concat(c.Args.Select(a => " " + DumpArg(a)))})",
        AttributeExpr a => $"(. {Dump(a.Obj)} {a.Name})",
        IndexExpr i => $"([] {Dump(i.Obj)} {Dump(i.Index)})",
        SliceExpr s => $"(slice {DumpOpt(s.Start)} {DumpOpt(s.Stop)} {DumpOpt(s.Step)})",
        StarExpr s => $"*{Dump(s.Value)}",
        IfExpExpr i => $"(ifexp {Dump(i.Cond)} {Dump(i.Then)} {Dump(i.Else)})",
        LambdaExpr l => $"(lambda {DumpParams(l.Params)} {Dump(l.Body)})",
        WalrusExpr w => $"(:= {w.Name} {Dump(w.Value)})",
        YieldExpr y => y.IsFrom ? $"(yield-from {Dump(y.Value!)})" : y.Value is null ? "(yield)" : $"(yield {Dump(y.Value)})",
        FStringExpr f => $"(fstr{string.Concat(f.Parts.Select(DumpFPart))})",
        ComprehensionExpr c => DumpComp(c),
        _ => $"(?{e.GetType().Name})",
    };

    private static string DumpComp(ComprehensionExpr c)
    {
        string kind = c.Kind.ToString().ToLowerInvariant();
        string head = c.Kind == ComprehensionKind.Dict
            ? $"{Dump(c.Key!)}:{Dump(c.Value!)}"
            : Dump(c.Element!);
        string fors = string.Concat(c.Fors.Select(f =>
            $" (for {Dump(f.Target)} in {Dump(f.Iter)}{string.Concat(f.Ifs.Select(i => $" if {Dump(i)}"))})"));
        return $"({kind}comp {head}{fors})";
    }

    private static string DumpFPart(FStringPart p) => p switch
    {
        FStringText t => $" '{t.Text}'",
        FStringValue v => $" {{{Dump(v.Value)}{(v.Conversion != '\0' ? "!" + v.Conversion : "")}{(v.FormatSpec is null ? "" : ":" + string.Concat(v.FormatSpec.Select(p => DumpFPart(p).TrimStart())))}}}",
        _ => "?",
    };

    private static string DumpAlias(ImportAlias a)
        => a.AsName is null ? a.DottedName : $"{a.DottedName} as {a.AsName}";

    private static string DumpArg(CallArg a)
        => a.IsStar ? $"*{Dump(a.Value)}"
            : a.IsDoubleStar ? $"**{Dump(a.Value)}"
            : a.Name is null ? Dump(a.Value)
            : $"{a.Name}={Dump(a.Value)}";

    private static string DumpParams(Parameters p)
    {
        var parts = new List<string>();
        foreach (var param in p.Positional)
            parts.Add(param.Default is null ? param.Name : $"{param.Name}={Dump(param.Default)}");
        if (p.StarArgs is not null)
            parts.Add(p.StarArgs.Length == 0 ? "*" : $"*{p.StarArgs}");
        foreach (var param in p.KwOnly)
            parts.Add(param.Default is null ? param.Name : $"{param.Name}={Dump(param.Default)}");
        if (p.KwArgs is not null)
            parts.Add($"**{p.KwArgs}");
        return $"({string.Join(" ", parts)})";
    }

    private static string DumpDecorators(List<Expr> decorators)
        => decorators.Count == 0 ? "" : $" @[{Join(decorators)}]";

    private static string DumpOpt(Expr? e) => e is null ? "_" : Dump(e);

    private static string Join(IEnumerable<Stmt> stmts) => string.Join(" ", stmts.Select(Dump));
    private static string Join(IEnumerable<Expr> exprs) => string.Join(" ", exprs.Select(Dump));
}

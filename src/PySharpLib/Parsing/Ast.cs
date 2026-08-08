// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using System.Numerics;

namespace PySharpLib.Parsing;

/// <summary>Base AST node. 1-based position in the source.</summary>
public abstract record Node
{
    public int Line { get; init; }
    public int Col { get; init; }
}

public abstract record Expr : Node;
public abstract record Stmt : Node;

// ---------------------------------------------------------------- expressions

public sealed record IntLit(BigInteger Value) : Expr;
public sealed record FloatLit(double Value) : Expr;
public sealed record StrLit(string Value) : Expr;
public sealed record BytesLit(byte[] Value) : Expr;
public sealed record BoolLit(bool Value) : Expr;
public sealed record NoneLit : Expr;
public sealed record EllipsisLit : Expr;

public sealed record NameExpr(string Id) : Expr;

public sealed record TupleExpr(List<Expr> Items) : Expr;
public sealed record ListExpr(List<Expr> Items) : Expr;
public sealed record SetExpr(List<Expr> Items) : Expr;
/// <summary>Null key = <c>**expr</c> unpacking.</summary>
public sealed record DictExpr(List<(Expr? Key, Expr Value)> Items) : Expr;

public sealed record UnaryExpr(string Op, Expr Operand) : Expr;
public sealed record BinaryExpr(Expr Left, string Op, Expr Right) : Expr;
/// <summary>"and"/"or" with short-circuit over n operands.</summary>
public sealed record BoolOpExpr(string Op, List<Expr> Values) : Expr;
/// <summary>Chained comparisons: a &lt; b &lt;= c.</summary>
public sealed record CompareExpr(Expr Left, List<string> Ops, List<Expr> Comparators) : Expr;

public sealed record CallArg(string? Name, Expr Value, bool IsStar, bool IsDoubleStar);
public sealed record CallExpr(Expr Func, List<CallArg> Args) : Expr;

public sealed record AttributeExpr(Expr Obj, string Name) : Expr;
public sealed record IndexExpr(Expr Obj, Expr Index) : Expr;
public sealed record SliceExpr(Expr? Start, Expr? Stop, Expr? Step) : Expr;

/// <summary><c>*expr</c> unpacking in target/display.</summary>
public sealed record StarExpr(Expr Value) : Expr;

public sealed record IfExpExpr(Expr Cond, Expr Then, Expr Else) : Expr;
public sealed record LambdaExpr(Parameters Params, Expr Body) : Expr;
public sealed record WalrusExpr(string Name, Expr Value) : Expr;
public sealed record YieldExpr(Expr? Value, bool IsFrom) : Expr;
/// <summary><c>await expr</c> — only valid inside an <c>async def</c>.</summary>
public sealed record AwaitExpr(Expr Value) : Expr;

/// <summary>Part of an f-string: literal or formatted value.</summary>
public abstract record FStringPart;
public sealed record FStringText(string Text) : FStringPart;
/// <summary>Conversion: '\0' | 'r' | 's' | 'a'. FormatSpec: nested parts (may contain {expr}).</summary>
public sealed record FStringValue(Expr Value, char Conversion, List<FStringPart>? FormatSpec) : FStringPart;
public sealed record FStringExpr(List<FStringPart> Parts) : Expr;

public enum ComprehensionKind { List, Set, Dict, Generator }
public sealed record CompFor(Expr Target, Expr Iter, List<Expr> Ifs);
/// <summary>Element for list/set/gen; for dict Key+Value.</summary>
public sealed record ComprehensionExpr(
    ComprehensionKind Kind, Expr? Element, Expr? Key, Expr? Value, List<CompFor> Fors) : Expr;

// ---------------------------------------------------------------- parameters

public sealed record Param(string Name, Expr? Default, Expr? Annotation);
/// <summary>Function/lambda parameters: positional, *args, keyword-only, **kwargs.</summary>
public sealed record Parameters(
    List<Param> Positional,
    string? StarArgs,          // name of *args, null if absent ("" for a bare '*')
    List<Param> KwOnly,
    string? KwArgs);           // name of **kwargs

// ---------------------------------------------------------------- statements

public sealed record Module(List<Stmt> Body) : Node;

/// <summary>Transparent sequence of statements (a line with several ';'-separated statements).</summary>
public sealed record BlockStmt(List<Stmt> Body) : Stmt;

public sealed record ExprStmt(Expr Value) : Stmt;
/// <summary>a = b = value (Targets in order).</summary>
public sealed record AssignStmt(List<Expr> Targets, Expr Value) : Stmt;
public sealed record AugAssignStmt(Expr Target, string Op, Expr Value) : Stmt;
/// <summary>x: T = v — the annotation is parsed but not evaluated.</summary>
public sealed record AnnAssignStmt(Expr Target, Expr Annotation, Expr? Value) : Stmt;

public sealed record IfStmt(Expr Cond, List<Stmt> Body, List<Stmt> OrElse) : Stmt;
public sealed record WhileStmt(Expr Cond, List<Stmt> Body, List<Stmt> OrElse) : Stmt;
public sealed record ForStmt(Expr Target, Expr Iter, List<Stmt> Body, List<Stmt> OrElse) : Stmt
{
    /// <summary><c>async for</c>: iterate an asynchronous iterator (__aiter__/__anext__).</summary>
    public bool IsAsync { get; init; }
}

public sealed record FuncDef(
    string Name, Parameters Params, List<Stmt> Body,
    List<Expr> Decorators, Expr? Returns, bool IsGenerator) : Stmt
{
    /// <summary><c>async def</c>: calling it produces a coroutine object.</summary>
    public bool IsAsync { get; init; }
}

public sealed record ClassDef(
    string Name, List<CallArg> Bases, List<Stmt> Body, List<Expr> Decorators) : Stmt;

public sealed record ReturnStmt(Expr? Value) : Stmt;
public sealed record PassStmt : Stmt;
public sealed record BreakStmt : Stmt;
public sealed record ContinueStmt : Stmt;
public sealed record RaiseStmt(Expr? Exc, Expr? Cause) : Stmt;

public sealed record ExceptHandler(Expr? Type, string? Name, List<Stmt> Body);
public sealed record TryStmt(
    List<Stmt> Body, List<ExceptHandler> Handlers,
    List<Stmt> OrElse, List<Stmt> Finally) : Stmt;

public sealed record WithItem(Expr Ctx, Expr? Target);
public sealed record WithStmt(List<WithItem> Items, List<Stmt> Body) : Stmt
{
    /// <summary><c>async with</c>: enter/exit via __aenter__/__aexit__ (awaited).</summary>
    public bool IsAsync { get; init; }
}

public sealed record ImportAlias(string DottedName, string? AsName);
public sealed record ImportStmt(List<ImportAlias> Names) : Stmt;
/// <summary>from Module import Names; Level = number of leading dots (relative imports); Star for import *.</summary>
public sealed record FromImportStmt(string Module, int Level, List<ImportAlias> Names, bool Star) : Stmt;

public sealed record GlobalStmt(List<string> Names) : Stmt;
public sealed record NonlocalStmt(List<string> Names) : Stmt;
public sealed record DelStmt(List<Expr> Targets) : Stmt;
public sealed record AssertStmt(Expr Test, Expr? Msg) : Stmt;

// ---------------------------------------------------------------- match/case (PEP 634)

public abstract record Pattern : Node;

/// <summary>Matches by value: <c>==</c> for numbers/strings/bytes, <c>is</c> (identity) for the
/// True/False/None singletons — matching real CPython's own distinction.</summary>
public sealed record LiteralPattern(Expr Value) : Pattern;

/// <summary>A bare name (captures the subject) or <c>_</c> (Name is null: matches anything, binds
/// nothing).</summary>
public sealed record CapturePattern(string? Name) : Pattern;

/// <summary>A dotted name (<c>Color.RED</c>) — compared by <c>==</c>, never a capture, since real
/// Python distinguishes "looks like an attribute access" from "looks like a bare name" purely by
/// syntax (presence of a dot).</summary>
public sealed record ValuePattern(Expr Value) : Pattern;

/// <summary><c>*name</c> / <c>*_</c> inside a sequence pattern — captures the remaining middle
/// slice (Name null for <c>*_</c>, which matches but doesn't bind).</summary>
public sealed record StarPattern(string? Name) : Pattern;

/// <summary><c>[p1, p2, *rest, p3]</c> or <c>(p1, p2)</c> — at most one item may be a
/// <see cref="StarPattern"/>. Matches list/tuple (never str/bytes/bytearray, per PEP 634).</summary>
public sealed record SequencePattern(List<Pattern> Items) : Pattern;

/// <summary><c>{"key": pat, **rest}</c> — RestName is the <c>**rest</c> capture name, or null.
/// Extra unmatched keys are fine unless a rest capture is present; extra pattern keys not present
/// in the subject fail the match.</summary>
public sealed record MappingPattern(List<(Expr Key, Pattern Value)> Items, string? RestName) : Pattern;

/// <summary><c>ClassName(p1, p2, kw=p3)</c>. Positional patterns map through the class's
/// <c>__match_args__</c> tuple — except for a handful of builtin types (int/str/list/...), where a
/// single positional pattern matches the whole subject value directly, per PEP 634's special
/// case.</summary>
public sealed record ClassPattern(
    Expr Cls, List<Pattern> Positional, List<(string Name, Pattern Value)> Keyword) : Pattern;

/// <summary><c>p1 | p2 | p3</c> — first alternative that matches wins. All alternatives must bind
/// the same set of names (not enforced here; a real gap, not attempted since nothing observed needs
/// the enforcement, only the matching behavior).</summary>
public sealed record OrPattern(List<Pattern> Alternatives) : Pattern;

/// <summary><c>pattern as name</c> — matches like Inner, and additionally binds the whole subject
/// to Name.</summary>
public sealed record AsPattern(Pattern Inner, string Name) : Pattern;

public sealed record MatchCase(Pattern Pattern, Expr? Guard, List<Stmt> Body);
public sealed record MatchStmt(Expr Subject, List<MatchCase> Cases) : Stmt;

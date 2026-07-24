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
public sealed record ForStmt(Expr Target, Expr Iter, List<Stmt> Body, List<Stmt> OrElse) : Stmt;

public sealed record FuncDef(
    string Name, Parameters Params, List<Stmt> Body,
    List<Expr> Decorators, Expr? Returns, bool IsGenerator) : Stmt;

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
public sealed record WithStmt(List<WithItem> Items, List<Stmt> Body) : Stmt;

public sealed record ImportAlias(string DottedName, string? AsName);
public sealed record ImportStmt(List<ImportAlias> Names) : Stmt;
/// <summary>from Module import Names; Level = number of leading dots (relative imports); Star for import *.</summary>
public sealed record FromImportStmt(string Module, int Level, List<ImportAlias> Names, bool Star) : Stmt;

public sealed record GlobalStmt(List<string> Names) : Stmt;
public sealed record NonlocalStmt(List<string> Names) : Stmt;
public sealed record DelStmt(List<Expr> Targets) : Stmt;
public sealed record AssertStmt(Expr Test, Expr? Msg) : Stmt;

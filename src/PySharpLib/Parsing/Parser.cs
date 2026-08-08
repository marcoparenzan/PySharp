// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using System.Globalization;
using System.Numerics;
using PySharpLib;
using PySharpLib.Lexing;

namespace PySharpLib.Parsing;

/// <summary>
/// Recursive-descent parser for PySharp's Python 3.x subset.
/// Produce l'AST definito in Ast.cs.
/// </summary>
public sealed class Parser
{
    private static readonly HashSet<string> AugOps = new()
    {
        "+=", "-=", "*=", "/=", "//=", "%=", "@=", "&=", "|=", "^=", "<<=", ">>=", "**=",
    };

    private readonly List<Token> _tokens;
    private readonly string _fileName;
    private int _pos;

    public Parser(List<Token> tokens, string fileName = "<string>")
    {
        _tokens = tokens;
        _fileName = fileName;
    }

    public static Module Parse(string source, string fileName = "<string>")
        => new Parser(Lexer.Tokenize(source, fileName), fileName).ParseModule();

    public static Expr ParseExpression(string source, string fileName = "<fstring>")
    {
        var p = new Parser(Lexer.Tokenize(source, fileName), fileName);
        var e = p.ParseTestListStar();
        p.SkipNewlines();
        p.Expect(TokenKind.EndOfFile);
        return e;
    }

    public Module ParseModule()
    {
        var body = new List<Stmt>();
        SkipNewlines();
        while (Cur.Kind != TokenKind.EndOfFile)
        {
            body.Add(ParseStatement());
            SkipNewlines();
        }
        return new Module(body) { Line = 1, Col = 1 };
    }

    // ================================================================ statement

    private Stmt ParseStatement()
    {
        var t = Cur;
        if (t.Kind == TokenKind.Keyword)
        {
            switch (t.Text)
            {
                case "if": return ParseIf();
                case "while": return ParseWhile();
                case "for": return ParseFor();
                case "try": return ParseTry();
                case "with": return ParseWith();
                case "def": return ParseFuncDef(new List<Expr>());
                case "class": return ParseClassDef(new List<Expr>());
                case "async": return ParseAsyncStatement(new List<Expr>());
            }
        }
        // `match`/`case` are soft keywords (real CPython, PEP 634): plain identifiers everywhere
        // else, special only when `match <subject>:` is immediately followed by an indented block
        // whose first line starts with `case` — checked by lookahead, not backtracking, since a
        // bare `:` at bracket-depth 0 inside an expression never occurs in valid Python (slice/
        // lambda/dict colons are always nested inside brackets), so the scan is unambiguous.
        if (t.Kind == TokenKind.Name && t.Text == "match" && LooksLikeMatchStatement())
            return ParseMatchStatement();
        if (t.Is(TokenKind.Op, "@"))
            return ParseDecorated();
        return ParseSimpleStatementLine();
    }

    /// <summary>Lookahead-only (no token consumption): does `match` here start a match statement?
    /// Scans forward at bracket-depth 0 for the first ':', then checks NEWLINE INDENT "case".</summary>
    private bool LooksLikeMatchStatement()
    {
        int i = _pos + 1;
        int depth = 0;
        while (i < _tokens.Count)
        {
            var tok = _tokens[i];
            if (tok.Kind == TokenKind.Op && tok.Text is "(" or "[" or "{")
            {
                depth++;
            }
            else if (tok.Kind == TokenKind.Op && tok.Text is ")" or "]" or "}")
            {
                depth--;
            }
            else if (depth == 0 && tok.Kind == TokenKind.Op && tok.Text == ":")
            {
                int j = i + 1;
                if (j >= _tokens.Count || _tokens[j].Kind != TokenKind.Newline)
                    return false;
                j++;
                while (j < _tokens.Count && _tokens[j].Kind == TokenKind.Newline)
                    j++;
                if (j >= _tokens.Count || _tokens[j].Kind != TokenKind.Indent)
                    return false;
                j++;
                while (j < _tokens.Count && _tokens[j].Kind == TokenKind.Newline)
                    j++;
                return j < _tokens.Count && _tokens[j].Kind == TokenKind.Name && _tokens[j].Text == "case";
            }
            else if (depth == 0 && tok.Kind is TokenKind.Newline or TokenKind.EndOfFile)
            {
                return false;
            }
            else if (depth == 0 && tok.Kind == TokenKind.Op && tok.Text == "=")
            {
                return false; // `match = ...` / `match, x = ...`: plain assignment
            }
            i++;
        }
        return false;
    }

    private Stmt ParseMatchStatement()
    {
        var t = Cur;
        Next(); // 'match'
        var subject = ParseTestListStar();
        Expect(TokenKind.Op, ":");
        Expect(TokenKind.Newline);
        SkipNewlines();
        Expect(TokenKind.Indent);
        SkipNewlines();
        var cases = new List<MatchCase>();
        while (Cur.Kind == TokenKind.Name && Cur.Text == "case")
        {
            Next(); // 'case'
            var pattern = ParseTopLevelPatterns();
            Expr? guard = MatchKw("if") ? ParseNamedTest() : null;
            var body = ParseSuite();
            cases.Add(new MatchCase(pattern, guard, body));
            SkipNewlines();
        }
        Expect(TokenKind.Dedent);
        if (cases.Count == 0)
            throw Error("expected at least one 'case' block", t.Line, t.Column);
        return new MatchStmt(subject, cases) { Line = t.Line, Col = t.Column };
    }

    // ---------------------------------------------------------------- patterns (PEP 634)

    /// <summary>Top of a `case` line: either a single pattern, or a bare comma-separated sequence
    /// (no brackets) — <c>case a, b:</c> / <c>case x, *rest:</c> — folded into a SequencePattern.</summary>
    private Pattern ParseTopLevelPatterns()
    {
        var t = Cur;
        var first = ParseMaybeStarPattern();
        if (!Cur.Is(TokenKind.Op, ","))
            return first;
        var items = new List<Pattern> { first };
        while (MatchOp(","))
        {
            if (Cur.Is(TokenKind.Op, ":") || Cur.Is(TokenKind.Keyword, "if"))
                break; // trailing comma
            items.Add(ParseMaybeStarPattern());
        }
        return new SequencePattern(items) { Line = t.Line, Col = t.Column };
    }

    private Pattern ParseMaybeStarPattern()
    {
        if (Cur.Is(TokenKind.Op, "*"))
        {
            var t = Cur;
            Next();
            string name = ExpectName();
            return new StarPattern(name == "_" ? null : name) { Line = t.Line, Col = t.Column };
        }
        return ParsePattern();
    }

    /// <summary>pattern: or_pattern ['as' NAME].</summary>
    private Pattern ParsePattern()
    {
        var e = ParseOrPattern();
        if (Cur.Is(TokenKind.Keyword, "as"))
        {
            Next();
            string name = ExpectName();
            return new AsPattern(e, name) { Line = e.Line, Col = e.Col };
        }
        return e;
    }

    /// <summary>or_pattern: closed_pattern ('|' closed_pattern)*.</summary>
    private Pattern ParseOrPattern()
    {
        var first = ParseClosedPattern();
        if (!Cur.Is(TokenKind.Op, "|"))
            return first;
        var alts = new List<Pattern> { first };
        while (MatchOp("|"))
            alts.Add(ParseClosedPattern());
        return new OrPattern(alts) { Line = first.Line, Col = first.Col };
    }

    private Pattern ParseClosedPattern()
    {
        var t = Cur;
        switch (t.Kind)
        {
            case TokenKind.Keyword when t.Text == "True":
                Next();
                return new LiteralPattern(new BoolLit(true) { Line = t.Line, Col = t.Column }) { Line = t.Line, Col = t.Column };
            case TokenKind.Keyword when t.Text == "False":
                Next();
                return new LiteralPattern(new BoolLit(false) { Line = t.Line, Col = t.Column }) { Line = t.Line, Col = t.Column };
            case TokenKind.Keyword when t.Text == "None":
                Next();
                return new LiteralPattern(new NoneLit { Line = t.Line, Col = t.Column }) { Line = t.Line, Col = t.Column };

            case TokenKind.Number:
                Next();
                return new LiteralPattern(ParseNumberLiteral(t)) { Line = t.Line, Col = t.Column };

            case TokenKind.Str:
            case TokenKind.Bytes:
                return new LiteralPattern(ParseStringConcatenation()) { Line = t.Line, Col = t.Column };

            case TokenKind.Op when t.Text == "-":
            {
                Next();
                var numTok = Expect(TokenKind.Number);
                var lit = ParseNumberLiteral(numTok);
                Expr negated = lit switch
                {
                    IntLit il => new IntLit(-il.Value) { Line = t.Line, Col = t.Column },
                    FloatLit fl => new FloatLit(-fl.Value) { Line = t.Line, Col = t.Column },
                    _ => lit,
                };
                return new LiteralPattern(negated) { Line = t.Line, Col = t.Column };
            }

            case TokenKind.Op when t.Text == "(":
                return ParseParenPattern();
            case TokenKind.Op when t.Text == "[":
                return ParseSequencePatternBracketed("[", "]");
            case TokenKind.Op when t.Text == "{":
                return ParseMappingPattern();

            case TokenKind.Name:
                return ParseCaptureValueOrClassPattern();
        }
        throw Error($"invalid pattern: unexpected '{t.Text}'", t.Line, t.Column);
    }

    private Pattern ParseParenPattern()
    {
        var t = Expect(TokenKind.Op, "(");
        if (Cur.Is(TokenKind.Op, ")"))
        {
            Next();
            return new SequencePattern(new List<Pattern>()) { Line = t.Line, Col = t.Column };
        }
        var items = new List<Pattern> { ParseMaybeStarPattern() };
        bool sawComma = false;
        while (MatchOp(","))
        {
            sawComma = true;
            if (Cur.Is(TokenKind.Op, ")"))
                break;
            items.Add(ParseMaybeStarPattern());
        }
        Expect(TokenKind.Op, ")");
        // A single, non-starred item with no trailing comma is just a parenthesized (group)
        // pattern — the parens are transparent, matching real CPython's group_pattern.
        if (!sawComma && items.Count == 1 && items[0] is not StarPattern)
            return items[0];
        return new SequencePattern(items) { Line = t.Line, Col = t.Column };
    }

    private Pattern ParseSequencePatternBracketed(string open, string close)
    {
        var t = Expect(TokenKind.Op, open);
        var items = new List<Pattern>();
        while (!Cur.Is(TokenKind.Op, close))
        {
            items.Add(ParseMaybeStarPattern());
            if (!MatchOp(","))
                break;
        }
        Expect(TokenKind.Op, close);
        return new SequencePattern(items) { Line = t.Line, Col = t.Column };
    }

    private Pattern ParseMappingPattern()
    {
        var t = Expect(TokenKind.Op, "{");
        var items = new List<(Expr, Pattern)>();
        string? restName = null;
        while (!Cur.Is(TokenKind.Op, "}"))
        {
            if (MatchOp("**"))
            {
                restName = ExpectName();
            }
            else
            {
                var key = ParseTest();
                Expect(TokenKind.Op, ":");
                items.Add((key, ParsePattern()));
            }
            if (!MatchOp(","))
                break;
        }
        Expect(TokenKind.Op, "}");
        return new MappingPattern(items, restName) { Line = t.Line, Col = t.Column };
    }

    /// <summary>Dispatches a bare NAME in pattern position: <c>_</c> (wildcard) / <c>Dotted.Name</c>
    /// (value pattern, compared by ==) / <c>Cls(...)</c> (class pattern) / a plain capture.</summary>
    private Pattern ParseCaptureValueOrClassPattern()
    {
        var t = Cur;
        string name = ExpectName();
        if (name == "_" && !Cur.Is(TokenKind.Op, ".") && !Cur.Is(TokenKind.Op, "("))
            return new CapturePattern(null) { Line = t.Line, Col = t.Column };

        Expr expr = new NameExpr(name) { Line = t.Line, Col = t.Column };
        bool isDotted = false;
        while (Cur.Is(TokenKind.Op, "."))
        {
            Next();
            expr = new AttributeExpr(expr, ExpectName()) { Line = t.Line, Col = t.Column };
            isDotted = true;
        }

        if (Cur.Is(TokenKind.Op, "("))
            return ParseClassPatternArgs(expr);
        if (isDotted)
            return new ValuePattern(expr) { Line = t.Line, Col = t.Column };
        return new CapturePattern(name) { Line = t.Line, Col = t.Column };
    }

    private Pattern ParseClassPatternArgs(Expr cls)
    {
        var t = Expect(TokenKind.Op, "(");
        var positional = new List<Pattern>();
        var keyword = new List<(string, Pattern)>();
        while (!Cur.Is(TokenKind.Op, ")"))
        {
            if (Cur.Kind == TokenKind.Name && PeekNext.Is(TokenKind.Op, "="))
            {
                string kwName = ExpectName();
                Next(); // '='
                keyword.Add((kwName, ParsePattern()));
            }
            else
            {
                positional.Add(ParsePattern());
            }
            if (!MatchOp(","))
                break;
        }
        Expect(TokenKind.Op, ")");
        return new ClassPattern(cls, positional, keyword) { Line = t.Line, Col = t.Column };
    }

    /// <summary>Simple-statement line: small_stmt (';' small_stmt)* NEWLINE. If more than one, the caller handles lists.</summary>
    private Stmt ParseSimpleStatementLine()
    {
        var stmts = ParseSimpleStatements();
        if (stmts.Count == 1)
            return stmts[0];
        // Several statements on the same line: representing them as If(True) would be ugly;
        // we use a transparent block.
        return new BlockStmt(stmts) { Line = stmts[0].Line, Col = stmts[0].Col };
    }

    private List<Stmt> ParseSimpleStatements()
    {
        var stmts = new List<Stmt> { ParseSmallStatement() };
        while (MatchOp(";"))
        {
            if (Cur.Kind is TokenKind.Newline or TokenKind.EndOfFile)
                break;
            stmts.Add(ParseSmallStatement());
        }
        ExpectNewlineOrEof();
        return stmts;
    }

    private Stmt ParseSmallStatement()
    {
        var t = Cur;
        if (t.Kind == TokenKind.Keyword)
        {
            switch (t.Text)
            {
                case "pass": Next(); return new PassStmt { Line = t.Line, Col = t.Column };
                case "break": Next(); return new BreakStmt { Line = t.Line, Col = t.Column };
                case "continue": Next(); return new ContinueStmt { Line = t.Line, Col = t.Column };
                case "return":
                {
                    Next();
                    Expr? value = AtStatementEnd ? null : ParseTestListStar();
                    return new ReturnStmt(value) { Line = t.Line, Col = t.Column };
                }
                case "raise":
                {
                    Next();
                    Expr? exc = null, cause = null;
                    if (!AtStatementEnd)
                    {
                        exc = ParseTest();
                        if (MatchKw("from"))
                            cause = ParseTest();
                    }
                    return new RaiseStmt(exc, cause) { Line = t.Line, Col = t.Column };
                }
                case "global":
                {
                    Next();
                    return new GlobalStmt(ParseNameList()) { Line = t.Line, Col = t.Column };
                }
                case "nonlocal":
                {
                    Next();
                    return new NonlocalStmt(ParseNameList()) { Line = t.Line, Col = t.Column };
                }
                case "del":
                {
                    Next();
                    var targets = new List<Expr> { ParseTargetAtomChain() };
                    while (MatchOp(","))
                        targets.Add(ParseTargetAtomChain());
                    return new DelStmt(targets) { Line = t.Line, Col = t.Column };
                }
                case "assert":
                {
                    Next();
                    var test = ParseTest();
                    Expr? msg = MatchOp(",") ? ParseTest() : null;
                    return new AssertStmt(test, msg) { Line = t.Line, Col = t.Column };
                }
                case "import": return ParseImport();
                case "from": return ParseFromImport();
                case "yield":
                {
                    var y = ParseTestListStar(); // yield expression as a statement
                    return new ExprStmt(y) { Line = t.Line, Col = t.Column };
                }
            }
        }
        return ParseExprOrAssignStatement();
    }

    private Stmt ParseExprOrAssignStatement()
    {
        var t = Cur;
        var first = ParseTestListStar();

        // Annotated assignment: target ':' annotation ['=' value]
        if (Cur.Is(TokenKind.Op, ":"))
        {
            Next();
            var ann = ParseTest();
            Expr? value = MatchOp("=") ? ParseTestListStar() : null;
            return new AnnAssignStmt(first, ann, value) { Line = t.Line, Col = t.Column };
        }

        // Augmented assignment
        if (Cur.Kind == TokenKind.Op && AugOps.Contains(Cur.Text))
        {
            string op = Cur.Text;
            Next();
            var value = Cur.Is(TokenKind.Keyword, "yield") ? ParseYieldExpr() : ParseTestListStar();
            return new AugAssignStmt(first, op[..^1], value) { Line = t.Line, Col = t.Column };
        }

        // Plain assignment (anche multiplo: a = b = c)
        if (Cur.Is(TokenKind.Op, "="))
        {
            var targets = new List<Expr> { first };
            Expr value;
            while (true)
            {
                Next();
                value = Cur.Is(TokenKind.Keyword, "yield") ? ParseYieldExpr() : ParseTestListStar();
                if (Cur.Is(TokenKind.Op, "="))
                {
                    targets.Add(value);
                    continue;
                }
                break;
            }
            foreach (var target in targets)
                ValidateTarget(target);
            return new AssignStmt(targets, value) { Line = t.Line, Col = t.Column };
        }

        return new ExprStmt(first) { Line = t.Line, Col = t.Column };
    }

    private void ValidateTarget(Expr e)
    {
        switch (e)
        {
            case NameExpr or AttributeExpr or IndexExpr:
                return;
            case StarExpr s:
                ValidateTarget(s.Value);
                return;
            case TupleExpr tup:
                foreach (var item in tup.Items) ValidateTarget(item);
                return;
            case ListExpr list:
                foreach (var item in list.Items) ValidateTarget(item);
                return;
            default:
                throw Error("cannot assign to expression", e.Line, e.Col);
        }
    }

    /// <summary>Target for del: atom with trailers (no trailing call).</summary>
    private Expr ParseTargetAtomChain() => ParseTest();

    private List<string> ParseNameList()
    {
        var names = new List<string> { ExpectName() };
        while (MatchOp(","))
            names.Add(ExpectName());
        return names;
    }

    private Stmt ParseImport()
    {
        var t = Expect(TokenKind.Keyword, "import");
        var names = new List<ImportAlias> { ParseImportAlias() };
        while (MatchOp(","))
            names.Add(ParseImportAlias());
        return new ImportStmt(names) { Line = t.Line, Col = t.Column };
    }

    private ImportAlias ParseImportAlias()
    {
        string dotted = ParseDottedName();
        string? asName = MatchKw("as") ? ExpectName() : null;
        return new ImportAlias(dotted, asName);
    }

    private string ParseDottedName()
    {
        var parts = new List<string> { ExpectName() };
        while (Cur.Is(TokenKind.Op, ".") && PeekNext.Kind == TokenKind.Name)
        {
            Next();
            parts.Add(ExpectName());
        }
        return string.Join(".", parts);
    }

    private Stmt ParseFromImport()
    {
        var t = Expect(TokenKind.Keyword, "from");
        int level = 0;
        while (Cur.Is(TokenKind.Op, ".") || Cur.Is(TokenKind.Op, "..."))
        {
            level += Cur.Text.Length;
            Next();
        }
        string module = Cur.Kind == TokenKind.Name ? ParseDottedName() : "";
        if (level == 0 && module.Length == 0)
            throw Error("expected module name after 'from'");
        Expect(TokenKind.Keyword, "import");

        if (MatchOp("*"))
            return new FromImportStmt(module, level, new List<ImportAlias>(), Star: true)
                { Line = t.Line, Col = t.Column };

        bool parens = MatchOp("(");
        var names = new List<ImportAlias>();
        do
        {
            if (parens && Cur.Is(TokenKind.Op, ")"))
                break; // trailing comma
            string name = ExpectName();
            string? asName = MatchKw("as") ? ExpectName() : null;
            names.Add(new ImportAlias(name, asName));
        } while (MatchOp(","));
        if (parens)
            Expect(TokenKind.Op, ")");
        return new FromImportStmt(module, level, names, Star: false) { Line = t.Line, Col = t.Column };
    }

    // ---------------------------------------------------------------- compound

    private Stmt ParseIf()
    {
        var t = Expect(TokenKind.Keyword, "if");
        var cond = ParseNamedTest();
        var body = ParseSuite();
        var orElse = new List<Stmt>();
        if (Cur.Is(TokenKind.Keyword, "elif"))
        {
            orElse.Add(ParseIfFromElif());
        }
        else if (MatchKw("else"))
        {
            orElse = ParseSuite();
        }
        return new IfStmt(cond, body, orElse) { Line = t.Line, Col = t.Column };
    }

    private Stmt ParseIfFromElif()
    {
        var t = Expect(TokenKind.Keyword, "elif");
        var cond = ParseNamedTest();
        var body = ParseSuite();
        var orElse = new List<Stmt>();
        if (Cur.Is(TokenKind.Keyword, "elif"))
            orElse.Add(ParseIfFromElif());
        else if (MatchKw("else"))
            orElse = ParseSuite();
        return new IfStmt(cond, body, orElse) { Line = t.Line, Col = t.Column };
    }

    private Stmt ParseWhile()
    {
        var t = Expect(TokenKind.Keyword, "while");
        var cond = ParseNamedTest();
        var body = ParseSuite();
        var orElse = MatchKw("else") ? ParseSuite() : new List<Stmt>();
        return new WhileStmt(cond, body, orElse) { Line = t.Line, Col = t.Column };
    }

    private Stmt ParseFor()
    {
        var t = Expect(TokenKind.Keyword, "for");
        var target = ParseTargetList();
        Expect(TokenKind.Keyword, "in");
        var iter = ParseTestListStar();
        var body = ParseSuite();
        var orElse = MatchKw("else") ? ParseSuite() : new List<Stmt>();
        ValidateTarget(target);
        return new ForStmt(target, iter, body, orElse) { Line = t.Line, Col = t.Column };
    }

    private Stmt ParseTry()
    {
        var t = Expect(TokenKind.Keyword, "try");
        var body = ParseSuite();
        var handlers = new List<ExceptHandler>();
        var orElse = new List<Stmt>();
        var finallyBody = new List<Stmt>();

        while (Cur.Is(TokenKind.Keyword, "except"))
        {
            Next();
            Expr? type = null;
            string? name = null;
            if (!Cur.Is(TokenKind.Op, ":"))
            {
                type = ParseTest();
                if (MatchKw("as"))
                    name = ExpectName();
            }
            handlers.Add(new ExceptHandler(type, name, ParseSuite()));
        }
        if (handlers.Count > 0 && MatchKw("else"))
            orElse = ParseSuite();
        if (MatchKw("finally"))
            finallyBody = ParseSuite();
        if (handlers.Count == 0 && finallyBody.Count == 0)
            throw Error("expected 'except' or 'finally' block");
        return new TryStmt(body, handlers, orElse, finallyBody) { Line = t.Line, Col = t.Column };
    }

    private Stmt ParseWith()
    {
        var t = Expect(TokenKind.Keyword, "with");
        var items = new List<WithItem>();
        do
        {
            var ctx = ParseTest();
            Expr? target = null;
            if (MatchKw("as"))
            {
                target = ParseTest();
                ValidateTarget(target);
            }
            items.Add(new WithItem(ctx, target));
        } while (MatchOp(","));
        var body = ParseSuite();
        return new WithStmt(items, body) { Line = t.Line, Col = t.Column };
    }

    private Stmt ParseDecorated()
    {
        var decorators = new List<Expr>();
        while (Cur.Is(TokenKind.Op, "@"))
        {
            Next();
            decorators.Add(ParseNamedTest());
            ExpectNewlineOrEof();
            SkipNewlines();
        }
        if (Cur.Is(TokenKind.Keyword, "def"))
            return ParseFuncDef(decorators);
        if (Cur.Is(TokenKind.Keyword, "class"))
            return ParseClassDef(decorators);
        if (Cur.Is(TokenKind.Keyword, "async"))
            return ParseAsyncStatement(decorators);
        throw Error("expected 'def' or 'class' after decorator");
    }

    /// <summary>'async' (def | for | with). The 'async' token has already been peeked.</summary>
    private Stmt ParseAsyncStatement(List<Expr> decorators)
    {
        Expect(TokenKind.Keyword, "async");
        if (Cur.Is(TokenKind.Keyword, "def"))
        {
            var fn = (FuncDef)ParseFuncDef(decorators);
            return fn with { IsAsync = true };
        }
        if (decorators.Count > 0)
            throw Error("decorators may only precede 'async def'");
        if (Cur.Is(TokenKind.Keyword, "for"))
        {
            var f = (ForStmt)ParseFor();
            return f with { IsAsync = true };
        }
        if (Cur.Is(TokenKind.Keyword, "with"))
        {
            var w = (WithStmt)ParseWith();
            return w with { IsAsync = true };
        }
        throw Error("expected 'def', 'for' or 'with' after 'async'");
    }

    private Stmt ParseFuncDef(List<Expr> decorators)
    {
        var t = Expect(TokenKind.Keyword, "def");
        string name = ExpectName();
        Expect(TokenKind.Op, "(");
        var parameters = ParseParameters(closing: ")");
        Expect(TokenKind.Op, ")");
        Expr? returns = MatchOp("->") ? ParseTest() : null;
        var body = ParseSuite();
        bool isGenerator = ContainsYield(body);
        return new FuncDef(name, parameters, body, decorators, returns, isGenerator)
            { Line = t.Line, Col = t.Column };
    }

    private Stmt ParseClassDef(List<Expr> decorators)
    {
        var t = Expect(TokenKind.Keyword, "class");
        string name = ExpectName();
        var bases = new List<CallArg>();
        if (MatchOp("("))
        {
            if (!Cur.Is(TokenKind.Op, ")"))
                bases = ParseCallArgs();
            Expect(TokenKind.Op, ")");
        }
        var body = ParseSuite();
        return new ClassDef(name, bases, body, decorators) { Line = t.Line, Col = t.Column };
    }

    /// <summary>':' NEWLINE INDENT stmt+ DEDENT, oppure ':' simple_stmts sulla stessa riga.</summary>
    private List<Stmt> ParseSuite()
    {
        Expect(TokenKind.Op, ":");
        if (Cur.Kind == TokenKind.Newline)
        {
            Next();
            SkipNewlines();
            Expect(TokenKind.Indent);
            var body = new List<Stmt>();
            SkipNewlines();
            while (Cur.Kind != TokenKind.Dedent && Cur.Kind != TokenKind.EndOfFile)
            {
                body.Add(ParseStatement());
                SkipNewlines();
            }
            Expect(TokenKind.Dedent);
            return body;
        }
        return ParseSimpleStatements();
    }

    // ---------------------------------------------------------------- parametri

    /// <summary>def/lambda parameters. closing = ")" for def, ":" for lambda.</summary>
    private Parameters ParseParameters(string closing)
    {
        var positional = new List<Param>();
        var kwOnly = new List<Param>();
        string? starArgs = null;
        string? kwArgs = null;
        bool seenStar = false;

        while (!Cur.Is(TokenKind.Op, closing))
        {
            if (MatchOp("**"))
            {
                kwArgs = ExpectName();
                if (closing != ":" && MatchOp(":"))
                    ParseTest(); // annotazione su **kwargs: parsata e ignorata
                MatchOp(",");
                break;
            }
            if (MatchOp("*"))
            {
                if (seenStar)
                    throw Error("duplicate '*' in parameter list");
                seenStar = true;
                starArgs = Cur.Kind == TokenKind.Name ? ExpectName() : "";
                if (starArgs.Length > 0 && closing != ":" && MatchOp(":"))
                    ParseTest(); // annotazione su *args: parsata e ignorata
                if (!MatchOp(","))
                    break;
                continue;
            }
            if (MatchOp("/"))
            {
                // positional-only marker: accettato e ignorato
                if (!MatchOp(","))
                    break;
                continue;
            }

            string name = ExpectName();
            // no annotations in lambdas: there ':' closes the parameter list
            Expr? annotation = closing != ":" && MatchOp(":") ? ParseTest() : null;
            Expr? def = MatchOp("=") ? ParseTest() : null;
            (seenStar ? kwOnly : positional).Add(new Param(name, def, annotation));
            if (!MatchOp(","))
                break;
        }
        return new Parameters(positional, starArgs, kwOnly, kwArgs);
    }

    // ================================================================ espressioni

    /// <summary>testlist with star_expr and commas → single Expr or TupleExpr.</summary>
    private Expr ParseTestListStar()
    {
        var t = Cur;
        if (t.Is(TokenKind.Keyword, "yield"))
            return ParseYieldExpr();
        var first = ParseTestOrStar();
        if (!Cur.Is(TokenKind.Op, ","))
            return first;
        var items = new List<Expr> { first };
        while (MatchOp(","))
        {
            if (AtExpressionEnd)
                break; // trailing comma
            items.Add(ParseTestOrStar());
        }
        return new TupleExpr(items) { Line = t.Line, Col = t.Column };
    }

    /// <summary>
    /// for/comprehension target: exprlist at the or_expr (bitor) level, so the keyword
    /// 'in' is not consumed as a comparison operator.
    /// </summary>
    private Expr ParseTargetList()
    {
        var t = Cur;
        var first = ParseTargetItem();
        if (!Cur.Is(TokenKind.Op, ","))
            return first;
        var items = new List<Expr> { first };
        while (MatchOp(","))
        {
            if (Cur.Is(TokenKind.Keyword, "in"))
                break;
            items.Add(ParseTargetItem());
        }
        return new TupleExpr(items) { Line = t.Line, Col = t.Column };
    }

    private Expr ParseTargetItem()
    {
        if (Cur.Is(TokenKind.Op, "*"))
        {
            var t = Cur;
            Next();
            return new StarExpr(ParseBitOr()) { Line = t.Line, Col = t.Column };
        }
        return ParseBitOr();
    }

    private Expr ParseTestOrStar()
    {
        if (Cur.Is(TokenKind.Op, "*"))
        {
            var t = Cur;
            Next();
            return new StarExpr(ParseOr()) { Line = t.Line, Col = t.Column };
        }
        return ParseNamedTest();
    }

    /// <summary>namedexpr_test: test [':=' test] — walrus.</summary>
    private Expr ParseNamedTest()
    {
        var e = ParseTest();
        if (Cur.Is(TokenKind.Op, ":="))
        {
            if (e is not NameExpr name)
                throw Error("cannot use assignment expression with non-name target", e.Line, e.Col);
            Next();
            var value = ParseTest();
            return new WalrusExpr(name.Id, value) { Line = e.Line, Col = e.Col };
        }
        return e;
    }

    /// <summary>test: or_test ['if' or_test 'else' test] | lambda.</summary>
    private Expr ParseTest()
    {
        if (Cur.Is(TokenKind.Keyword, "lambda"))
            return ParseLambda();
        var e = ParseOr();
        if (Cur.Is(TokenKind.Keyword, "if"))
        {
            Next();
            var cond = ParseOr();
            Expect(TokenKind.Keyword, "else");
            var orElse = ParseTest();
            return new IfExpExpr(cond, e, orElse) { Line = e.Line, Col = e.Col };
        }
        return e;
    }

    private Expr ParseLambda()
    {
        var t = Expect(TokenKind.Keyword, "lambda");
        var parameters = ParseParameters(closing: ":");
        Expect(TokenKind.Op, ":");
        var body = ParseTest();
        return new LambdaExpr(parameters, body) { Line = t.Line, Col = t.Column };
    }

    private Expr ParseYieldExpr()
    {
        var t = Expect(TokenKind.Keyword, "yield");
        if (MatchKw("from"))
            return new YieldExpr(ParseTest(), IsFrom: true) { Line = t.Line, Col = t.Column };
        Expr? value = AtStatementEnd || Cur.Is(TokenKind.Op, ")") ? null : ParseTestListStar();
        return new YieldExpr(value, IsFrom: false) { Line = t.Line, Col = t.Column };
    }

    private Expr ParseOr()
    {
        var e = ParseAnd();
        if (!Cur.Is(TokenKind.Keyword, "or"))
            return e;
        var values = new List<Expr> { e };
        while (MatchKw("or"))
            values.Add(ParseAnd());
        return new BoolOpExpr("or", values) { Line = e.Line, Col = e.Col };
    }

    private Expr ParseAnd()
    {
        var e = ParseNot();
        if (!Cur.Is(TokenKind.Keyword, "and"))
            return e;
        var values = new List<Expr> { e };
        while (MatchKw("and"))
            values.Add(ParseNot());
        return new BoolOpExpr("and", values) { Line = e.Line, Col = e.Col };
    }

    private Expr ParseNot()
    {
        if (Cur.Is(TokenKind.Keyword, "not"))
        {
            var t = Cur;
            Next();
            return new UnaryExpr("not", ParseNot()) { Line = t.Line, Col = t.Column };
        }
        return ParseComparison();
    }

    private Expr ParseComparison()
    {
        var e = ParseBitOr();
        var ops = new List<string>();
        var comparators = new List<Expr>();
        while (true)
        {
            string? op = null;
            if (Cur.Kind == TokenKind.Op && Cur.Text is "<" or ">" or "==" or ">=" or "<=" or "!=")
            {
                op = Cur.Text;
                Next();
            }
            else if (Cur.Is(TokenKind.Keyword, "in"))
            {
                op = "in";
                Next();
            }
            else if (Cur.Is(TokenKind.Keyword, "not") && PeekNext.Is(TokenKind.Keyword, "in"))
            {
                op = "not in";
                Next();
                Next();
            }
            else if (Cur.Is(TokenKind.Keyword, "is"))
            {
                Next();
                op = MatchKw("not") ? "is not" : "is";
            }

            if (op is null)
                break;
            ops.Add(op);
            comparators.Add(ParseBitOr());
        }
        return ops.Count == 0
            ? e
            : new CompareExpr(e, ops, comparators) { Line = e.Line, Col = e.Col };
    }

    private Expr ParseBitOr()
    {
        var e = ParseBitXor();
        while (Cur.Is(TokenKind.Op, "|"))
        {
            Next();
            e = new BinaryExpr(e, "|", ParseBitXor()) { Line = e.Line, Col = e.Col };
        }
        return e;
    }

    private Expr ParseBitXor()
    {
        var e = ParseBitAnd();
        while (Cur.Is(TokenKind.Op, "^"))
        {
            Next();
            e = new BinaryExpr(e, "^", ParseBitAnd()) { Line = e.Line, Col = e.Col };
        }
        return e;
    }

    private Expr ParseBitAnd()
    {
        var e = ParseShift();
        while (Cur.Is(TokenKind.Op, "&"))
        {
            Next();
            e = new BinaryExpr(e, "&", ParseShift()) { Line = e.Line, Col = e.Col };
        }
        return e;
    }

    private Expr ParseShift()
    {
        var e = ParseArith();
        while (Cur.Kind == TokenKind.Op && Cur.Text is "<<" or ">>")
        {
            string op = Cur.Text;
            Next();
            e = new BinaryExpr(e, op, ParseArith()) { Line = e.Line, Col = e.Col };
        }
        return e;
    }

    private Expr ParseArith()
    {
        var e = ParseTerm();
        while (Cur.Kind == TokenKind.Op && Cur.Text is "+" or "-")
        {
            string op = Cur.Text;
            Next();
            e = new BinaryExpr(e, op, ParseTerm()) { Line = e.Line, Col = e.Col };
        }
        return e;
    }

    private Expr ParseTerm()
    {
        var e = ParseFactor();
        while (Cur.Kind == TokenKind.Op && Cur.Text is "*" or "/" or "//" or "%" or "@")
        {
            string op = Cur.Text;
            Next();
            e = new BinaryExpr(e, op, ParseFactor()) { Line = e.Line, Col = e.Col };
        }
        return e;
    }

    private Expr ParseFactor()
    {
        if (Cur.Kind == TokenKind.Op && Cur.Text is "+" or "-" or "~")
        {
            var t = Cur;
            Next();
            return new UnaryExpr(t.Text, ParseFactor()) { Line = t.Line, Col = t.Column };
        }
        return ParsePower();
    }

    private Expr ParsePower()
    {
        // power: await_expr ['**' factor];  await_expr: ['await'] atom_expr
        Expr e;
        if (Cur.Is(TokenKind.Keyword, "await"))
        {
            var t = Cur;
            Next();
            e = new AwaitExpr(ParseAtomExpr()) { Line = t.Line, Col = t.Column };
        }
        else
        {
            e = ParseAtomExpr();
        }
        if (Cur.Is(TokenKind.Op, "**"))
        {
            Next();
            // ** is right-associative and binds less tightly than a unary on the right
            e = new BinaryExpr(e, "**", ParseFactor()) { Line = e.Line, Col = e.Col };
        }
        return e;
    }

    private Expr ParseAtomExpr()
    {
        var e = ParseAtom();
        while (true)
        {
            if (Cur.Is(TokenKind.Op, "("))
            {
                Next();
                var args = Cur.Is(TokenKind.Op, ")") ? new List<CallArg>() : ParseCallArgs();
                Expect(TokenKind.Op, ")");
                e = new CallExpr(e, args) { Line = e.Line, Col = e.Col };
            }
            else if (Cur.Is(TokenKind.Op, "["))
            {
                Next();
                var index = ParseSubscriptList();
                Expect(TokenKind.Op, "]");
                e = new IndexExpr(e, index) { Line = e.Line, Col = e.Col };
            }
            else if (Cur.Is(TokenKind.Op, ".") )
            {
                Next();
                string name = ExpectName();
                e = new AttributeExpr(e, name) { Line = e.Line, Col = e.Col };
            }
            else
            {
                return e;
            }
        }
    }

    private List<CallArg> ParseCallArgs()
    {
        var args = new List<CallArg>();
        do
        {
            if (Cur.Is(TokenKind.Op, ")"))
                break; // trailing comma
            if (MatchOp("*"))
            {
                args.Add(new CallArg(null, ParseTest(), IsStar: true, IsDoubleStar: false));
            }
            else if (MatchOp("**"))
            {
                args.Add(new CallArg(null, ParseTest(), IsStar: false, IsDoubleStar: true));
            }
            else if (Cur.Kind == TokenKind.Name && PeekNext.Is(TokenKind.Op, "="))
            {
                string name = ExpectName();
                Next(); // '='
                args.Add(new CallArg(name, ParseTest(), IsStar: false, IsDoubleStar: false));
            }
            else
            {
                var value = ParseNamedTest();
                if (Cur.Is(TokenKind.Keyword, "for"))
                {
                    // generator expression as the sole argument: f(x for x in y)
                    var fors = ParseCompFors();
                    value = new ComprehensionExpr(ComprehensionKind.Generator, value, null, null, fors)
                        { Line = value.Line, Col = value.Col };
                }
                args.Add(new CallArg(null, value, IsStar: false, IsDoubleStar: false));
            }
        } while (MatchOp(","));
        return args;
    }

    private Expr ParseSubscriptList()
    {
        var t = Cur;
        var first = ParseSubscript();
        if (!Cur.Is(TokenKind.Op, ","))
            return first;
        var items = new List<Expr> { first };
        while (MatchOp(","))
        {
            if (Cur.Is(TokenKind.Op, "]"))
                break;
            items.Add(ParseSubscript());
        }
        return new TupleExpr(items) { Line = t.Line, Col = t.Column };
    }

    private Expr ParseSubscript()
    {
        var t = Cur;
        Expr? start = null, stop = null, step = null;
        if (!Cur.Is(TokenKind.Op, ":"))
        {
            start = ParseTest();
            if (!Cur.Is(TokenKind.Op, ":"))
                return start;
        }
        Expect(TokenKind.Op, ":");
        if (!Cur.Is(TokenKind.Op, ":") && !Cur.Is(TokenKind.Op, "]") && !Cur.Is(TokenKind.Op, ","))
            stop = ParseTest();
        if (MatchOp(":"))
        {
            if (!Cur.Is(TokenKind.Op, "]") && !Cur.Is(TokenKind.Op, ","))
                step = ParseTest();
        }
        return new SliceExpr(start, stop, step) { Line = t.Line, Col = t.Column };
    }

    // ---------------------------------------------------------------- atomi

    private Expr ParseAtom()
    {
        var t = Cur;
        switch (t.Kind)
        {
            case TokenKind.Number:
                Next();
                return ParseNumberLiteral(t);

            case TokenKind.Str:
            case TokenKind.Bytes:
                return ParseStringConcatenation();

            case TokenKind.Name:
                Next();
                return new NameExpr(t.Text) { Line = t.Line, Col = t.Column };

            case TokenKind.Keyword when t.Text == "True":
                Next();
                return new BoolLit(true) { Line = t.Line, Col = t.Column };
            case TokenKind.Keyword when t.Text == "False":
                Next();
                return new BoolLit(false) { Line = t.Line, Col = t.Column };
            case TokenKind.Keyword when t.Text == "None":
                Next();
                return new NoneLit { Line = t.Line, Col = t.Column };
            case TokenKind.Keyword when t.Text == "lambda":
                return ParseLambda();

            case TokenKind.Op when t.Text == "...":
                Next();
                return new EllipsisLit { Line = t.Line, Col = t.Column };

            case TokenKind.Op when t.Text == "(":
                return ParseParenAtom();
            case TokenKind.Op when t.Text == "[":
                return ParseListAtom();
            case TokenKind.Op when t.Text == "{":
                return ParseDictOrSetAtom();
        }
        throw Error($"unexpected token '{t.Text}'");
    }

    private Expr ParseNumberLiteral(Token t)
    {
        string text = t.Text.Replace("_", "");
        try
        {
            if (text.StartsWith("0x") || text.StartsWith("0X"))
                return new IntLit(ParseBigIntBase(text[2..], 16)) { Line = t.Line, Col = t.Column };
            if (text.StartsWith("0o") || text.StartsWith("0O"))
                return new IntLit(ParseBigIntBase(text[2..], 8)) { Line = t.Line, Col = t.Column };
            if (text.StartsWith("0b") || text.StartsWith("0B"))
                return new IntLit(ParseBigIntBase(text[2..], 2)) { Line = t.Line, Col = t.Column };
            if (text.Contains('.') || text.Contains('e') || text.Contains('E'))
                return new FloatLit(double.Parse(text, CultureInfo.InvariantCulture))
                    { Line = t.Line, Col = t.Column };
            return new IntLit(BigInteger.Parse(text)) { Line = t.Line, Col = t.Column };
        }
        catch (FormatException)
        {
            throw Error($"invalid number literal '{t.Text}'", t.Line, t.Column);
        }
    }

    private static BigInteger ParseBigIntBase(string digits, int numBase)
    {
        if (digits.Length == 0)
            throw new FormatException();
        BigInteger v = 0;
        foreach (char c in digits)
        {
            int d = Uri.IsHexDigit(c) ? Uri.FromHex(c) : throw new FormatException();
            if (d >= numBase)
                throw new FormatException();
            v = v * numBase + d;
        }
        return v;
    }

    /// <summary>Concatenazione di letterali stringa/bytes adiacenti, incluse f-string.</summary>
    private Expr ParseStringConcatenation()
    {
        var t = Cur;
        var stringTokens = new List<Token>();
        while (Cur.Kind is TokenKind.Str or TokenKind.Bytes)
        {
            stringTokens.Add(Cur);
            Next();
        }

        bool anyBytes = stringTokens.Any(s => s.Kind == TokenKind.Bytes);
        bool anyStr = stringTokens.Any(s => s.Kind == TokenKind.Str);
        if (anyBytes && anyStr)
            throw Error("cannot mix bytes and nonbytes literals", t.Line, t.Column);

        if (anyBytes)
        {
            var all = stringTokens.SelectMany(s => s.BytesValue!).ToArray();
            return new BytesLit(all) { Line = t.Line, Col = t.Column };
        }

        if (stringTokens.Any(s => s.IsFString))
        {
            var parts = new List<FStringPart>();
            foreach (var s in stringTokens)
            {
                if (s.IsFString)
                    parts.AddRange(ParseFStringParts(s));
                else if (s.StringValue!.Length > 0)
                    parts.Add(new FStringText(s.StringValue!));
            }
            return new FStringExpr(parts) { Line = t.Line, Col = t.Column };
        }

        string joined = string.Concat(stringTokens.Select(s => s.StringValue));
        return new StrLit(joined) { Line = t.Line, Col = t.Column };
    }

    // ---------------------------------------------------------------- f-string

    private List<FStringPart> ParseFStringParts(Token token)
    {
        string s = token.StringValue!;
        var parts = new List<FStringPart>();
        var literal = new System.Text.StringBuilder();
        int i = 0;

        void FlushLiteral()
        {
            if (literal.Length == 0)
                return;
            string text = literal.ToString();
            if (!token.IsRaw)
                text = Lexer.DecodeEscapes(text, _fileName, token.Line, token.Column);
            parts.Add(new FStringText(text));
            literal.Clear();
        }

        while (i < s.Length)
        {
            char c = s[i];
            if (c == '{' && i + 1 < s.Length && s[i + 1] == '{')
            {
                literal.Append('{');
                i += 2;
                continue;
            }
            if (c == '}' && i + 1 < s.Length && s[i + 1] == '}')
            {
                literal.Append('}');
                i += 2;
                continue;
            }
            if (c == '}')
                throw Error("f-string: single '}' is not allowed", token.Line, token.Column);
            if (c != '{')
            {
                literal.Append(c);
                i++;
                continue;
            }

            // Espressione {expr[!conv][:spec]}
            FlushLiteral();
            i++; // consuma '{'
            int exprStart = i;
            int depth = 0;
            char inString = '\0';
            int conversionPos = -1, specPos = -1, debugEqPos = -1;
            while (i < s.Length)
            {
                char e = s[i];
                if (inString != '\0')
                {
                    if (e == '\\') { i += 2; continue; }
                    if (e == inString) inString = '\0';
                    i++;
                    continue;
                }
                if (e is '\'' or '"') { inString = e; i++; continue; }
                if (e is '(' or '[' or '{') { depth++; i++; continue; }
                if (e is ')' or ']' or '}' && depth > 0) { depth--; i++; continue; }
                if (depth == 0)
                {
                    if (e == '}')
                        break;
                    // debug specifier {expr=}: '=' followed by '}'/'!'/':' and not ==,!=,<=,>=,:=
                    if (e == '=' && conversionPos < 0 && specPos < 0 && debugEqPos < 0
                        && (i + 1 >= s.Length || s[i + 1] is '}' or '!' or ':')
                        && (i == exprStart || s[i - 1] is not ('=' or '!' or '<' or '>' or ':')))
                    {
                        debugEqPos = i;
                        i++;
                        continue;
                    }
                    if (e == '!' && i + 1 < s.Length && s[i + 1] != '=' && conversionPos < 0 && specPos < 0)
                    {
                        conversionPos = i;
                        i += 2;
                        continue;
                    }
                    if (e == ':' && specPos < 0)
                    {
                        specPos = i;
                        i++;
                        continue;
                    }
                }
                i++;
            }
            if (i >= s.Length)
                throw Error("f-string: expecting '}'", token.Line, token.Column);

            int exprEndCandidate = debugEqPos >= 0 ? debugEqPos
                : conversionPos >= 0 ? conversionPos
                : specPos >= 0 ? specPos : i;
            string exprText = s[exprStart..exprEndCandidate].Trim();
            if (exprText.Length == 0)
                throw Error("f-string: empty expression not allowed", token.Line, token.Column);

            // {expr=}: first emits the literal text "expr=" (raw source, including the '=')
            if (debugEqPos >= 0)
                parts.Add(new FStringText(s[exprStart..(debugEqPos + 1)]));

            char conversion = '\0';
            if (conversionPos >= 0)
                conversion = s[conversionPos + 1];
            else if (debugEqPos >= 0 && specPos < 0)
                conversion = 'r'; // {x=} without a format spec uses repr

            List<FStringPart>? spec = null;
            if (specPos >= 0)
            {
                string specText = s[(specPos + 1)..i];
                spec = ParseFormatSpec(specText, token);
            }

            var expr = ParseExpression(exprText, _fileName);
            parts.Add(new FStringValue(expr, conversion, spec));
            i++; // consuma '}'
        }
        FlushLiteral();
        return parts;
    }

    /// <summary>f-string format spec, which may itself contain {expr} (one level).</summary>
    private List<FStringPart> ParseFormatSpec(string spec, Token token)
    {
        var parts = new List<FStringPart>();
        var literal = new System.Text.StringBuilder();
        int i = 0;
        while (i < spec.Length)
        {
            char c = spec[i];
            if (c == '{')
            {
                int close = spec.IndexOf('}', i);
                if (close < 0)
                    throw Error("f-string: expecting '}' in format spec", token.Line, token.Column);
                if (literal.Length > 0)
                {
                    parts.Add(new FStringText(literal.ToString()));
                    literal.Clear();
                }
                var expr = ParseExpression(spec[(i + 1)..close], _fileName);
                parts.Add(new FStringValue(expr, '\0', null));
                i = close + 1;
                continue;
            }
            literal.Append(c);
            i++;
        }
        if (literal.Length > 0)
            parts.Add(new FStringText(literal.ToString()));
        return parts;
    }

    // ---------------------------------------------------------------- display

    private Expr ParseParenAtom()
    {
        var t = Expect(TokenKind.Op, "(");
        if (Cur.Is(TokenKind.Op, ")"))
        {
            Next();
            return new TupleExpr(new List<Expr>()) { Line = t.Line, Col = t.Column };
        }

        if (Cur.Is(TokenKind.Keyword, "yield"))
        {
            var y = ParseYieldExpr();
            Expect(TokenKind.Op, ")");
            return y;
        }

        var first = ParseTestOrStar();

        if (Cur.Is(TokenKind.Keyword, "for"))
        {
            var fors = ParseCompFors();
            Expect(TokenKind.Op, ")");
            return new ComprehensionExpr(ComprehensionKind.Generator, first, null, null, fors)
                { Line = t.Line, Col = t.Column };
        }

        if (Cur.Is(TokenKind.Op, ","))
        {
            var items = new List<Expr> { first };
            while (MatchOp(","))
            {
                if (Cur.Is(TokenKind.Op, ")"))
                    break;
                items.Add(ParseTestOrStar());
            }
            Expect(TokenKind.Op, ")");
            return new TupleExpr(items) { Line = t.Line, Col = t.Column };
        }

        Expect(TokenKind.Op, ")");
        return first; // espressione tra parentesi
    }

    private Expr ParseListAtom()
    {
        var t = Expect(TokenKind.Op, "[");
        if (Cur.Is(TokenKind.Op, "]"))
        {
            Next();
            return new ListExpr(new List<Expr>()) { Line = t.Line, Col = t.Column };
        }

        var first = ParseTestOrStar();
        if (Cur.Is(TokenKind.Keyword, "for"))
        {
            var fors = ParseCompFors();
            Expect(TokenKind.Op, "]");
            return new ComprehensionExpr(ComprehensionKind.List, first, null, null, fors)
                { Line = t.Line, Col = t.Column };
        }

        var items = new List<Expr> { first };
        while (MatchOp(","))
        {
            if (Cur.Is(TokenKind.Op, "]"))
                break;
            items.Add(ParseTestOrStar());
        }
        Expect(TokenKind.Op, "]");
        return new ListExpr(items) { Line = t.Line, Col = t.Column };
    }

    private Expr ParseDictOrSetAtom()
    {
        var t = Expect(TokenKind.Op, "{");
        if (Cur.Is(TokenKind.Op, "}"))
        {
            Next();
            return new DictExpr(new List<(Expr?, Expr)>()) { Line = t.Line, Col = t.Column };
        }

        // **unpacking → dict
        if (MatchOp("**"))
        {
            var items = new List<(Expr?, Expr)> { ((Expr?)null, ParseOr()) };
            while (MatchOp(","))
            {
                if (Cur.Is(TokenKind.Op, "}"))
                    break;
                items.Add(ParseDictItem());
            }
            Expect(TokenKind.Op, "}");
            return new DictExpr(items) { Line = t.Line, Col = t.Column };
        }

        var firstExpr = ParseTestOrStar();

        // dict: key ':' value
        if (Cur.Is(TokenKind.Op, ":"))
        {
            Next();
            var firstValue = ParseTest();
            if (Cur.Is(TokenKind.Keyword, "for"))
            {
                var fors = ParseCompFors();
                Expect(TokenKind.Op, "}");
                return new ComprehensionExpr(ComprehensionKind.Dict, null, firstExpr, firstValue, fors)
                    { Line = t.Line, Col = t.Column };
            }
            var items = new List<(Expr?, Expr)> { (firstExpr, firstValue) };
            while (MatchOp(","))
            {
                if (Cur.Is(TokenKind.Op, "}"))
                    break;
                items.Add(ParseDictItem());
            }
            Expect(TokenKind.Op, "}");
            return new DictExpr(items) { Line = t.Line, Col = t.Column };
        }

        // set
        if (Cur.Is(TokenKind.Keyword, "for"))
        {
            var fors = ParseCompFors();
            Expect(TokenKind.Op, "}");
            return new ComprehensionExpr(ComprehensionKind.Set, firstExpr, null, null, fors)
                { Line = t.Line, Col = t.Column };
        }
        var setItems = new List<Expr> { firstExpr };
        while (MatchOp(","))
        {
            if (Cur.Is(TokenKind.Op, "}"))
                break;
            setItems.Add(ParseTestOrStar());
        }
        Expect(TokenKind.Op, "}");
        return new SetExpr(setItems) { Line = t.Line, Col = t.Column };
    }

    private (Expr?, Expr) ParseDictItem()
    {
        if (MatchOp("**"))
            return (null, ParseOr());
        var key = ParseTest();
        Expect(TokenKind.Op, ":");
        return (key, ParseTest());
    }

    private List<CompFor> ParseCompFors()
    {
        var fors = new List<CompFor>();
        while (Cur.Is(TokenKind.Keyword, "for"))
        {
            Next();
            var target = ParseTargetList();
            ValidateTarget(target);
            Expect(TokenKind.Keyword, "in");
            var iter = ParseOr();
            var ifs = new List<Expr>();
            while (Cur.Is(TokenKind.Keyword, "if"))
            {
                Next();
                ifs.Add(ParseOr());
            }
            fors.Add(new CompFor(target, iter, ifs));
        }
        return fors;
    }

    // ---------------------------------------------------------------- yield detection

    private static bool ContainsYield(List<Stmt> body)
        => body.Any(ContainsYield);

    private static bool ContainsYield(Stmt s) => s switch
    {
        ExprStmt e => ContainsYield(e.Value),
        AssignStmt a => ContainsYield(a.Value) || a.Targets.Any(ContainsYield),
        AugAssignStmt a => ContainsYield(a.Value),
        AnnAssignStmt a => a.Value is not null && ContainsYield(a.Value),
        ReturnStmt r => r.Value is not null && ContainsYield(r.Value),
        IfStmt i => ContainsYield(i.Cond) || ContainsYield(i.Body) || ContainsYield(i.OrElse),
        WhileStmt w => ContainsYield(w.Cond) || ContainsYield(w.Body) || ContainsYield(w.OrElse),
        ForStmt f => ContainsYield(f.Iter) || ContainsYield(f.Body) || ContainsYield(f.OrElse),
        TryStmt t => ContainsYield(t.Body) || t.Handlers.Any(h => ContainsYield(h.Body))
                     || ContainsYield(t.OrElse) || ContainsYield(t.Finally),
        WithStmt w => w.Items.Any(item => ContainsYield(item.Ctx)) || ContainsYield(w.Body),
        BlockStmt b => ContainsYield(b.Body),
        RaiseStmt r => (r.Exc is not null && ContainsYield(r.Exc)),
        MatchStmt m => ContainsYield(m.Subject)
            || m.Cases.Any(c => (c.Guard is not null && ContainsYield(c.Guard)) || ContainsYield(c.Body)),
        _ => false, // nested FuncDef/ClassDef do NOT propagate yield
    };

    private static bool ContainsYield(Expr e) => e switch
    {
        YieldExpr => true,
        BinaryExpr b => ContainsYield(b.Left) || ContainsYield(b.Right),
        UnaryExpr u => ContainsYield(u.Operand),
        BoolOpExpr b => b.Values.Any(ContainsYield),
        CompareExpr c => ContainsYield(c.Left) || c.Comparators.Any(ContainsYield),
        CallExpr c => ContainsYield(c.Func) || c.Args.Any(a => ContainsYield(a.Value)),
        AttributeExpr a => ContainsYield(a.Obj),
        IndexExpr i => ContainsYield(i.Obj) || ContainsYield(i.Index),
        SliceExpr s => (s.Start is not null && ContainsYield(s.Start))
                       || (s.Stop is not null && ContainsYield(s.Stop))
                       || (s.Step is not null && ContainsYield(s.Step)),
        TupleExpr t => t.Items.Any(ContainsYield),
        ListExpr l => l.Items.Any(ContainsYield),
        SetExpr s => s.Items.Any(ContainsYield),
        DictExpr d => d.Items.Any(kv => (kv.Key is not null && ContainsYield(kv.Key)) || ContainsYield(kv.Value)),
        IfExpExpr i => ContainsYield(i.Cond) || ContainsYield(i.Then) || ContainsYield(i.Else),
        StarExpr s => ContainsYield(s.Value),
        WalrusExpr w => ContainsYield(w.Value),
        _ => false, // Lambda does NOT propagate
    };

    // ---------------------------------------------------------------- helper

    private Token Cur => _tokens[_pos];
    private Token PeekNext => _pos + 1 < _tokens.Count ? _tokens[_pos + 1] : _tokens[^1];

    private bool AtStatementEnd
        => Cur.Kind is TokenKind.Newline or TokenKind.EndOfFile || Cur.Is(TokenKind.Op, ";");

    /// <summary>Plausible end of an expression list (after a trailing comma).</summary>
    private bool AtExpressionEnd
        => AtStatementEnd
           || Cur.Kind is TokenKind.Op && Cur.Text is ")" or "]" or "}" or "=" or ":"
           || Cur.Kind == TokenKind.Keyword && Cur.Text is "in" or "for";

    private void Next() => _pos++;

    private void SkipNewlines()
    {
        while (Cur.Kind == TokenKind.Newline)
            Next();
    }

    private bool MatchOp(string text)
    {
        if (Cur.Is(TokenKind.Op, text))
        {
            Next();
            return true;
        }
        return false;
    }

    private bool MatchKw(string text)
    {
        if (Cur.Is(TokenKind.Keyword, text))
        {
            Next();
            return true;
        }
        return false;
    }

    private Token Expect(TokenKind kind, string? text = null)
    {
        var t = Cur;
        if (t.Kind != kind || (text is not null && t.Text != text))
            throw Error($"expected {text ?? kind.ToString()}, got '{t.Text}'", t.Line, t.Column);
        Next();
        return t;
    }

    private void ExpectNewlineOrEof()
    {
        if (Cur.Kind == TokenKind.Newline)
        {
            Next();
            return;
        }
        if (Cur.Kind is TokenKind.EndOfFile or TokenKind.Dedent)
            return;
        throw Error($"expected end of line, got '{Cur.Text}'");
    }

    private string ExpectName() => Expect(TokenKind.Name).Text;

    private PySyntaxError Error(string message)
        => new(message, _fileName, Cur.Line, Cur.Column);

    private PySyntaxError Error(string message, int line, int col)
        => new(message, _fileName, line, col);
}

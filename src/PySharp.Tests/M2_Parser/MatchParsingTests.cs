// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

namespace PySharp.Tests.M2_Parser;

/// <summary>
/// match/case (PEP 634) parsing: `match`/`case` are soft keywords, detected by lookahead (does
/// `match &lt;expr&gt;:` end in NEWLINE INDENT "case"?), not backtracking — real CPython's own PEG
/// grammar treats them the same way. See FASTAPI_PLAN.md Phase 3.
/// </summary>
public class MatchParsingTests
{
    [Fact]
    public void Literal_capture_and_wildcard_patterns()
        => Assert.Equal(
            "(match x (case 0 [(pass)]) (case y [(pass)]) (case _ [(pass)]))",
            P.Mod("""
                match x:
                    case 0:
                        pass
                    case y:
                        pass
                    case _:
                        pass
                """));

    [Fact]
    public void Or_pattern_and_guard()
        => Assert.Equal(
            "(match x (case (or 1 2 3) if (cmp x > 0) [(pass)]))",
            P.Mod("""
                match x:
                    case 1 | 2 | 3 if x > 0:
                        pass
                """));

    [Fact]
    public void Sequence_pattern_with_star()
        => Assert.Equal(
            "(match x (case (seq a *rest b) [(pass)]))",
            P.Mod("""
                match x:
                    case [a, *rest, b]:
                        pass
                """));

    [Fact]
    public void Mapping_pattern_with_rest()
        => Assert.Equal(
            "(match x (case (map 'a':1 **rest) [(pass)]))",
            P.Mod("""
                match x:
                    case {"a": 1, **rest}:
                        pass
                """));

    [Fact]
    public void Class_pattern_positional_and_keyword()
        => Assert.Equal(
            "(match x (case (cls Point 0 y=y) [(pass)]))",
            P.Mod("""
                match x:
                    case Point(0, y=y):
                        pass
                """));

    [Fact]
    public void Value_pattern_is_a_dotted_name_not_a_capture()
        => Assert.Equal(
            "(match x (case (. Color RED) [(pass)]))",
            P.Mod("""
                match x:
                    case Color.RED:
                        pass
                """));

    [Fact]
    public void As_pattern()
        => Assert.Equal(
            "(match x (case (as (seq a b) pair) [(pass)]))",
            P.Mod("""
                match x:
                    case [a, b] as pair:
                        pass
                """));

    [Fact]
    public void Bare_comma_pattern_becomes_a_sequence_pattern()
        => Assert.Equal(
            "(match (tuple x y) (case (seq 0 0) [(pass)]) (case (seq a b) [(pass)]))",
            P.Mod("""
                match x, y:
                    case 0, 0:
                        pass
                    case a, b:
                        pass
                """));

    [Fact]
    public void Match_used_as_a_plain_identifier_is_not_a_match_statement()
    {
        // `match` not followed by `<expr>: NEWLINE INDENT case` is just a name — the lookahead must
        // correctly reject these, matching real CPython's soft-keyword disambiguation.
        Assert.Equal("(= [match] 5)", P.Mod("match = 5"));
        Assert.Equal("(expr (call match 1 2))", P.Mod("match(1, 2)"));
        Assert.Equal("(expr (+ match 1))", P.Mod("match + 1"));
    }

    [Fact]
    public void Negative_number_literal_pattern()
        => Assert.Equal(
            "(match x (case -1 [(pass)]))",
            P.Mod("""
                match x:
                    case -1:
                        pass
                """));

    [Fact]
    public void Parenthesized_single_pattern_is_transparent_group()
        => Assert.Equal(
            "(match x (case 1 [(pass)]))",
            P.Mod("""
                match x:
                    case (1):
                        pass
                """));
}

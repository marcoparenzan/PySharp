// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

namespace PySharp.Tests.M17_Match;

/// <summary>
/// match/case (PEP 634) execution semantics: real structural pattern matching, not a stub. Found
/// via anyio's real `match self.status: case TaskHandle.Status.PENDING: ...` (_core/_tasks.py),
/// itself a real dependency of starlette — the frontier that closed out Phase 3's first probe-driven
/// round. See FASTAPI_PLAN.md.
/// </summary>
public class MatchExecutionTests
{
    private static string Run(string body) => Py.Run(body).TrimEnd('\n');

    [Fact]
    public void Literal_or_pattern_and_wildcard_pick_the_first_matching_case()
        => Assert.Equal("zero\nsmall\nsmall\nother", Run("""
            def describe(x):
                match x:
                    case 0:
                        return "zero"
                    case 1 | 2 | 3:
                        return "small"
                    case _:
                        return "other"
            for v in [0, 2, 3, 100]:
                print(describe(v))
            """));

    [Fact]
    public void Capture_pattern_binds_the_subject()
        => Assert.Equal("5", Run("""
            def f(x):
                match x:
                    case n:
                        return n
            print(f(5))
            """));

    [Fact]
    public void Guard_is_checked_only_after_the_pattern_itself_matches()
        => Assert.Equal("negative\npositive\nzero", Run("""
            def sign(x):
                match x:
                    case n if n < 0:
                        return "negative"
                    case n if n > 0:
                        return "positive"
                    case _:
                        return "zero"
            print(sign(-5))
            print(sign(5))
            print(sign(0))
            """));

    [Fact]
    public void Failed_guard_falls_through_to_the_next_case()
        => Assert.Equal("second", Run("""
            def f(x):
                match x:
                    case n if n > 100:
                        return "first"
                    case n if n == 5:
                        return "second"
            print(f(5))
            """));

    [Fact]
    public void None_true_false_singleton_patterns_use_identity_not_equality()
        // Regression-shaped: 1 == True in Python, but `case True:` must NOT match 1 — real CPython
        // uses `is` for the None/True/False singleton patterns specifically.
        => Assert.Equal("other\nis true\nother", Run("""
            def f(x):
                match x:
                    case True:
                        return "is true"
                    case _:
                        return "other"
            print(f(1))
            print(f(True))
            print(f(0))
            """));

    [Fact]
    public void Sequence_pattern_matches_lists_and_tuples_with_star_capture()
        => Assert.Equal("empty\none: 1\ntwo: 1,2\nhead 1 rest [2, 3, 4]", Run("""
            def seq(x):
                match x:
                    case []:
                        return "empty"
                    case [a]:
                        return f"one: {a}"
                    case [a, b]:
                        return f"two: {a},{b}"
                    case [a, *rest]:
                        return f"head {a} rest {rest}"
                    case _:
                        return "no match"
            print(seq([]))
            print(seq((1,)))
            print(seq([1, 2]))
            print(seq([1, 2, 3, 4]))
            """));

    [Fact]
    public void Sequence_pattern_does_not_match_strings_or_bytes()
        // Regression-shaped: str/bytes are iterable but PEP 634 explicitly excludes them from
        // sequence patterns (they'd otherwise "match" character-by-character, which is never what
        // real code wants).
        => Assert.Equal("not a sequence", Run("""
            def f(x):
                match x:
                    case [a, b]:
                        return f"seq {a} {b}"
                    case _:
                        return "not a sequence"
            print(f("ab"))
            """));

    [Fact]
    public void Mapping_pattern_matches_dicts_with_rest_capture()
        => Assert.Equal(
            "point 1,2\nother circle rest={'r': 5}\nno match",
            Run("""
                def m(d):
                    match d:
                        case {"type": "point", "x": x, "y": y}:
                            return f"point {x},{y}"
                        case {"type": t, **rest}:
                            return f"other {t} rest={rest}"
                        case _:
                            return "no match"
                print(m({"type": "point", "x": 1, "y": 2}))
                print(m({"type": "circle", "r": 5}))
                print(m([1, 2]))
                """));

    [Fact]
    public void Class_pattern_uses_match_args_for_positional_and_getattr_for_keyword()
        => Assert.Equal(
            "origin\ny-axis at 5\nx-axis at 5\ndiagonal at 3\nsomewhere else",
            Run("""
                class Point:
                    __match_args__ = ("x", "y")
                    def __init__(self, x, y):
                        self.x = x
                        self.y = y

                def where(p):
                    match p:
                        case Point(0, 0):
                            return "origin"
                        case Point(x=0, y=y):
                            return f"y-axis at {y}"
                        case Point(x=x, y=0):
                            return f"x-axis at {x}"
                        case Point(x, y) if x == y:
                            return f"diagonal at {x}"
                        case Point():
                            return "somewhere else"

                for p in [Point(0, 0), Point(0, 5), Point(5, 0), Point(3, 3), Point(1, 2)]:
                    print(where(p))
                """));

    [Fact]
    public void Class_pattern_special_cases_builtin_types_to_match_the_whole_value()
        // PEP 634's special case: int()/str()/etc. have no real __match_args__, so a single
        // positional sub-pattern matches the whole subject value directly.
        => Assert.Equal("int 5\nstr hi\nother", Run("""
            def f(x):
                match x:
                    case int(n):
                        return f"int {n}"
                    case str(s):
                        return f"str {s}"
                    case _:
                        return "other"
            print(f(5))
            print(f("hi"))
            print(f(3.5))
            """));

    [Fact]
    public void As_pattern_binds_the_whole_subject_alongside_the_inner_pattern()
        => Assert.Equal("[1, 2] len=2", Run("""
            def f(x):
                match x:
                    case [a, b] as whole:
                        return f"{whole} len={len(whole)}"
            print(f([1, 2]))
            """));

    [Fact]
    public void Bare_comma_subject_and_pattern_form_a_tuple_match()
        => Assert.Equal("both zero\n1 2", Run("""
            def tv(x, y):
                match x, y:
                    case 0, 0:
                        return "both zero"
                    case a, b:
                        return f"{a} {b}"
            print(tv(0, 0))
            print(tv(1, 2))
            """));

    [Fact]
    public void Value_pattern_compares_a_dotted_name_by_equality_not_capture()
        // The exact real-world shape that originally blocked FASTAPI_PLAN.md Phase 3: anyio's
        // `match self.status: case TaskHandle.Status.PENDING: ...`.
        => Assert.Equal("pending\nfinished\ncancelled", Run("""
            from enum import Enum

            class TaskHandle:
                class Status(Enum):
                    PENDING = 1
                    FINISHED = 2
                    CANCELLED = 3

            def describe(status):
                match status:
                    case TaskHandle.Status.PENDING:
                        return "pending"
                    case TaskHandle.Status.FINISHED:
                        return "finished"
                    case TaskHandle.Status.CANCELLED:
                        return "cancelled"

            print(describe(TaskHandle.Status.PENDING))
            print(describe(TaskHandle.Status.FINISHED))
            print(describe(TaskHandle.Status.CANCELLED))
            """));

    [Fact]
    public void Match_used_as_a_plain_identifier_still_works()
        // `match`/`case` are soft keywords: real code (e.g. `re.match`) using the name `match` for
        // completely unrelated purposes must keep working.
        => Assert.Equal("123\n6", Run("""
            import re
            match = re.match(r"\d+", "123abc")
            print(match.group())

            def f(match):
                return match + 1
            print(f(5))
            """));

    [Fact]
    public void No_case_matches_and_the_statement_is_a_silent_no_op()
        => Assert.Equal("after", Run("""
            def f(x):
                match x:
                    case 1:
                        print("one")
                print("after")
            f(2)
            """));
}

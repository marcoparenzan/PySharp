// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

namespace PySharp.Tests.M4_Functions;

/// <summary>Real CPython `exec(source, globals=None, locals=None)` — full statement-level dynamic
/// execution (unlike `eval()`, which only handles a single expression), added while probing real
/// sqlalchemy (see ORM_PLAN.md Phase 0): `util/langhelpers.py`'s `_exec_code_in_env` dynamically
/// generates a wrapper function's source (to preserve the original's real signature for
/// introspection) and `exec()`s it — a common real-world metaprogramming idiom well beyond just this
/// one package. Mirrors `eval()`'s own three call shapes.</summary>
public class ExecTests
{
    private static string Run(string body) => Py.Run(body).TrimEnd('\n');

    [Fact]
    public void Exec_with_no_namespace_runs_in_the_callers_own_current_scope()
        => Assert.Equal("2", Run("""
            x = 1
            exec("x = x + 1")
            print(x)
            """));

    [Fact]
    public void Exec_with_a_single_globals_dict_writes_directly_into_it_like_real_module_level_code()
        => Assert.Equal("20\n20", Run("""
            env = {"y": 10}
            exec("y = y * 2\ndef f(): return y", env)
            print(env["y"])
            print(env["f"]())
            """));

    [Fact]
    public void Exec_with_separate_globals_and_locals_writes_new_bindings_into_locals_not_globals()
        => Assert.Equal("3\nFalse", Run("""
            g = {"a": 1}
            l = {"b": 2}
            exec("c = a + b", g, l)
            print(l["c"])
            print("c" in g)
            """));

    [Fact]
    public void Exec_can_define_a_multi_statement_function_and_call_it_back_matching_the_real_sqlalchemy_idiom()
        => Assert.Equal("7", Run("""
            code = "def wrapper(a, b):\n    total = a + b\n    return total\n"
            env = {}
            exec(code, env)
            print(env["wrapper"](3, 4))
            """));

    [Fact]
    public void Exec_returns_None()
        => Assert.Equal("None", Run("""
            print(exec("pass"))
            """));

    [Fact]
    public void Exec_propagates_a_real_exception_raised_by_the_executed_code()
        => Assert.Equal("True", Run("""
            try:
                exec("raise ValueError('boom')")
                print(False)
            except ValueError as e:
                print(str(e) == "boom")
            """));
}

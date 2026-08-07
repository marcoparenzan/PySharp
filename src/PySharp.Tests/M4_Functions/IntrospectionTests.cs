// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

namespace PySharp.Tests.M4_Functions;

/// <summary>
/// Function introspection required by the "API" scenario (roadmap 2.x):
/// __annotations__ populated with the type callables + __name__ on builtins.
/// It is the mechanism that enables FastAPI-style validation/injection.
/// </summary>
public class IntrospectionTests
{
    [Fact]
    public void Annotations_are_populated_with_type_callables()
    {
        string src = """
            def handler(item_id: int, q: str = 'x', flag: bool = False):
                return item_id
            ann = handler.__annotations__
            print(ann['item_id'] is int)
            print(ann['q'] is str)
            print(ann['flag'] is bool)
            """;
        Assert.Equal("True\nTrue\nTrue\n", Py.Run(src));
    }

    [Fact]
    public void Annotations_preserve_parameter_order()
    {
        string src = """
            def h(a: int, b: str, c: bool):
                return 0
            print(list(h.__annotations__))
            """;
        Assert.Equal("['a', 'b', 'c']\n", Py.Run(src));
    }

    [Fact]
    public void Unannotated_function_has_empty_annotations()
    {
        Assert.Equal("{}\n", Py.Run("def f(a, b):\n    return a\nprint(f.__annotations__)"));
    }

    [Fact]
    public void User_assigned_annotations_win()
    {
        string src = """
            def f(a: int):
                return a
            f.__annotations__ = {'a': 'custom'}
            print(f.__annotations__['a'])
            """;
        Assert.Equal("custom\n", Py.Run(src));
    }

    [Fact]
    public void Builtin_types_expose_name()
    {
        Assert.Equal("int str float bool\n",
            Py.Run("print(int.__name__, str.__name__, float.__name__, bool.__name__)"));
    }

    [Fact]
    public void Code_exposes_parameter_names_in_order()
    {
        string src = """
            def h(name, item_id: int, limit: int = 10):
                return 0
            print(h.__code__.co_varnames)
            print(h.__code__.co_argcount)
            """;
        Assert.Equal("('name', 'item_id', 'limit')\n3\n", Py.Run(src));
    }

    [Fact]
    public void Code_orders_star_and_kwonly_and_kwargs()
    {
        string src = """
            def h(a, *rest, key=1, **extra):
                return 0
            c = h.__code__
            print(c.co_varnames)
            print(c.co_argcount, c.co_kwonlyargcount)
            """;
        Assert.Equal("('a', 'rest', 'key', 'extra')\n1 1\n", Py.Run(src));
    }

    [Fact]
    public void Return_annotation_is_captured_under_return_key()
    {
        string src = """
            def f(x: int) -> str:
                return str(x)
            print(f.__annotations__['return'] is str)
            print('return' in f.__annotations__)
            """;
        Assert.Equal("True\nTrue\n", Py.Run(src));
    }

    [Fact]
    public void No_return_annotation_means_no_return_key()
    {
        Assert.Equal("False\n", Py.Run("def g(a):\n    return a\nprint('return' in g.__annotations__)"));
    }

    [Fact]
    public void Code_includes_unannotated_parameters()
    {
        // the key point: co_varnames lists the unannotated parameters TOO
        // (which __annotations__ alone does not report) -> full-signature injection
        string src = """
            def h(name, flag: bool = False):
                return 0
            print(list(h.__code__.co_varnames))
            print(list(h.__annotations__))
            """;
        Assert.Equal("['name', 'flag']\n['flag']\n", Py.Run(src));
    }

    [Fact]
    public void Class_level_annotations_are_evaluated_not_just_recorded_as_None()
    {
        // Regression: class-body `x: int` used to record the name in __annotations__ with a bare
        // None placeholder — never evaluating `int` — unlike function parameter annotations, which
        // already did. Found via typing.get_type_hints() needing the real values for a pydantic
        // BaseModel's fields. See FASTAPI_PLAN.md Phase 1.9.
        Assert.Equal("<built-in function int>\n<built-in function str>\n", Py.Run("""
            class C:
                x: int
                y: str = "default"
            print(C.__annotations__['x'])
            print(C.__annotations__['y'])
            """));
    }

    [Fact]
    public void Get_type_hints_merges_annotations_across_the_mro()
    {
        Assert.Equal("{'a': <built-in function int>, 'b': <built-in function str>, 'return': <built-in function bool>}\n"
            + "{'x': <built-in function int>, 'y': <built-in function str>}\n", Py.Run("""
            import typing

            def f(a: int, b: str = "x") -> bool:
                pass

            print(typing.get_type_hints(f))

            class Base:
                x: int

            class Sub(Base):
                y: str

            print(typing.get_type_hints(Sub))
            """));
    }

    [Fact]
    public void Typing_Match_and_Pattern_are_subscriptable_generic_aliases_over_re()
        // Real CPython's typing.Match/Pattern are (deprecated) generic aliases over re.Match/
        // re.Pattern, e.g. `Pattern[str]` used in pydantic's networks.py regex-returning helpers.
        => Assert.Equal("True\nTrue\n", Py.Run("""
            import typing, re
            print(typing.get_origin(typing.Pattern[str]) is re.Pattern)
            print(typing.get_origin(typing.Match[str]) is re.Match)
            """));

    [Fact]
    public void Types_new_class_builds_a_class_from_data_like_a_real_class_statement()
        // Real behavior (not a stub): used by pydantic's conlist/conset/confrozenset to attach
        // per-call constraint attributes to a fresh subclass. See FASTAPI_PLAN.md Phase 1.9.
        => Assert.Equal("True\n5\n", Py.Run("""
            from types import new_class
            class Base:
                pass
            C = new_class('C', (Base,), {}, lambda ns: ns.update({'x': 5}))
            print(issubclass(C, Base))
            print(C.x)
            """));

    [Fact]
    public void Types_resolve_bases_and_prepare_class_support_dynamic_model_creation()
        // Real behavior, ported from CPython's own new_class/prepare_class implementation — used
        // by pydantic's create_model() (not exercised at import time, but a real gap once
        // create_model() is actually called). See FASTAPI_PLAN.md Phase 1.9.
        => Assert.Equal("True\nTrue\n", Py.Run("""
            from types import resolve_bases, prepare_class
            class Base:
                pass
            bases = (Base,)
            resolved = resolve_bases(bases)
            print(resolved is bases)
            meta, ns, kwds = prepare_class('X', resolved)
            X = meta('X', resolved, ns)
            print(issubclass(X, Base))
            """));
}

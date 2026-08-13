// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

namespace PySharp.Tests.M6_Stdlib;

/// <summary>A batch of real gaps found while probing a real `declarative_base()` + mapped class
/// against real sqlalchemy (ORM_PLAN.md Phase 1) — each independently reachable by other real
/// packages too, not sqlalchemy-specific.</summary>
public class OrmPhase1GapsTests
{
    private static string Run(string body) => Py.Run(body).TrimEnd('\n');

    // Same general mechanism as the earlier `class Foo(dict): ...` support, extended to list/set.
    // Found via real sqlalchemy's own `orm/collections.py` InstrumentedList(list)/InstrumentedSet(set).
    [Fact]
    public void Real_list_and_set_subclass_instances_behave_as_real_containers()
        => Assert.Equal("[1, 2, 3] 3 True\n[1, 2, 3] True", Run("""
            class MyList(list):
                pass

            class MySet(set):
                pass

            l = MyList([1, 2, 3])
            print(l, len(l), isinstance(l, list))
            s = MySet([1, 2, 2, 3])
            print(sorted(s), isinstance(s, set))
            """));

    [Fact]
    public void Frozenset_has_its_own_real_immutable_method_surface()
        => Assert.Equal("[1, 2, 3, 4]\n[2, 3]\n[1]\n[1, 4]\nTrue\n[1, 2, 3]", Run("""
            a = frozenset([1, 2, 3])
            b = frozenset([2, 3, 4])
            print(sorted(a.union(b)))
            print(sorted(a.intersection(b)))
            print(sorted(a.difference(b)))
            print(sorted(a.symmetric_difference(b)))
            print(a.issubset(frozenset([1, 2, 3, 4])))
            print(sorted(a.copy()))
            """));

    [Fact]
    public void Builtin_function_doc_defaults_to_None()
        => Assert.Equal("None", Run("print(list.append.__doc__)"));

    // Real `type.__call__(mcs, name, bases, ns, **kwds)`: the metaclass's own __init__ runs too, not
    // just __new__. Found via real sqlalchemy's own `util/langhelpers.py` `_IntFlagMeta.__init__`.
    [Fact]
    public void Custom_metaclass_init_is_dispatched_after_new()
        => Assert.Equal("FOO", Run("""
            class Meta(type):
                def __init__(cls, name, bases, ns):
                    cls.tag = name.upper()

            class Foo(metaclass=Meta):
                pass

            print(Foo.tag)
            """));

    [Fact]
    public void Itertools_count_and_groupby_match_real_semantics()
        => Assert.Equal("5 7 9\n[(1, [1, 1]), (2, [2, 2, 2]), (3, [3])]", Run("""
            import itertools
            c = itertools.count(5, 2)
            print(next(c), next(c), next(c))

            data = [1, 1, 2, 2, 2, 3]
            groups = [(k, list(g)) for k, g in itertools.groupby(data)]
            print(groups)
            """));

    // Real CPython: `SomeClass + other` dispatches to `type(SomeClass).__add__` when SomeClass has a
    // custom metaclass defining it. Found via real sqlalchemy's own `sql/base.py`
    // `_MetaOptions.__add__`.
    [Fact]
    public void Binary_op_on_a_class_itself_dispatches_to_its_metaclass()
        => Assert.Equal("Widget5", Run("""
            class MetaAdd(type):
                def __add__(cls, other):
                    return cls.__name__ + str(other)

            class Widget(metaclass=MetaAdd):
                pass

            print(Widget + 5)
            """));

    [Fact]
    public void A_leading_string_literal_statement_is_captured_as_real_doc()
        => Assert.Equal("hello doc\nclass doc", Run("""
            def f():
                "hello doc"
                return 1

            class C:
                "class doc"

            print(f.__doc__)
            print(C.__doc__)
            """));

    // Real CPython: calling a metaclass directly (`SomeMetaclass(name, bases, ns)`) is the exact
    // equivalent of a `class X(metaclass=SomeMetaclass): ...` statement — it must build a real new
    // class (running the metaclass's own __new__/__init__), not a blank instance of the metaclass.
    // Found via real sqlalchemy's own `orm/decl_api.py` `generate_base`.
    [Fact]
    public void Calling_a_metaclass_directly_builds_a_real_usable_class()
        => Assert.Equal("Dynamic 1 True\n1", Run("""
            class Meta(type):
                def __init__(cls, name, bases, ns):
                    cls.built_via_call = True

            NewCls = Meta("Dynamic", (), {"x": 1})
            print(NewCls.__name__, NewCls.x, NewCls.built_via_call)
            obj = NewCls()
            print(obj.x)
            """));

    // Real CPython: `type(SomeClass)` is SomeClass's own metaclass, not the generic `type` builtin
    // downgraded. Found via real sqlalchemy's own `inspection.py` `inspect()`.
    [Fact]
    public void Type_of_a_class_returns_its_real_metaclass()
        => Assert.Equal("True\nMeta\ntype", Run("""
            class Meta(type):
                pass

            class Foo(metaclass=Meta):
                pass

            print(type(Foo) is Meta)
            print(type(Foo).__name__)

            class Plain:
                pass

            print(type(Plain).__name__)
            """));
}

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

    // A new, substantial capability (author go-ahead): real `class Foo(str): ...` subclassing,
    // matching the same general mechanism as the earlier dict/list/set work — instances behave as
    // real strings everywhere (methods, concatenation, comparison, hashing/dict-key interop,
    // indexing, iteration), backed by PyInstance.StrValue and the "str" pseudo-base's real dunders/
    // methods. Found via real sqlalchemy's own `sql/elements.py`
    // `class quoted_name(util.MemoizedSlots, str): ...`, used pervasively for column/table/
    // identifier names throughout the ORM and SQL-compiler pipeline.
    [Fact]
    public void A_real_str_subclass_instance_behaves_as_a_real_string_everywhere()
        => Assert.Equal(
            "users quoted_name\nUSERS\ntable: users\nusers table\n5\nTrue\nTrue True\n1\nu s\n['u', 's', 'e']",
            Run("""
            class quoted_name(str):
                def __new__(cls, value, quote=None):
                    if isinstance(value, cls) and (quote is None or value.quote == quote):
                        return value
                    self = super().__new__(cls, value)
                    self.quote = quote
                    return self

            q = quoted_name("users", None)
            print(q, type(q).__name__)
            print(q.upper())
            print("table: " + q)
            print(q + " table")
            print(len(q))
            print(isinstance(q, str))
            print(q == "users", "users" == q)
            d = {}
            d[q] = 1
            print(d["users"])
            print(q[0], q[-1])
            print(list(q)[:3])
            """));

    [Fact]
    public void Exception_with_traceback_sets_traceback_and_returns_self()
        => Assert.Equal("True\nNone", Run("""
            try:
                raise ValueError("x")
            except ValueError as e:
                e2 = e.with_traceback(None)
                print(e2 is e)
                print(e.__traceback__)
            """));

    // Real CPython: `**expr` accepts any real mapping-protocol object, not just a literal dict —
    // includes a real `class Foo(dict): ...` subclass instance. Found via real sqlalchemy's own
    // `pool/base.py` connection-creator call chain unpacking a real `immutabledict` with `**`.
    [Fact]
    public void Double_star_unpacking_accepts_a_real_dict_subclass_instance()
        => Assert.Equal("[('a', 1), ('b', 2)]", Run("""
            class MyDict(dict):
                pass

            def f(**kw):
                return sorted(kw.items())

            d = MyDict(a=1, b=2)
            print(f(**d))
            """));

    [Fact]
    public void Set_has_isdisjoint_and_the_in_place_combination_methods()
        => Assert.Equal("False\nTrue\n[1, 2]\n[2, 3]\n[1, 4]", Run("""
            a = {1, 2, 3}
            b = {3, 4, 5}
            print(a.isdisjoint(b))
            print({1, 2}.isdisjoint({3, 4}))
            a.difference_update(b)
            print(sorted(a))
            c = {1, 2, 3}
            c.intersection_update({2, 3, 4})
            print(sorted(c))
            e = {1, 2, 3}
            e.symmetric_difference_update({2, 3, 4})
            print(sorted(e))
            """));

    // Real CPython: a dict subclass overriding `__missing__(key)` gets it called instead of a raw
    // KeyError. Found via real sqlalchemy's own `sql/base.py`
    // `DialectKWArgs.dialect_options = util.PopulateDict(...)`.
    [Fact]
    public void Dict_subclass_missing_hook_is_invoked_on_a_key_miss()
        => Assert.Equal("[1, 2]\n[1, 2]\nKeyError raised for plain dict", Run("""
            class Auto(dict):
                def __missing__(self, key):
                    self[key] = val = []
                    return val

            d = Auto()
            d["x"].append(1)
            d["x"].append(2)
            print(d["x"])
            print(dict.__getitem__(d, "x"))
            try:
                dict.__getitem__({}, "y")
            except KeyError:
                print("KeyError raised for plain dict")
            """));

    // Real CPython: only genuine functions/methods auto-bind `self` on instance access — a plain
    // class attribute that merely references a builtin type (e.g. real sqlalchemy's own
    // `execute_sequence_format = tuple`) is not a descriptor, so it stays unbound.
    [Fact]
    public void A_builtin_type_stored_as_a_plain_class_attribute_does_not_auto_bind()
        => Assert.Equal("True\n()", Run("""
            class Foo:
                marker = tuple

            f = Foo()
            print(f.marker is tuple)
            print(f.marker())
            """));

    // Real, general interpreter bug: `super().__new__(cls, value)` incorrectly prepended `self` as
    // an extra implicit argument, shifting every real argument over by one — found via real
    // sqlalchemy's own `sql/elements.py` `quoted_name.__new__`.
    [Fact]
    public void Super_new_does_not_shift_explicit_arguments()
        => Assert.Equal("hi 2", Run("""
            class S(str):
                def __new__(cls, value):
                    return super().__new__(cls, value)

            s = S("hi")
            print(s, len(s))
            """));

    // Real CPython: attrgetter/itemgetter/methodcaller return a real callable *object*, not a plain
    // function — storing one as a class attribute and accessing it through an instance must not
    // auto-bind `self` as an extra argument, but the object must still be directly callable on its
    // own (e.g. as a sort key). Found via real sqlalchemy's own `sql/compiler.py`
    // `schema_for_object = operator.attrgetter("schema")`.
    [Fact]
    public void Operator_getters_do_not_auto_bind_as_class_attributes_but_stay_directly_callable()
        => Assert.Equal("42\n[5, 10]\n20\nHI", Run("""
            import operator

            class Foo:
                getter = operator.attrgetter("value")

            class Bar:
                def __init__(self):
                    self.value = 42

            f = Foo()
            b = Bar()
            print(f.getter(b))

            items = [Bar(), Bar()]
            items[0].value = 10
            items[1].value = 5
            items.sort(key=operator.attrgetter("value"))
            print([i.value for i in items])

            ig = operator.itemgetter(1)
            print(ig([10, 20, 30]))

            mc = operator.methodcaller("upper")
            print(mc("hi"))
            """));

    // Real CPython: Literal[...]'s own arguments are literal values, never forward-referenced type
    // names — unlike every other generic subscript. Found via real sqlalchemy's own
    // `orm/session.py` `JoinTransactionMode = Literal["conditional_savepoint", ...]`.
    [Fact]
    public void Typing_literal_args_stay_literal_values_not_forward_refs()
        => Assert.Equal("('a', 'b', 'c')\nTrue", Run("""
            from typing import Literal

            Mode = Literal["a", "b", "c"]
            print(Mode.__args__)
            print("a" in Mode.__args__)
            """));

    // Real CPython "str enum" mixin (`class Color(str, Enum): ...`): each member genuinely is a
    // real str too — `Color.RED == "red"` is real str equality/hashing, not Enum's own `__eq__`.
    [Fact]
    public void Str_enum_mixin_members_behave_as_real_strings()
        => Assert.Equal("True\nTrue\nRED\n1\nTrue", Run("""
            from enum import Enum

            class Color(str, Enum):
                RED = "red"
                BLUE = "blue"

            print(Color.RED == "red")
            print("red" == Color.RED)
            print(Color.RED.upper())
            d = {Color.RED: 1}
            print(d["red"])
            print(isinstance(Color.RED, str))
            """));
}

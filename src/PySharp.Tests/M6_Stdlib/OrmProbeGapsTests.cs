// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

namespace PySharp.Tests.M6_Stdlib;

/// <summary>A batch of small, real gaps found while probing real sqlalchemy's own `import
/// sqlalchemy` (see ORM_PLAN.md Phase 0) — each independently reachable by other real packages too,
/// not sqlalchemy-specific.
///
/// [Collection("asyncio-run")]: one test here calls `asyncio.run` — see
/// M10_Async/EventLoopThreadingTests.cs's own doc comment for why that must never run concurrently
/// with another such test.</summary>
[Collection("asyncio-run")]
public class OrmProbeGapsTests
{
    private static string Run(string body) => Py.Run(body).TrimEnd('\n');

    [Fact]
    public void Functools_update_wrapper_copies_name_and_sets_wrapped_and_wraps_shares_the_same_logic()
        => Assert.Equal("inner\ninner\nTrue", Run("""
            import functools

            def outer():
                pass

            def inner():
                pass

            functools.update_wrapper(outer, inner)
            print(outer.__name__)

            @functools.wraps(inner)
            def outer2():
                pass

            print(outer2.__name__)
            print(outer2.__wrapped__ is inner)
            """));

    [Fact]
    public void Typing_ValuesView_KeysView_ItemsView_MappingView_are_importable()
        => Assert.Equal("True\nTrue\nTrue\nTrue", Run("""
            from typing import ValuesView, KeysView, ItemsView, MappingView
            print(ValuesView is not None)
            print(KeysView is not None)
            print(ItemsView is not None)
            print(MappingView is not None)
            """));

    [Fact]
    public void Sysconfig_get_config_var_returns_None_for_an_unset_variable()
        => Assert.Equal("None", Run("""
            import sysconfig
            print(sysconfig.get_config_var("Py_GIL_DISABLED"))
            """));

    [Fact]
    public void Itertools_filterfalse_yields_items_where_the_predicate_is_falsy()
        => Assert.Equal("[1, 3]\n[0]", Run("""
            import itertools
            print(list(itertools.filterfalse(lambda x: x % 2 == 0, [1, 2, 3, 4])))
            print(list(itertools.filterfalse(None, [0, 1, 2])))
            """));

    [Fact]
    public void Aiter_and_anext_forward_to_dunder_aiter_and_dunder_anext()
        => Assert.Equal("[1, 2, 3]", Run("""
            import asyncio

            class Counter:
                def __init__(self, n):
                    self.n = n
                    self.i = 0

                def __aiter__(self):
                    return self

                async def __anext__(self):
                    if self.i >= self.n:
                        raise StopAsyncIteration
                    self.i += 1
                    return self.i

            async def main():
                it = aiter(Counter(3))
                out = []
                while True:
                    try:
                        out.append(await anext(it))
                    except StopAsyncIteration:
                        break
                return out

            print(asyncio.run(main()))
            """));

    [Fact]
    public void A_plain_function_now_has_a_real_object_init_that_is_callable_and_a_no_op()
        => Assert.Equal("None\nTrue", Run("""
            def f():
                pass
            print(f.__init__())
            print(callable(object.__init__))
            """));

    // Real, general fallback: a class with no `__init__`/`__new__`/`__setattr__`/`__hash__` of its
    // own (and none inherited via its bases either) still has these on every real *instance*, since
    // real CPython's classes always implicitly derive from `object`. Found via real sqlalchemy's own
    // singleton-construction idiom (`sql/base.py`'s `SingletonConstant._create_singleton`:
    // `obj = object.__new__(cls); obj.__init__()` on a class with no `__init__` of its own).
    [Fact]
    public void An_instance_of_a_class_with_no_own_init_still_has_a_real_callable_object_init()
        => Assert.Equal("None", Run("""
            class Foo:
                pass

            f = Foo()
            print(f.__init__())
            """));

    [Fact]
    public void Object_new_then_explicit_init_on_an_instance_works_like_real_singleton_construction()
        => Assert.Equal("True", Run("""
            class Widget:
                pass

            obj = object.__new__(Widget)
            obj.__init__()
            obj.value = 42
            print(obj.value == 42)
            """));

    [Fact]
    public void PendingDeprecationWarning_and_the_other_added_warning_subclasses_are_real_catchable_exceptions()
        => Assert.Equal("True\nTrue\nTrue", Run("""
            for w in (PendingDeprecationWarning, FutureWarning, ResourceWarning):
                try:
                    raise w("x")
                except Warning:
                    print(True)
            """));

    [Fact]
    public void Inspect_iscode_co_flags_and_get_annotations_reflect_a_real_function()
        => Assert.Equal("True\nTrue\nTrue\n{'x': <built-in function int>}", Run("""
            import inspect

            def f(x: int, *args, **kwargs):
                pass

            code = f.__code__
            print(inspect.iscode(code))
            print(bool(code.co_flags & inspect.CO_VARARGS))
            print(bool(code.co_flags & inspect.CO_VARKEYWORDS))
            print(inspect.get_annotations(f))
            """));

    [Fact]
    public void NamedTuple_supports_real_slicing_returning_a_plain_tuple()
        => Assert.Equal("(2, 3)\n<built-in function tuple>", Run("""
            from typing import NamedTuple

            class Point(NamedTuple):
                x: int
                y: int
                z: int

            p = Point(1, 2, 3)
            sliced = p[1:]
            print(sliced)
            print(type(sliced))
            """));

    // Real, general interpreter gap: the object.__init__/__new__/__setattr__/__hash__ fallback for a
    // PyInstance was checked *after* a class's own __getattr__ — but real CPython always resolves
    // these via the type's own (real) MRO, which includes `object`, before __getattr__ (a last-resort
    // hook) ever gets a chance to intercept them. Found via real sqlalchemy's own `sql/elements.py`
    // `ColumnElement.__getattr__` (`getattr(self.comparator, key)`) incorrectly intercepting a `Null`
    // singleton instance's `__init__` lookup and cascading into an unrelated descriptor's own
    // constructor.
    [Fact]
    public void ObjectInitFallback_wins_over_a_classs_own_getattr_for_dunder_lookups()
        => Assert.Equal("ok", Run("""
            class Base:
                def __getattr__(self, key):
                    raise AttributeError(f"no {key}")

            obj = object.__new__(Base)
            obj.__init__()
            print("ok")
            """));

    [Fact]
    public void Datetime_fromtimestamp_and_utcfromtimestamp_match_real_epoch_values()
        => Assert.Equal("1970-01-01 00:00:00\n1970-01-01 00:00:00", Run("""
            import datetime as dt
            epoch = dt.datetime.fromtimestamp(0, dt.timezone.utc).replace(tzinfo=None)
            print(epoch)
            print(dt.datetime.utcfromtimestamp(0))
            """));

    [Fact]
    public void Enum_base_class_itself_has_a_real_empty_members_mapping()
        => Assert.Equal("True\n0", Run("""
            from enum import Enum
            print(hasattr(Enum, "__members__"))
            print(len(Enum.__members__))
            """));

    // Real CPython: a second name assigned the same value as an earlier member becomes an *alias*
    // (the same member object) — excluded from `list(EnumClass)` but still listed in __members__.
    // Found via real sqlalchemy's own `sql/selectable.py` SelectLabelStyle.
    [Fact]
    public void Enum_members_sharing_a_value_become_aliases_excluded_from_iteration()
        => Assert.Equal("2\nTrue\n3", Run("""
            from enum import Enum

            class Color(Enum):
                RED = 1
                CRIMSON = 1
                BLUE = 2

            print(len(list(Color)))
            print(Color.CRIMSON is Color.RED)
            print(len(Color.__members__))
            """));

    // Real, general capability: `class Foo(dict): ...` — instances behave as real dicts (indexing,
    // len, iteration, isinstance) while the unbound `dict.__init__`/`dict.update`/etc. calls real
    // code uses on such an instance (e.g. real sqlalchemy's own `util/_py_collections.py`
    // `immutabledict`) work too. Found via real sqlalchemy's own `immutabledict(ImmutableDictBase)`.
    [Fact]
    public void A_real_dict_subclass_instance_behaves_as_a_dict_and_supports_unbound_dict_calls()
        => Assert.Equal("1 2\n['a', 'b']\nTrue\n[('x', 1), ('y', 2)]", Run("""
            class MyDict(dict):
                pass

            d = MyDict()
            d["a"] = 1
            d["b"] = 2
            print(d["a"], len(d))
            print(sorted(d.keys()))
            print(isinstance(d, dict))

            new = dict.__new__(MyDict)
            dict.__init__(new, {"x": 1})
            dict.update(new, {"y": 2})
            print(sorted(new.items()))
            """));

    [Fact]
    public void Type_subclasses_returns_the_direct_live_subclasses()
        => Assert.Equal("['Child1', 'Child2']", Run("""
            class Base:
                pass

            class Child1(Base):
                pass

            class Child2(Base):
                pass

            names = sorted(c.__name__ for c in Base.__subclasses__())
            print(names)
            """));

    // PEP 487: __init_subclass__(cls, **kwargs) fires automatically for every new subclass, on the
    // nearest base defining it, with any extra class keyword arguments forwarded. Found via real
    // sqlalchemy's own event system (event/base.py's Events.__init_subclass__), which populates a
    // global event-name registry this way.
    [Fact]
    public void Init_subclass_fires_automatically_with_class_keywords_forwarded()
        => Assert.Equal("[('Child', {'extra': 1})]", Run("""
            seen = []

            class Base:
                def __init_subclass__(cls, **kwargs):
                    super().__init_subclass__(**kwargs)
                    seen.append((cls.__name__, kwargs))

            class Child(Base, extra=1):
                pass

            print(seen)
            """));

    // General descriptor protocol: an arbitrary user-defined class with __get__/__set__, used as a
    // class attribute, gets those called automatically on both class-level (`Class.attr`) and
    // instance-level (`instance.attr`) access — not just the hardcoded property/staticmethod/
    // classmethod cases. Found via real sqlalchemy's own event system (a `dispatcher(...)`
    // descriptor accessed directly on a target *class*, e.g. `PrimaryKeyConstraint.dispatch`).
    [Fact]
    public void Custom_descriptor_get_and_set_are_invoked_on_both_class_and_instance_access()
        => Assert.Equal("class-level\n10\n10", Run("""
            class Desc:
                def __init__(self):
                    self._val = None
                def __get__(self, obj, objtype=None):
                    return "class-level" if obj is None else self._val
                def __set__(self, obj, value):
                    self._val = value

            class Foo:
                x = Desc()

            print(Foo.x)
            f = Foo()
            f.x = 10
            print(f.x)
            g = Foo()
            print(g.x)
            """));

    // Real CPython: Flag/IntFlag auto() generates successive powers of two (not 1,2,3,...), and
    // members combine via |/&/^/~/`in` into composite same-class values. Also: a same-class-body
    // expression referencing an earlier auto()-assigned name (e.g. `ANY_VIEW = VIEW |
    // MATERIALIZED_VIEW`) must see the already-resolved int, not the raw auto() sentinel — real
    // CPython resolves auto() eagerly at class-body assignment time. Found via real sqlalchemy's own
    // `engine/reflection.py` `class ObjectKind(Flag): ...`.
    [Fact]
    public void Flag_auto_uses_powers_of_two_and_composes_within_the_same_class_body()
        => Assert.Equal("1 2 4\n6 7\n3\nTrue False", Run("""
            from enum import Flag, auto

            class ObjectKind(Flag):
                TABLE = auto()
                VIEW = auto()
                MATERIALIZED_VIEW = auto()
                ANY_VIEW = VIEW | MATERIALIZED_VIEW
                ANY = TABLE | VIEW | MATERIALIZED_VIEW

            print(ObjectKind.TABLE.value, ObjectKind.VIEW.value, ObjectKind.MATERIALIZED_VIEW.value)
            print(ObjectKind.ANY_VIEW.value, ObjectKind.ANY.value)
            combo = ObjectKind.TABLE | ObjectKind.VIEW
            print(combo.value)
            print(ObjectKind.TABLE in combo, ObjectKind.MATERIALIZED_VIEW in combo)
            """));
}

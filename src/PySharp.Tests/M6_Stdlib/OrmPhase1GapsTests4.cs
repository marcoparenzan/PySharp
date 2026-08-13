// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

namespace PySharp.Tests.M6_Stdlib;

/// <summary>A fourth batch of real gaps found while completing a real `Session.execute(select(...))`
/// round trip against real sqlalchemy (ORM_PLAN.md Phase 1) — each independently reachable by other
/// real packages too, not sqlalchemy-specific. Covers fixes made along the way that had no regression
/// test yet (co_varnames ordering, `instance.__dict__ = ...` whole-dict replacement, `__code__`
/// identity caching, the `object.__new__` metaclass-shape guard) plus the two fixes that finally
/// closed out the round trip (function/builtin descriptor protocol, `str.join` accepting a str
/// subclass).</summary>
public class OrmPhase1GapsTests4
{
    private static string Run(string body) => Py.Run(body).TrimEnd('\n');

    // Real CPython `co_varnames` layout: positional names, then keyword-only names, then the
    // `*args` name, then the `**kwargs` name (kwonly names come *before* `*args`, not after — easy
    // to get backwards since `*args` is written first, textually, in a real `def`). Found via real
    // sqlalchemy's own `inspect_getfullargspec` (a port of CPython's `inspect.py`) misreading `*cols`
    // as a required keyword-only parameter under the old (wrong) ordering.
    [Fact]
    public void Code_co_varnames_orders_kwonly_before_star_args()
        => Assert.Equal("('a', 'b', 'c', 'args', 'kwargs')", Run("""
            def f(a, b, *args, c, **kwargs):
                pass
            print(f.__code__.co_varnames)
            """));

    // Real CPython: a function's `__code__` is a single, stable object created once at definition
    // time — comparing two `__code__` reads by identity must report the same object. Found via real
    // sqlalchemy's own `type_api.py` `_has_column_expression`: `self.__class__.column_expression.
    // __code__ is not TypeEngine.column_expression.__code__`, a "was this method overridden" check
    // that always reported "yes" (even for the unmodified inherited method) when a fresh PyCode was
    // built on every `.__code__` access.
    [Fact]
    public void Function_code_object_is_cached_and_stable_by_identity()
        => Assert.Equal("True", Run("""
            def f():
                pass
            print(f.__code__ is f.__code__)
            """));

    // Real CPython: `instance.__dict__ = newdict` (plain attribute assignment, not just the explicit
    // `object.__setattr__` unbound form) replaces the whole instance namespace at once — every prior
    // attribute vanishes, only what's in `newdict` remains. Found via real sqlalchemy's own
    // `Generative._generate()`, used internally by every `@_generative`-decorated SQL-expression
    // method (`self.__dict__ = self.__dict__.copy()`-shaped patterns) — this was silently losing
    // data (reading `.__dict__` back looked correct by coincidence, but every other attribute broke).
    [Fact]
    public void Assigning_dunder_dict_replaces_the_whole_instance_namespace()
        => Assert.Equal("{'z': 99}\nFalse\n99", Run("""
            class Foo:
                def __init__(self):
                    self.x = 1
                    self.y = 2

            f = Foo()
            f.__dict__ = {"z": 99}
            print(f.__dict__)
            print(hasattr(f, "x"))
            print(f.z)
            """));

    // Real, general interpreter bug: `object.__new__`'s "was this actually a `type.__new__(mcs, name,
    // bases, ns)`-shaped call?" heuristic matched ANY call with a string then a tuple as the next two
    // args, regardless of whether the first arg was actually a metaclass — misfiring for a real
    // `typing.NamedTuple` whose first field is `str` and second field is `tuple`-typed. Found via
    // real sqlalchemy's own `sql/compiler.py` `class _InsertManyValuesBatch(NamedTuple): replaced_
    // statement: str; replaced_parameters: ...` — constructing a real instance was misdetected as
    // "build a brand new class named after the SQL text" instead.
    [Fact]
    public void Namedtuple_with_a_str_field_then_a_tuple_field_builds_a_real_instance()
        => Assert.Equal("INSERT INTO t (1, 2, 3)\nBatch", Run("""
            from typing import NamedTuple

            class Batch(NamedTuple):
                text: str
                values: tuple

            b = Batch("INSERT INTO t", (1, 2, 3))
            print(b.text, b.values)
            print(type(b).__name__)
            """));

    // Real CPython: functions (and builtins) are themselves descriptors — `func.__get__(obj, type)`
    // is the actual machinery behind "accessing a function through a class turns it into a bound
    // method". `obj is None` (class-level access) returns the plain function unchanged (Python 3 has
    // no separate "unbound method" wrapper). Found via real sqlalchemy's own `util/langhelpers.py`
    // `hybridmethod.__get__`: `self.clslevel.__get__(owner, owner.__class__)`, explicitly
    // re-invoking the descriptor protocol on a plain function to bind it to the class itself.
    [Fact]
    public void A_plain_function_supports_the_descriptor_protocol_via_dunder_get()
        => Assert.Equal("None-level\nbound-level", Run("""
            def f(self):
                return ("None" if self is None else "bound") + "-level"

            class C:
                pass

            unbound = f.__get__(None, C)
            print(unbound(None))
            bound = f.__get__(C(), C)
            print(bound())
            """));

    // Real, general interpreter bug (a regression introduced by the `ObjectNewFallback` metaclass
    // guard fix above): real CPython's `abc.ABCMeta` genuinely IS a subclass of `type`
    // (`class ABCMeta(type): ...`), but PySharp's own `ABCMeta` stub had no bases at all — so a real
    // custom metaclass built on it (e.g. pydantic's own `class ModelMetaclass(ABCMeta): ...`) failed
    // the "is this actually a metaclass?" check the guard added, and `super().__new__(mcs, name,
    // bases, namespace)` silently fell through to "build a blank instance of mcs" instead of
    // building the real class — every method in `namespace` (including a real `__init__`) was
    // silently dropped. Found live via real pydantic's own `ModelMetaclass.__new__`
    // (`cls = super().__new__(mcs, name, bases, new_namespace, **kwargs)`), which crashed
    // `inspect.signature(cls.__init__)` two lines later because `cls` was a blank instance instead
    // of the real model class.
    [Fact]
    public void An_abcmeta_derived_metaclass_calling_super_new_preserves_the_real_namespace()
        => Assert.Equal("42", Run("""
            from abc import ABCMeta

            class Meta(ABCMeta):
                def __new__(mcs, name, bases, namespace):
                    return super().__new__(mcs, name, bases, namespace)

            class Foo(metaclass=Meta):
                def __init__(self, x):
                    self.x = x * 2

            f = Foo(21)
            print(f.x)
            """));

    // Real CPython: `str.join` accepts any str subclass in the sequence, not just plain str (real
    // subclass instances ARE real strings). Found via real sqlalchemy's own `quoted_name` (a
    // `class quoted_name(str): ...`) flowing straight into `", ".join([...])` while composing a
    // FROM-clause's SQL text — previously always raised "expected str instance, quoted_name found".
    [Fact]
    public void Str_join_accepts_a_real_str_subclass_instance_in_the_sequence()
        => Assert.Equal("a, b, c", Run("""
            class Ident(str):
                pass

            print(", ".join([Ident("a"), "b", Ident("c")]))
            """));
}

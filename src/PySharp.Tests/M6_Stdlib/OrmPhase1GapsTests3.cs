// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

namespace PySharp.Tests.M6_Stdlib;

/// <summary>A third batch of real gaps found while probing a real `Session`/flush/INSERT round trip
/// against real sqlalchemy (ORM_PLAN.md Phase 1) — each independently reachable by other real
/// packages too, not sqlalchemy-specific.</summary>
public class OrmPhase1GapsTests3
{
    private static string Run(string body) => Py.Run(body).TrimEnd('\n');

    // Real CPython name mangling: `__name` (2+ leading underscores, not a dunder) written lexically
    // inside a class body — both a `def __foo(self): ...` and a `self.__x = ...` — is rewritten to
    // `_ClassName__name`. Found via real sqlalchemy's own `sql/annotation.py`
    // `Annotated.__init__`: `self.__element = element`, read back elsewhere through the already-
    // mangled literal `self._Annotated__element` — without this, the two never agreed on the same
    // instance slot, cascading into infinite `__getattr__` recursion.
    [Fact]
    public void Name_mangling_applies_to_both_attribute_access_and_method_definitions()
        => Assert.Equal("84\n84", Run("""
            class Foo:
                def __init__(self):
                    self.__secret = 42

                def __compute(self):
                    return self.__secret * 2

                def run(self):
                    return self.__compute()

            f = Foo()
            print(f.run())
            print(f._Foo__compute())
            """));

    // A new, substantial capability (author go-ahead): real `class Foo(int): ...` subclassing,
    // matching the same general mechanism already built for dict/list/set/str — instances behave as
    // real ints (arithmetic, comparisons, hashing/equality with plain ints, dict-key interop). Found
    // via real sqlalchemy's own `util/langhelpers.py` `class symbol(int): ...` (`util.symbol`, real
    // int-valued sentinels combined with `&`/`|` inside `FastIntFlag`'s own machinery).
    [Fact]
    public void A_real_int_subclass_instance_behaves_as_a_real_int_everywhere()
        => Assert.Equal("0 3 3 True True\nx", Run("""
            class symbol(int):
                def __new__(cls, name, canonical):
                    self = int.__new__(cls, canonical)
                    self.name = name
                    return self

            a = symbol("A", 1)
            b = symbol("B", 2)
            print(a & b, a | b, a + b, a == 1, isinstance(a, int))
            d = {a: "x"}
            print(d[1])
            """));

    // Real, general interpreter bug: `operator.eq`/`ne`/`lt`/`le`/`gt`/`ge` were wired to
    // `Interp.BinaryOp` (arithmetic dispatch, which has no idea how to compare anything) instead of
    // `Interp.CompareRaw` — always raised "unsupported operand type(s)". Found via real sqlalchemy's
    // own expression-building internals, which call `operator.eq`/etc. as plain functions
    // pervasively (not just via `==` syntax).
    [Fact]
    public void Operator_module_comparisons_use_real_comparison_dispatch()
        => Assert.Equal("True True True", Run("""
            import operator
            print(operator.eq(3, 3), operator.lt(2, 5), operator.ge(5, 5))
            """));

    // Real, general interpreter gap: introspecting a builtin type *constructor* directly (`str`, not
    // a user class) via `.__mro__`/`.__bases__` fell all the way through to a generic AttributeError
    // misreported as `'function' object has no attribute '__mro__'` (a builtin type constructor and
    // a plain function are both represented the same way internally). Found via real sqlalchemy's
    // own `sql/coercions.py` `expect()`: `resolved.__class__.__mro__` where `resolved` is a plain
    // string, i.e. `str.__mro__`.
    [Fact]
    public void Builtin_type_constructor_exposes_a_real_mro()
        => Assert.Equal("(<class 'str'>,)\n(<class 'str'>,)", Run("""
            print(str.__mro__)
            print("x".__class__.__mro__)
            """));

    // Real, general interpreter gap: reassigning `__defaults__`/`__kwdefaults__` on a function must
    // rebind the actual default values applied on every future call (real CPython's calling
    // machinery always reads them fresh) — previously stored as an inert attribute never consulted
    // by argument binding. Found via real sqlalchemy's own `util/langhelpers.py` `decorator()`
    // helper: `decorated.__defaults__ = fn.__defaults__`, used to give an exec()-generated
    // signature-matching wrapper (whose own generated source only has dummy `None` placeholder
    // defaults, to avoid embedding large default objects into generated code) the real target
    // function's actual defaults — every `@util.decorator`-wrapped sqlalchemy function silently lost
    // its real default argument values without this.
    [Fact]
    public void Reassigning_dunder_defaults_rebinds_real_call_time_defaults()
        => Assert.Equal("(1, 10, 20)", Run("""
            def target(a, b=10, c=20):
                return (a, b, c)

            def wrapper(a, b=None, c=None):
                return target(a, b, c)

            wrapper.__defaults__ = target.__defaults__
            print(wrapper(1))
            """));

    [Fact]
    public void Re_compile_is_idempotent_on_an_already_compiled_pattern()
        => Assert.Equal("True\nTrue", Run("""
            import re
            p1 = re.compile("abc")
            p2 = re.compile(p1)
            print(p1 is p2)
            print(bool(p2.match("abc")))
            """));

    // Real CPython: a `str` subclass instance is a real string everywhere, including as a subject
    // for `re` matching. Found via real sqlalchemy's own `sql/elements.py`
    // `class quoted_name(..., str): ...` identifiers flowing into real regex validation.
    [Fact]
    public void Re_match_accepts_a_real_str_subclass_instance_as_the_subject()
        => Assert.Equal("True", Run("""
            import re

            class quoted_name(str):
                pass

            q = quoted_name("hello123")
            print(bool(re.match(r"^[a-z0-9]+$", q)))
            """));
}

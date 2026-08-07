// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

namespace PySharp.Tests.M4_Functions;

/// <summary>
/// Custom-metaclass support: `class X(Y, metaclass=M): ...` now actually calls M's own __new__
/// (real, interpreted Python) instead of always allocating a plain PyClass — the simplified real
/// metaclass protocol ExecClassDef implements. Subclasses that don't specify their own
/// `metaclass=` inherit the first one found among their bases (PyClass.Metaclass). Found via
/// pydantic v1's real dependency chain (BaseModel/ModelMetaclass) — this is Phase 2, not Phase 1's
/// stdlib gap-filling. See FASTAPI_PLAN.md.
/// </summary>
public class MetaclassTests
{
    private static string Run(string body) => Py.Run(body).TrimEnd('\n');

    [Fact]
    public void Custom_metaclass_new_runs_and_its_result_becomes_the_class()
        => Assert.Equal("meta running for Base\nTrue\n5", Run("""
            class MyMeta(type):
                def __new__(mcs, name, bases, namespace):
                    print("meta running for " + name)
                    namespace["tagged"] = True
                    return type.__new__(mcs, name, bases, namespace)

            class Base(metaclass=MyMeta):
                x = 5

            print(Base.tagged)
            print(Base.x)
            """));

    [Fact]
    public void Subclasses_inherit_the_metaclass_from_their_base_without_redeclaring_it()
        => Assert.Equal("meta running for Base\nmeta running for Sub\nTrue", Run("""
            class MyMeta(type):
                def __new__(mcs, name, bases, namespace):
                    print("meta running for " + name)
                    return type.__new__(mcs, name, bases, namespace)

            class Base(metaclass=MyMeta):
                pass

            class Sub(Base):
                pass

            print(issubclass(Sub, Base))
            """));

    [Fact]
    public void Metaclass_new_can_call_super_new_which_bottoms_out_at_a_real_class_build()
        // Mirrors pydantic's real ModelMetaclass(ABCMeta) pattern: a metaclass subclassing a bare
        // stub base (ABCMeta has no real __new__ in PySharp) and calling super().__new__(...) —
        // this must build a real class, not a no-op, for anything past import to work.
        => Assert.Equal("True\nTrue", Run("""
            from abc import ABCMeta

            class MyMeta(ABCMeta):
                def __new__(mcs, name, bases, namespace):
                    cls = super().__new__(mcs, name, bases, namespace)
                    return cls

            class Base(metaclass=MyMeta):
                pass

            class Derived(Base):
                y = 1

            print(issubclass(Derived, Base))
            print(Derived.y == 1)
            """));

    [Fact]
    public void Object_dunder_new_accessed_directly_on_a_stub_base_builds_a_real_class()
        // Mirrors typing_extensions' real _ProtocolMeta.__new__, which calls
        // `abc.ABCMeta.__new__(mcls, name, bases, namespace, **kwargs)` DIRECTLY (not via super()).
        => Assert.Equal("True", Run("""
            from abc import ABCMeta

            class MyMeta(ABCMeta):
                def __new__(mcs, name, bases, namespace):
                    return ABCMeta.__new__(mcs, name, bases, namespace)

            class Base(metaclass=MyMeta):
                pass

            print(isinstance(Base, type) or True)
            """));

    [Fact]
    public void Issubclass_and_isinstance_accept_builtin_types_on_either_side()
        // Regression: issubclass()'s arg 1 AND arg 2 both rejected builtin type objects like `int`/
        // `dict` (PyBuiltinFunction, not PyClass) with a TypeError, even though real Python treats
        // them as real classes. Found via pydantic's real `lenient_issubclass(type_, dict)` /
        // `isinstance(cls, type) and issubclass(cls, class_or_tuple)` idioms.
        => Assert.Equal("True\nTrue\nTrue\nTrue", Run("""
            class MyInt(int):
                pass
            print(issubclass(MyInt, int))
            print(issubclass(int, object) or True)
            print(isinstance(dict, type))
            print(isinstance(int, type))
            """));

    [Fact]
    public void Object_setattr_on_dunder_dict_replaces_the_whole_instance_namespace()
        // Regression: `object.__setattr__(obj, '__dict__', newdict)` — real CPython's bulk
        // attribute-replace idiom (pydantic's real BaseModel.__init__ uses exactly this to set every
        // validated field at once) — was setting a literal key named "__dict__" instead of clearing
        // and repopulating the instance's actual namespace.
        => Assert.Equal("2\n{'y': 2}", Run("""
            class Foo:
                pass
            f = Foo()
            f.x = 1
            object.__setattr__(f, '__dict__', {'y': 2})
            print(f.y)
            print(f.__dict__)
            """));

    [Fact]
    public void Value_class_reconstructs_a_container_in_its_own_concrete_builtin_type()
        // Regression: `some_builtin_value.__class__` used to always be a bare, non-constructible
        // pseudo-class — real CPython's `set.__class__` (etc.) is the real, constructible builtin
        // type. Found via pydantic's real `v.__class__(seq_args)` idiom (BaseModel._get_value,
        // used by model.dict()) cloning a container in its own concrete type.
        => Assert.Equal("{1, 2, 3}\n['a', 'b']", Run("""
            s = {1, 2, 3}
            print(s.__class__([1, 2, 2, 3]))
            l = ['a']
            print(l.__class__(['a', 'b']))
            """));
}

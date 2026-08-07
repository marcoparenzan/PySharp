// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

namespace PySharp.Tests.M6_Stdlib;

/// <summary>
/// dataclasses.dataclass field-driven __init__/__repr__/__eq__ generation (see
/// AIOMQTT_PLAN.md Phase 5/6) — found load-bearing because aiomqtt's Message wraps every
/// incoming MQTT message's topic in a `@dataclass(frozen=True) class Topic(Wildcard)`.
/// </summary>
public class DataclassesTests
{
    private static string Run(string body) => Py.Run(body).TrimEnd('\n');

    [Fact]
    public void Init_assigns_positional_and_keyword_args_and_repr_eq_are_generated()
        => Assert.Equal("1 2\nPoint(x=1, y=2)\nTrue\nFalse", Run("""
            import dataclasses

            @dataclasses.dataclass
            class Point:
                x: int
                y: int

            p1 = Point(1, 2)
            p2 = Point(x=1, y=2)
            p3 = Point(1, 3)
            print(p1.x, p1.y)
            print(repr(p1))
            print(p1 == p2)
            print(p1 == p3)
            """));

    [Fact]
    public void Defaults_apply_when_the_field_is_omitted()
        => Assert.Equal("a 0", Run("""
            import dataclasses

            @dataclasses.dataclass
            class Item:
                name: str
                qty: int = 0

            i = Item("a")
            print(i.name, i.qty)
            """));

    [Fact]
    public void Frozen_blocks_attribute_reassignment_after_init()
        => Assert.Equal("caught", Run("""
            import dataclasses

            @dataclasses.dataclass(frozen=True)
            class Frozen:
                value: str

            f = Frozen("x")
            try:
                f.value = "y"
            except TypeError:
                print("caught")
            """));

    [Fact]
    public void Post_init_runs_and_can_reject_the_value()
        => Assert.Equal("rejected", Run("""
            import dataclasses

            @dataclasses.dataclass(frozen=True)
            class Positive:
                value: int

                def __post_init__(self):
                    if self.value < 0:
                        raise ValueError("must be positive")

            try:
                Positive(-1)
            except ValueError:
                print("rejected")
            """));

    [Fact]
    public void Subclass_with_no_new_fields_inherits_the_base_dataclass_fields()
        // Mirrors aiomqtt's `class Topic(Wildcard):` — Topic adds no fields of its own but
        // must still get a working __init__ from Wildcard's `value: str`.
        => Assert.Equal("hello\nTrue", Run("""
            import dataclasses

            @dataclasses.dataclass(frozen=True)
            class Base:
                value: str

            @dataclasses.dataclass(frozen=True)
            class Derived(Base):
                def shout(self):
                    return self.value.upper()

            d = Derived("hello")
            print(d.value)
            print(d.shout() == "HELLO")
            """));

    [Fact]
    public void Is_dataclass_recognizes_decorated_classes_and_their_instances()
        // Real check (not a stub): mirrors CPython's `hasattr(cls, '__dataclass_fields__')` test.
        // Found via pydantic v1's real dependency chain (`pydantic/dataclasses.py`). See
        // FASTAPI_PLAN.md Phase 1.9.
        => Assert.Equal("True\nTrue\nFalse", Run("""
            import dataclasses

            @dataclasses.dataclass
            class Point:
                x: int
                y: int

            class Plain:
                pass

            print(dataclasses.is_dataclass(Point))
            print(dataclasses.is_dataclass(Point(1, 2)))
            print(dataclasses.is_dataclass(Plain))
            """));
}

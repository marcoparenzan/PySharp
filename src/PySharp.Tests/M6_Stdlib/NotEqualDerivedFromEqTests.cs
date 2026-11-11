// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

namespace PySharp.Tests.M6_Stdlib;

/// <summary>`!=` must follow `==` when a type defines only `__eq__`: Python's default `object.__ne__` inverts
/// `__eq__`. PySharp used to fall back to a structural comparison, so an Enum/IntEnum member compared with its own
/// integer value was both `==` and `!=` (found as an inverted `rc != MQTT_ERR_SUCCESS` check in paho-mqtt under the
/// AzureIoTForge device agent). Plain CPython behaviour, not specific to that project.</summary>
public class NotEqualDerivedFromEqTests
{
    private static string Run(string body) => Py.Run(body).TrimEnd('\n');

    [Fact]
    public void IntEnum_member_is_not_unequal_to_its_own_integer_value()
        => Assert.Equal("True\nFalse\nFalse\nTrue", Run("""
            import enum
            class E(enum.IntEnum):
                A = 0
                B = 1
            print(E.A == 0)
            print(E.A != 0)
            print(0 != E.A)
            print(E.A != 1)
            """));

    [Fact]
    public void IntEnum_members_compare_with_each_other_and_with_floats_and_bools()
        => Assert.Equal("False\nTrue\nFalse\nTrue", Run("""
            import enum
            class E(enum.IntEnum):
                A = 0
                B = 1
            print(E.A != E.A)
            print(E.A != E.B)
            print(E.A != 0.0)
            print(E.B != 0)
            """));

    [Fact]
    public void IntFlag_member_is_not_unequal_to_its_own_integer_value()
        => Assert.Equal("True\nFalse", Run("""
            import enum
            class F(enum.IntFlag):
                A = 1
            print(F.A == 1)
            print(F.A != 1)
            """));

    [Fact]
    public void Class_defining_only_eq_gets_ne_from_it()
        => Assert.Equal("True\nFalse\nFalse\nTrue", Run("""
            class C:
                def __init__(self, v):
                    self.v = v
                def __eq__(self, o):
                    return isinstance(o, C) and self.v == o.v
            print(C(1) == C(1))
            print(C(1) != C(1))
            print(C(1) == 5)
            print(C(1) != 5)
            """));

    [Fact]
    public void An_explicit_ne_still_wins_over_eq()
        => Assert.Equal("True\nTrue", Run("""
            class C:
                def __eq__(self, o):
                    return True
                def __ne__(self, o):
                    return True
            print(C() == 1)
            print(C() != 1)
            """));

    [Fact]
    public void Ne_that_returns_NotImplemented_falls_back_to_eq()
        => Assert.Equal("False", Run("""
            class C:
                def __eq__(self, o):
                    return True
                def __ne__(self, o):
                    return NotImplemented
            print(C() != 1)
            """));

    [Fact]
    public void Plain_values_and_str_mixin_enums_are_unchanged()
        => Assert.Equal("False\nTrue\nFalse\nTrue", Run("""
            import enum
            class SE(str, enum.Enum):
                A = "a"
            print(SE.A != "a")
            print(SE.A != "b")
            print(1 != 1)
            print(1 != 2)
            """));
}

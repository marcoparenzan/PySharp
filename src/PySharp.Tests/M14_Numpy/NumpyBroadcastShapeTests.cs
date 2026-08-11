// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using PySharpLib.Modules;
using PySharpLib.Runtime;

namespace PySharp.Tests.M14_Numpy;

/// <summary>Pure C# tests for `NumpyModule.BroadcastShape`'s real numpy broadcasting rule
/// (NUMPY_PLAN.md Phase 4.4) — no Python involved, matching that step's own "pure C# helper, unit
/// test directly" instruction.</summary>
public class NumpyBroadcastShapeTests
{
    [Fact]
    public void Identical_shapes_broadcast_to_themselves()
        => Assert.Equal(new[] { 2, 3 }, NumpyModule.BroadcastShape(new[] { 2, 3 }, new[] { 2, 3 }));

    [Fact]
    public void A_size_one_dimension_stretches_to_match_the_other_operand()
        => Assert.Equal(new[] { 2, 3 }, NumpyModule.BroadcastShape(new[] { 2, 1 }, new[] { 1, 3 }));

    [Fact]
    public void A_shorter_shape_is_padded_with_ones_on_the_left_before_comparing()
        => Assert.Equal(new[] { 2, 3 }, NumpyModule.BroadcastShape(new[] { 2, 3 }, new[] { 3 }));

    [Fact]
    public void A_0D_scalar_shape_broadcasts_against_anything()
        => Assert.Equal(new[] { 2, 3 }, NumpyModule.BroadcastShape(Array.Empty<int>(), new[] { 2, 3 }));

    [Fact]
    public void Incompatible_shapes_raise_a_real_ValueError()
    {
        var ex = Assert.Throws<PyRaise>(() => NumpyModule.BroadcastShape(new[] { 3 }, new[] { 2 }));
        Assert.Equal("ValueError", ex.Value.Class.Name);
    }
}

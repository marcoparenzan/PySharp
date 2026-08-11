// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using PySharpLib.Modules;

namespace PySharp.Tests.M14_Numpy;

/// <summary>Pure C# tests for `NdArrayData`'s real C-order stride computation (NUMPY_PLAN.md
/// Phase 1.1) — no Python involved, matching that step's own "unit-test the stride math directly"
/// instruction.</summary>
public class NdArrayDataTests
{
    [Fact]
    public void One_dimensional_shape_has_a_unit_stride()
    {
        var data = new NdArrayData(DType.Float64, new double[3], new[] { 3 });
        Assert.Equal(new[] { 1 }, data.Strides);
        Assert.Equal(3, data.Size);
        Assert.Equal(1, data.Ndim);
    }

    [Fact]
    public void Two_dimensional_shape_has_real_C_order_row_major_strides()
    {
        // shape (2, 3): row stride = 3 (elements per row), column stride = 1
        var data = new NdArrayData(DType.Float64, new double[6], new[] { 2, 3 });
        Assert.Equal(new[] { 3, 1 }, data.Strides);
        Assert.Equal(6, data.Size);
    }

    [Fact]
    public void Three_dimensional_shape_strides_are_the_product_of_every_axis_to_the_right()
    {
        // shape (2, 3, 4): strides (12, 4, 1)
        var data = new NdArrayData(DType.Float64, new double[24], new[] { 2, 3, 4 });
        Assert.Equal(new[] { 12, 4, 1 }, data.Strides);
        Assert.Equal(24, data.Size);
    }

    [Fact]
    public void A_scalar_zero_dimensional_shape_has_size_one_and_no_strides()
    {
        var data = new NdArrayData(DType.Float64, new double[1], Array.Empty<int>());
        Assert.Empty(data.Strides);
        Assert.Equal(1, data.Size);
        Assert.Equal(0, data.Ndim);
    }
}

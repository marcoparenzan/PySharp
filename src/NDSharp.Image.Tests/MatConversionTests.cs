// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using NDSharp;
using NDSharp.Image;

namespace NDSharp.Image.Tests;

public class MatConversionTests
{
    [Fact]
    public void OpenCv_native_library_loads_and_a_gray_conversion_works()
    {
        var bgr = np.Full(new[] { 2, 2, 3 }, 100L, DType.UInt8);
        var gray = Cv.CvtColor(bgr, 6); // COLOR_BGR2GRAY
        Assert.Equal(new[] { 2, 2 }, gray.Shape);
        Assert.Equal(DType.UInt8, gray.DType);
        Assert.Equal(100, Convert.ToInt32(gray[0, 0]));
    }

    [Theory]
    [InlineData(DType.UInt8)]
    [InlineData(DType.Int16)]
    [InlineData(DType.Float32)]
    [InlineData(DType.Float64)]
    public void Round_trip_through_Mat_preserves_shape_dtype_and_data(DType dt)
    {
        var a = np.Reshape(np.Arange(0L, 24L).AsType(dt), 2, 4, 3);
        using var m = Mats.ToMat(a);
        var b = Mats.FromMat(m);
        Assert.Equal(a.Shape, b.Shape);
        Assert.Equal(dt, b.DType);
        Assert.True((bool)np.All(np.Equal(a, b)).GetAt(0));
    }

    [Fact]
    public void Non_contiguous_views_are_copied_correctly()
    {
        var a = np.Reshape(np.Arange(0L, 36L).AsType(DType.UInt8), 6, 6);
        var view = a.Get(new Slice(1, 5), new Slice(2, 6));
        using var m = Mats.ToMat(view);
        Assert.True((bool)np.All(np.Equal(view, Mats.FromMat(m))).GetAt(0));
    }
}

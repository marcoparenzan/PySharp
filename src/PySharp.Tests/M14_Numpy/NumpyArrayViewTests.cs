// Copyright (c) 2026 Marco Parenzan
// Licensed under the MIT License. See the LICENSE file in the project root.
using NDSharp;
using PySharpLib.Numpy;
using PySharpLib.Runtime;

namespace PySharp.Tests.M14_Numpy;

public class NumpyArrayViewTests
{
    [Fact]
    public void RealPythonSliceExportsNegativeStrideAndSharesWrites()
    {
        var engine = Py.NewEngine(new StringWriter());
        var module = engine.Run("import numpy as np\na = np.arange(6.0)\nb = a[::-2]\n");
        var view = NumpyArrayViews.ToView<double>(module.Dict["b"]);
        Assert.Equal(-2, view.Strides[0]);
        Assert.Equal(5d, view[0]);
        view[1] = 23;
        Assert.Equal(23d, NumpyArrayViews.ToView<double>(module.Dict["a"])[3]);
        var restored = NumpyArrayViews.ToView<double>(NumpyArrayViews.FromView(view, DType.Float64));
        Assert.Same(view.Buffer, restored.Buffer);
        Assert.Equal(view.Offset, restored.Offset);
        Assert.Equal(view.Strides.ToArray(), restored.Strides.ToArray());
    }

    [Fact]
    public void ImportCanBeUsedByPythonWithoutElementCopy()
    {
        var engine = Py.NewEngine(new StringWriter());
        double[] buffer = [1, 2, 3, 4, 5, 6];
        var view = new ArrayView<double>(buffer, [2, 3], [1, 2]);
        engine.SetVariable("shared", NumpyArrayViews.FromView(view, DType.Float64));
        engine.Run("shared[1, 2] = 19\n");
        Assert.Equal(19d, buffer[5]);
    }

    [Fact]
    public void RoundTripPreservesSpecialValueBitsAndSnapshotMetadata()
    {
        double[] buffer = [-0d, double.NaN, double.PositiveInfinity, double.NegativeInfinity, double.Epsilon];
        var view = new ArrayView<double>(buffer, [5], [1]);
        var wrapper = NumpyArrayViews.FromView(view, DType.Float64);
        var exported = NumpyArrayViews.ToView<double>(wrapper);
        Assert.Same(buffer, exported.Buffer);
        for (int i = 0; i < buffer.Length; i++)
            Assert.Equal(BitConverter.DoubleToInt64Bits(view[i]), BitConverter.DoubleToInt64Bits(exported[i]));
        Assert.IsType<NDArray>(wrapper.Native).Shape[0] = 1;
        Assert.Equal(5, exported.Shape[0]);
    }

    [Fact]
    public void BroadcastEmptyAndScalarViewsRetainTheirShape()
    {
        var broadcast = new ArrayView<long>([7], [3], [0]);
        var result = NumpyArrayViews.ToView<long>(NumpyArrayViews.FromView(broadcast, DType.Int64));
        result[2] = 8;
        Assert.Equal(8, result[0]);
        var scalar = NumpyArrayViews.FromView(new ArrayView<bool>([true], [], []), DType.Bool);
        Assert.Equal(0, NumpyArrayViews.ToView<bool>(scalar).Rank);
        var empty = NumpyArrayViews.FromView(new ArrayView<byte>([], [0, 2], [2, 1]), DType.UInt8);
        Assert.Equal(0, NumpyArrayViews.ToView<byte>(empty).Size);
    }

    [Fact]
    public void WrongStorageAndNonArrayValuesAreRejected()
    {
        var wrapper = NumpyArrayViews.FromView(new ArrayView<uint>([1], [1], [1]), DType.UInt32);
        Assert.Throws<NDTypeException>(() => NumpyArrayViews.ToView<int>(wrapper));
        Assert.Throws<NDTypeException>(() => NumpyArrayViews.FromView(new ArrayView<uint>([1], [1], [1]), DType.Int32));
        Assert.Throws<ArgumentException>(() => NumpyArrayViews.ToView<double>(1d));
        Assert.Throws<ArgumentNullException>(() => NumpyArrayViews.ToView<double>(null!));
    }
}

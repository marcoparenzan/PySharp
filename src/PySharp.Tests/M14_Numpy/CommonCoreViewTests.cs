// Copyright (c) 2026 Marco Parenzan
// Licensed under the MIT License. See the LICENSE file in the project root.
using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using MatSharpLib.NDSharpBridge;
using MatSharpLib.Runtime;
using NDSharp;
using PySharpLib.Numpy;

namespace PySharp.Tests.M14_Numpy;

public class CommonCoreViewTests
{
    public static IEnumerable<object[]> DTypesToTest()
        => Enum.GetValues<DType>().Select(dtype => new object[] { dtype });

    [Theory]
    [MemberData(nameof(DTypesToTest))]
    public void EveryDeclaredDtypeRetainsBytesAndMetadata(DType dtype)
    {
        var method = typeof(CommonCoreViewTests).GetMethod(nameof(CheckType), BindingFlags.NonPublic | BindingFlags.Static)!;
        try { method.MakeGenericMethod(dtype.ClrType()).Invoke(null, [dtype]); }
        catch (TargetInvocationException error) when (error.InnerException != null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw();
        }
    }

    private static void CheckType<T>(DType dtype) where T : unmanaged
    {
        T[] data = (T[])Samples(typeof(T));
        var originalBytes = MemoryMarshal.AsBytes(data.AsSpan()).ToArray();
        var nd = new NDArray(dtype, data, [data.Length], [-1], data.Length - 1, null);
        var wrapper = NumpyArrayViews.FromView(nd.AsArrayView<T>(), dtype);
        var view = NumpyArrayViews.ToView<T>(wrapper);
        var restored = NDArray.FromArrayView(view, dtype);
        Assert.Equal(dtype, restored.DType);
        Assert.Same(data, restored.Buffer);
        Assert.Equal(new[] { data.Length }, restored.Shape);
        Assert.Equal(new[] { -1 }, restored.Strides);
        Assert.Equal(data.Length - 1, restored.Offset);
        Assert.Equal(originalBytes, MemoryMarshal.AsBytes(restored.Data<T>().AsSpan()).ToArray());
        view[0] = data[0];
        Assert.Equal(MemoryMarshal.AsBytes(data.AsSpan(0, 1)).ToArray(), MemoryMarshal.AsBytes(data.AsSpan(data.Length - 1, 1)).ToArray());
    }

    private static Array Samples(Type type)
    {
        if (type == typeof(bool)) return new bool[] { false, true };
        if (type == typeof(sbyte)) return new sbyte[] { sbyte.MinValue, 0, sbyte.MaxValue };
        if (type == typeof(byte)) return new byte[] { 0, 1, byte.MaxValue };
        if (type == typeof(short)) return new short[] { short.MinValue, 0, short.MaxValue };
        if (type == typeof(ushort)) return new ushort[] { 0, 1, ushort.MaxValue };
        if (type == typeof(int)) return new int[] { int.MinValue, 0, int.MaxValue };
        if (type == typeof(uint)) return new uint[] { 0, 1, uint.MaxValue };
        if (type == typeof(long)) return new long[] { long.MinValue, 0, long.MaxValue };
        if (type == typeof(ulong)) return new ulong[] { 0, 1, ulong.MaxValue };
        if (type == typeof(Half)) return new Half[] { (Half)0, BitConverter.Int16BitsToHalf(unchecked((short)0x8000)), Half.Epsilon, Half.PositiveInfinity, Half.NegativeInfinity, Half.NaN };
        if (type == typeof(float)) return new float[] { 0, -0f, float.Epsilon, float.PositiveInfinity, float.NegativeInfinity, BitConverter.Int32BitsToSingle(0x7fc01234) };
        if (type == typeof(double)) return new double[] { 0, -0d, double.Epsilon, double.PositiveInfinity, double.NegativeInfinity, BitConverter.Int64BitsToDouble(0x7ff8000000001234) };
        if (type == typeof(Complex)) return new Complex[] { new(-0d, double.Epsilon), new(double.NaN, double.NegativeInfinity), new(double.PositiveInfinity, -0d) };
        throw new InvalidOperationException("No sample corpus for " + type);
    }

    [Fact]
    public void MatPythonTransposeRoundTripSharesStorageAndPreservesBits()
    {
        double[] data = [0, -0d, double.Epsilon, double.PositiveInfinity, double.NegativeInfinity, BitConverter.Int64BitsToDouble(0x7ff8000000001234)];
        var before = MemoryMarshal.AsBytes(data.AsSpan()).ToArray();
        var mat = Mat.Matrix(2, 3, data);
        var engine = Py.NewEngine(new StringWriter());
        engine.SetVariable("shared", NumpyArrayViews.FromView(MatArrayViews.ToView(mat), DType.Float64));
        var module = engine.Run("echo = shared.T.T\n");
        var restored = MatArrayViews.FromView(NumpyArrayViews.ToView<double>(module.Dict["echo"]));
        Assert.Same(data, restored.Re);
        Assert.Equal(mat.Dims, restored.Dims);
        Assert.Equal(before, MemoryMarshal.AsBytes(restored.Re.AsSpan()).ToArray());
        restored.Re[1] = 17;
        Assert.Equal(17d, NumpyArrayViews.ToView<double>(module.Dict["echo"])[1, 0]);
    }
}

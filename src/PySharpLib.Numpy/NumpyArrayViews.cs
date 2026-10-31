// Copyright (c) 2026 Marco Parenzan
// Licensed under the MIT License. See the LICENSE file in the project root.
using NDSharp;
using PySharpLib.Runtime;

namespace PySharpLib.Numpy;

/// <summary>Host interop between numpy ndarray wrappers and typed shared-storage views.
/// No element conversion, numerical arithmetic or Python scalar coercion occurs.
/// Writes are shared; hosts must synchronize access with interpreter execution.</summary>
public static class NumpyArrayViews
{
    /// <summary>Exports an ndarray wrapper with snapshot shape/strides and shared storage.
    /// T must exactly match CLR storage, including signedness. Scalar wrappers and other
    /// Python values are rejected. Signed and zero strides retain their NDSharp meaning.</summary>
    public static ArrayView<T> ToView<T>(object value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value is not PyInstance { Native: NDArray array } || !Conv.IsNdArray(value))
            throw new ArgumentException("An ndarray wrapper is required.", nameof(value));
        return array.AsArrayView<T>();
    }

    /// <summary>Imports a view as an ndarray wrapper, including rank-zero arrays, without
    /// copying elements. Explicit dtype must exactly match T. Metadata is copied; Base/Tag
    /// identity from the original wrapper is not transferred. Floating-point bits are unchanged.
    /// NDSharp owns dtype support; split-complex Mat storage is not converted by this API.</summary>
    public static PyInstance FromView<T>(ArrayView<T> view, DType dtype)
        => Conv.Wrap(NDArray.FromArrayView(view, dtype));
}

// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using System.Numerics;

namespace PySharpLib.Runtime;

/// <summary>
/// Equality/hash with Python semantics for dict keys and set members:
/// 1 == 1.0 == True, strings/bytes/tuples by value.
/// </summary>
public sealed class PyEqualityComparer : IEqualityComparer<object>
{
    public static readonly PyEqualityComparer Instance = new();

    public new bool Equals(object? x, object? y) => PyOps.PyEquals(x!, y!);

    public int GetHashCode(object obj) => PyOps.PyHash(obj);
}

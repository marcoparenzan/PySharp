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

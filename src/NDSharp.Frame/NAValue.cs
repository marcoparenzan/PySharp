// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

namespace NDSharp.Frame;

/// <summary>The missing value of nullable dtypes (<c>pd.NA</c>): what an element of an <c>Int64</c>, <c>Float64</c>, <c>boolean</c> or <c>string</c> column reads as when it is missing.</summary>
public sealed class NAValue
{
    public static readonly NAValue Instance = new();
    private NAValue() { }
    public override string ToString() => "<NA>";
}

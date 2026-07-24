// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

namespace PySharpLib.Runtime;

/// <summary>Control-flow signals implemented as C# exceptions.</summary>
public sealed class BreakSignal : Exception
{
    public static readonly BreakSignal Instance = new();
    private BreakSignal() { }
}

public sealed class ContinueSignal : Exception
{
    public static readonly ContinueSignal Instance = new();
    private ContinueSignal() { }
}

public sealed class ReturnSignal : Exception
{
    public object Value { get; }
    public ReturnSignal(object value) => Value = value;
}

/// <summary>Eccezione Python in volo: trasporta l'istanza dell'eccezione (PyInstance).</summary>
public sealed class PyRaise : Exception
{
    public PyInstance Value { get; }

    public PyRaise(PyInstance value)
        : base(PyErr.FormatForClr(value))
        => Value = value;
}

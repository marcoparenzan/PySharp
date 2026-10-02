// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

namespace NDSharp;

/// <summary>Base class of every error NDSharp raises on purpose. The subclasses mirror the Python
/// exception types numpy raises in the same situations so a language binding can map them 1:1
/// (<see cref="NDValueException"/> → <c>ValueError</c>, <see cref="NDTypeException"/> →
/// <c>TypeError</c>, <see cref="NDIndexException"/> → <c>IndexError</c>, ...).</summary>
public abstract class NDException : Exception
{
    protected NDException(string message) : base(message) { }
}

public sealed class NDValueException : NDException
{
    public NDValueException(string message) : base(message) { }
}

public sealed class NDTypeException : NDException
{
    public NDTypeException(string message) : base(message) { }
}

public sealed class NDIndexException : NDException
{
    public NDIndexException(string message) : base(message) { }
}

/// <summary>numpy's <c>AxisError</c> — an axis argument out of range. In numpy it derives from both
/// <c>ValueError</c> and <c>IndexError</c>; bindings should map it to <c>ValueError</c>.</summary>
public sealed class NDAxisException : NDException
{
    public NDAxisException(int axis, int ndim)
        : base($"axis {axis} is out of bounds for array of dimension {ndim}") { }
}

public sealed class NDOverflowException : NDException
{
    public NDOverflowException(string message) : base(message) { }
}

/// <summary>A feature numpy has but this library has not implemented (yet).</summary>
public sealed class NDNotSupportedException : NDException
{
    public NDNotSupportedException(string message) : base(message) { }
}

/// <summary>numpy's <c>LinAlgError</c> (singular matrix, no convergence, ...).</summary>
public sealed class NDLinAlgException : NDException
{
    public NDLinAlgException(string message) : base(message) { }
}

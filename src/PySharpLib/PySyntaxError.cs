// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

namespace PySharpLib;

/// <summary>Syntax error raised by lexer/parser, with a position in the source.</summary>
public sealed class PySyntaxError : Exception
{
    public string FileName { get; }
    public int Line { get; }
    public int Column { get; }

    public PySyntaxError(string message, string fileName, int line, int column)
        : base($"{message} ({fileName}, line {line}, col {column})")
    {
        FileName = fileName;
        Line = line;
        Column = column;
    }
}

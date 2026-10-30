using PySharpLib.Parsing;
namespace PySharpLib.Runtime;
/// <summary>Parsed executable source, distinct from function signature metadata PyCode.</summary>
public sealed record PyCompiledCode(string FileName, string Mode, Module? Statements, Expr? Expression);
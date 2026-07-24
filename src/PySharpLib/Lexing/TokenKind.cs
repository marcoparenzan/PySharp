namespace PySharpLib.Lexing;

public enum TokenKind
{
    /// <summary>Identificatore (non keyword).</summary>
    Name,
    /// <summary>Reserved keyword (text in Token.Text).</summary>
    Keyword,
    /// <summary>Numeric literal (raw text in Token.Text, conversion in the parser).</summary>
    Number,
    /// <summary>String literal (decoded value in Token.StringValue; f-string: raw content).</summary>
    Str,
    /// <summary>Bytes literal (value in Token.BytesValue).</summary>
    Bytes,
    /// <summary>Operatore o punteggiatura (testo in Token.Text).</summary>
    Op,
    /// <summary>End of a logical line.</summary>
    Newline,
    /// <summary>Aumento del livello di indentazione.</summary>
    Indent,
    /// <summary>Riduzione del livello di indentazione.</summary>
    Dedent,
    /// <summary>End of source.</summary>
    EndOfFile,
}

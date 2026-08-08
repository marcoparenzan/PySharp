// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using System.Globalization;
using System.Numerics;
using System.Text;
using PySharpLib.Runtime;

namespace PySharpLib.Modules;

/// <summary>unicodedata: found via real idna's `core.py` (`import unicodedata` at module level — a
/// transitive dependency of httpx). `category()` and `normalize()` are real, backed by .NET's own
/// (genuinely comprehensive) `CharUnicodeInfo`/`string.Normalize` Unicode Character Database — not
/// approximations. `combining()`/`bidirectional()`/`name()` have no .NET equivalent (the BCL doesn't
/// expose canonical combining class, bidi category, or character names as queryable per-character
/// data) and are honestly scoped: correct for the ASCII range (where these properties are simple and
/// well-known — no ASCII character combines, and Basic Latin bidi categories are just L/EN/neutral),
/// a documented simplification beyond it. This matters for real target-app use because idna's own
/// `check_bidi` short-circuits to `True` for any label containing no RTL-category character (RFC
/// 5893) — so as long as ASCII never gets misclassified as RTL (it doesn't here), ASCII-only
/// hostnames validate correctly even without a full bidi-category table; only genuine RTL-script
/// (Hebrew/Arabic) domains would see a real functional gap.</summary>
public static class UnicodedataModule
{
    public static PyModule Create()
    {
        var m = new PyModule("unicodedata");
        var d = m.Dict;

        d["category"] = new PyBuiltinFunction("category", (_, a, _) => CategoryCode(CharOf(a[0])));

        d["combining"] = new PyBuiltinFunction("combining", (_, _, _) => BigInteger.Zero);

        d["bidirectional"] = new PyBuiltinFunction("bidirectional", (_, a, _) => Bidirectional(CharOf(a[0])));

        d["name"] = new PyBuiltinFunction("name", (_, a, _) =>
        {
            char c = CharOf(a[0]);
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.OtherNotAssigned)
                return $"U+{(int)c:X4}"; // a real name string isn't available without full UCD data;
                                          // truthiness (assigned vs. not) is what idna's own check needs
            if (a.Length > 1)
                return a[1];
            throw PyErr.ValueError("no such name");
        });

        d["normalize"] = new PyBuiltinFunction("normalize", (_, a, _) =>
        {
            string form = (string)a[0];
            string s = (string)a[1];
            var nf = form switch
            {
                "NFC" => NormalizationForm.FormC,
                "NFD" => NormalizationForm.FormD,
                "NFKC" => NormalizationForm.FormKC,
                "NFKD" => NormalizationForm.FormKD,
                _ => throw PyErr.ValueError($"invalid normalization form '{form}'"),
            };
            return s.Normalize(nf);
        });

        return m;
    }

    private static char CharOf(object o) => ((string)o)[0];

    private static string CategoryCode(char c) => CharUnicodeInfo.GetUnicodeCategory(c) switch
    {
        UnicodeCategory.UppercaseLetter => "Lu",
        UnicodeCategory.LowercaseLetter => "Ll",
        UnicodeCategory.TitlecaseLetter => "Lt",
        UnicodeCategory.ModifierLetter => "Lm",
        UnicodeCategory.OtherLetter => "Lo",
        UnicodeCategory.NonSpacingMark => "Mn",
        UnicodeCategory.SpacingCombiningMark => "Mc",
        UnicodeCategory.EnclosingMark => "Me",
        UnicodeCategory.DecimalDigitNumber => "Nd",
        UnicodeCategory.LetterNumber => "Nl",
        UnicodeCategory.OtherNumber => "No",
        UnicodeCategory.ConnectorPunctuation => "Pc",
        UnicodeCategory.DashPunctuation => "Pd",
        UnicodeCategory.OpenPunctuation => "Ps",
        UnicodeCategory.ClosePunctuation => "Pe",
        UnicodeCategory.InitialQuotePunctuation => "Pi",
        UnicodeCategory.FinalQuotePunctuation => "Pf",
        UnicodeCategory.OtherPunctuation => "Po",
        UnicodeCategory.MathSymbol => "Sm",
        UnicodeCategory.CurrencySymbol => "Sc",
        UnicodeCategory.ModifierSymbol => "Sk",
        UnicodeCategory.OtherSymbol => "So",
        UnicodeCategory.SpaceSeparator => "Zs",
        UnicodeCategory.LineSeparator => "Zl",
        UnicodeCategory.ParagraphSeparator => "Zp",
        UnicodeCategory.Control => "Cc",
        UnicodeCategory.Format => "Cf",
        UnicodeCategory.Surrogate => "Cs",
        UnicodeCategory.PrivateUse => "Co",
        _ => "Cn",
    };

    private static string Bidirectional(char c)
    {
        if (char.IsControl(c))
            return c is '\n' or '\r' ? "B" : c == '\t' ? "S" : "BN";
        if (char.IsWhiteSpace(c))
            return "WS";
        if (char.IsDigit(c))
            return "EN";
        if (char.IsLetter(c))
            return "L";
        return "ON";
    }
}

// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using PySharpLib.Runtime;

namespace PySharpLib.Modules;

/// <summary>encodings.aliases: real CPython's own `encodings/aliases.py` is a large static
/// name -> canonical-codec-name lookup table; this is a practical subset covering the encodings
/// realistically seen in real-world HTTP `Content-Type: charset=` values and common text files, not
/// the full ~600-entry table. Found via charset_normalizer's own `utils.py` (`from
/// encodings.aliases import aliases`, iterated to normalize an encoding name before a codec
/// lookup), reachable from `import requests` (`response.text`'s encoding auto-detection path).</summary>
public static class EncodingsAliasesModule
{
    public static PyModule Create()
    {
        var m = new PyModule("encodings.aliases");
        var aliases = new PyDict();
        foreach (var (alias, canonical) in Aliases)
            aliases[alias] = canonical;
        m.Dict["aliases"] = aliases;
        return m;
    }

    private static readonly (string Alias, string Canonical)[] Aliases =
    {
        ("utf8", "utf_8"), ("utf_8", "utf_8"), ("u8", "utf_8"), ("utf", "utf_8"),
        ("utf8mb4", "utf_8"), ("cp65001", "utf_8"),
        ("utf16", "utf_16"), ("utf_16", "utf_16"), ("u16", "utf_16"),
        ("utf16le", "utf_16_le"), ("utf_16le", "utf_16_le"),
        ("utf16be", "utf_16_be"), ("utf_16be", "utf_16_be"),
        ("utf32", "utf_32"), ("utf_32", "utf_32"),
        ("ascii", "ascii"), ("us_ascii", "ascii"), ("646", "ascii"), ("ansi_x3_4_1968", "ascii"),
        ("latin", "iso8859_1"), ("latin1", "iso8859_1"), ("latin_1", "iso8859_1"),
        ("iso_8859_1", "iso8859_1"), ("iso88591", "iso8859_1"), ("8859", "iso8859_1"),
        ("l1", "iso8859_1"), ("cp819", "iso8859_1"),
        ("iso_8859_2", "iso8859_2"), ("latin2", "iso8859_2"), ("l2", "iso8859_2"),
        ("iso_8859_15", "iso8859_15"), ("latin9", "iso8859_15"),
        ("windows_1250", "cp1250"), ("cp1250", "cp1250"),
        ("windows_1251", "cp1251"), ("cp1251", "cp1251"),
        ("windows_1252", "cp1252"), ("cp1252", "cp1252"), ("ansi", "cp1252"),
        ("windows_1253", "cp1253"), ("cp1253", "cp1253"),
        ("windows_1254", "cp1254"), ("cp1254", "cp1254"),
        ("windows_1255", "cp1255"), ("cp1255", "cp1255"),
        ("windows_1256", "cp1256"), ("cp1256", "cp1256"),
        ("gbk", "gbk"), ("936", "gbk"), ("cp936", "gbk"),
        ("gb2312", "gb2312"), ("gb_2312_80", "gb2312"),
        ("gb18030", "gb18030"),
        ("big5", "big5"), ("cp950", "big5"),
        ("shift_jis", "shift_jis"), ("sjis", "shift_jis"), ("cp932", "shift_jis"),
        ("euc_jp", "euc_jp"), ("eucjp", "euc_jp"),
        ("euc_kr", "euc_kr"), ("euckr", "euc_kr"),
        ("koi8_r", "koi8_r"), ("koi8r", "koi8_r"),
        ("mac_roman", "mac_roman"), ("macroman", "mac_roman"),
        ("cp437", "cp437"), ("ibm437", "cp437"),
        ("cp850", "cp850"), ("ibm850", "cp850"),
    };
}

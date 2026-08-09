// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using System.Text;
using PySharpLib.Runtime;

namespace PySharpLib.Modules;

/// <summary>codecs: real `lookup`/`getincrementaldecoder`, scoped to what real httpx actually needs
/// (`_models.py`'s `codecs.lookup(encoding)` to validate a charset name, `_decoders.py`'s
/// `TextDecoder` wrapping `codecs.getincrementaldecoder(encoding)(errors="replace")` to decode a
/// streamed HTTP response body incrementally). Backed by .NET's own `Decoder`, which already
/// correctly buffers a multi-byte sequence split across chunk boundaries — exactly what "incremental"
/// means here — so this is real behavior, not a stub.</summary>
public static class CodecsModule
{
    public static PyModule Create()
    {
        var m = new PyModule("codecs");
        var d = m.Dict;

        d["lookup"] = new PyBuiltinFunction("lookup", (_, a, _) =>
        {
            string name = (string)a[0];
            var enc = StrModules.GetEncoding(name); // raises PyErr.LookupError for an unknown name
            return MakeCodecInfo(name, enc);
        });

        d["getincrementaldecoder"] = new PyBuiltinFunction("getincrementaldecoder", (_, a, _) =>
        {
            string name = (string)a[0];
            StrModules.GetEncoding(name); // validate eagerly, matching real codecs' own behavior
            return BuildIncrementalDecoderClass(name);
        });

        // Real CPython's byte-order-mark constants. Found via httpx's own `_utils.py`'s
        // `guess_json_utf` (Response.json()'s auto-detection when no explicit charset is given),
        // sniffing a response body's first bytes against these exact values.
        d["BOM_UTF8"] = new PyBytes(new byte[] { 0xEF, 0xBB, 0xBF });
        d["BOM_UTF16_LE"] = new PyBytes(new byte[] { 0xFF, 0xFE });
        d["BOM_UTF16_BE"] = new PyBytes(new byte[] { 0xFE, 0xFF });
        d["BOM_UTF32_LE"] = new PyBytes(new byte[] { 0xFF, 0xFE, 0x00, 0x00 });
        d["BOM_UTF32_BE"] = new PyBytes(new byte[] { 0x00, 0x00, 0xFE, 0xFF });
        d["BOM_LE"] = d["BOM_UTF16_LE"];
        d["BOM_BE"] = d["BOM_UTF16_BE"];
        d["BOM"] = d["BOM_UTF16_LE"]; // native order on the little-endian hosts this project targets
        d["BOM_UTF16"] = d["BOM_UTF16_LE"];
        d["BOM_UTF32"] = d["BOM_UTF32_LE"];

        return m;
    }

    private static PyInstance MakeCodecInfo(string name, Encoding enc)
    {
        var cls = new PyClass("CodecInfo", new List<PyClass>());
        var inst = new PyInstance(cls);
        inst.Dict["name"] = enc.WebName;
        return inst;
    }

    private static Encoding BuildEncodingForErrors(string name, string errors)
    {
        var baseEncoding = StrModules.GetEncoding(name);
        DecoderFallback decoderFallback = errors switch
        {
            "strict" => DecoderFallback.ExceptionFallback,
            "replace" => new DecoderReplacementFallback("�"),
            "ignore" => new DecoderReplacementFallback(""),
            _ => throw PyErr.Raise(PyErr.ValueErrorClass, $"unknown error handler name '{errors}'"),
        };
        return Encoding.GetEncoding(baseEncoding.CodePage, EncoderFallback.ReplacementFallback, decoderFallback);
    }

    private static PyClass BuildIncrementalDecoderClass(string encodingName)
    {
        var cls = new PyClass("IncrementalDecoder", new List<PyClass>());
        void Add(string n, BuiltinFn fn) => cls.Dict[n] = new PyBuiltinFunction($"IncrementalDecoder.{n}", fn);

        Add("__init__", (_, a, kwargs) =>
        {
            var inst = (PyInstance)a[0];
            string errors = a.Length > 1 ? (string)a[1]
                : kwargs is not null && kwargs.TryGetValue("errors", out var e) ? (string)e : "strict";
            inst.Dict["errors"] = errors;
            inst.Dict["__decoder"] = BuildEncodingForErrors(encodingName, errors).GetDecoder();
            return PyNone.Instance;
        });

        Add("decode", (interp, a, kwargs) =>
        {
            var inst = (PyInstance)a[0];
            byte[] data = CryptoModules.AsBytes(a[1]);
            bool final = a.Length > 2 ? PyOps.Truthy(interp, a[2])
                : kwargs is not null && kwargs.TryGetValue("final", out var f) && PyOps.Truthy(interp, f);
            var decoder = (Decoder)inst.Dict["__decoder"];
            try
            {
                // Decoder.Convert (not separate GetCharCount+GetChars calls) is the API .NET
                // documents as safe for incremental/streaming use — calling GetCharCount and
                // GetChars back to back on the same stateful Decoder double-processes any
                // multi-byte sequence held over from a prior call, corrupting it (verified by hand
                // against real CPython: a UTF-8 sequence split across two decode() calls came back
                // wrong until this was switched to Convert).
                var sb = new StringBuilder();
                var chars = new char[data.Length + 8];
                int byteIndex = 0, byteCount = data.Length;
                bool completed;
                do
                {
                    decoder.Convert(data, byteIndex, byteCount, chars, 0, chars.Length, final,
                        out int bytesUsed, out int charsUsed, out completed);
                    sb.Append(chars, 0, charsUsed);
                    byteIndex += bytesUsed;
                    byteCount -= bytesUsed;
                } while (!completed);
                return sb.ToString();
            }
            catch (DecoderFallbackException ex)
            {
                throw PyErr.Raise(PyErr.UnicodeDecodeErrorClass,
                    $"'{encodingName}' codec can't decode byte at position {ex.Index}");
            }
        });

        Add("reset", (_, a, _) =>
        {
            var inst = (PyInstance)a[0];
            ((Decoder)inst.Dict["__decoder"]).Reset();
            return PyNone.Instance;
        });

        return cls;
    }
}

// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using System.Globalization;
using System.Numerics;
using System.Text;
using PySharpLib.Interpretation;

namespace PySharpLib.Runtime;

/// <summary>
/// Mini-linguaggio di format spec: [[fill]align][sign][#][0][width][,][.precision][type].
/// A subset sufficient for the f-strings and str.format of real cases.
/// </summary>
public static class PyFormat
{
    public static string Format(Interp interp, object value, string spec)
    {
        if (spec.Length == 0)
            return PyOps.Str(interp, value);

        // parse spec
        int i = 0;
        char fill = ' ';
        char align = '\0';
        if (spec.Length >= 2 && spec[1] is '<' or '>' or '^' or '=')
        {
            fill = spec[0];
            align = spec[1];
            i = 2;
        }
        else if (spec.Length >= 1 && spec[0] is '<' or '>' or '^' or '=')
        {
            align = spec[0];
            i = 1;
        }

        char sign = '\0';
        if (i < spec.Length && spec[i] is '+' or '-' or ' ')
            sign = spec[i++];

        bool alternate = i < spec.Length && spec[i] == '#';
        if (alternate) i++;

        if (i < spec.Length && spec[i] == '0' && align == '\0')
        {
            fill = '0';
            align = '=';
            i++;
        }

        int width = 0;
        while (i < spec.Length && char.IsDigit(spec[i]))
            width = width * 10 + (spec[i++] - '0');

        bool thousands = i < spec.Length && (spec[i] == ',' || spec[i] == '_');
        char thousandsChar = thousands ? spec[i] : ',';
        if (thousands) i++;

        int precision = -1;
        if (i < spec.Length && spec[i] == '.')
        {
            i++;
            precision = 0;
            while (i < spec.Length && char.IsDigit(spec[i]))
                precision = precision * 10 + (spec[i++] - '0');
        }

        char type = i < spec.Length ? spec[i] : '\0';

        string body;
        string prefix = "";
        bool negative = false;

        switch (type)
        {
            case 'd' or 'b' or 'o' or 'x' or 'X' or 'c' or '\0' when value is BigInteger or bool && type != '\0' || value is BigInteger && type == '\0':
            {
                var n = PyOps.AsBigInt(value, "format");
                negative = n.Sign < 0;
                var abs = BigInteger.Abs(n);
                body = type switch
                {
                    'b' => ToBase(abs, 2),
                    'o' => ToBase(abs, 8),
                    'x' => ToBase(abs, 16),
                    'X' => ToBase(abs, 16).ToUpperInvariant(),
                    'c' => ((char)(int)abs).ToString(),
                    _ => abs.ToString(CultureInfo.InvariantCulture),
                };
                if (alternate && type is 'b' or 'o' or 'x' or 'X')
                    prefix = "0" + char.ToLowerInvariant(type);
                if (thousands && type is 'd' or '\0')
                    body = AddThousands(body, thousandsChar);
                break;
            }
            case 'f' or 'F' or 'e' or 'E' or 'g' or 'G' or '%' or '\0' when value is double || value is BigInteger && type != '\0' && type != 'd':
            {
                double d = PyOps.AsDouble(value);
                if (type == '%')
                    d *= 100;
                negative = d < 0 || (d == 0 && double.IsNegative(d));
                double abs = Math.Abs(d);
                int prec = precision >= 0 ? precision : 6;
                body = type switch
                {
                    'e' => abs.ToString("0." + new string('0', prec) + "e+00", CultureInfo.InvariantCulture),
                    'E' => abs.ToString("0." + new string('0', prec) + "E+00", CultureInfo.InvariantCulture),
                    'g' or 'G' => FormatG(abs, prec == 0 ? 1 : prec, type == 'G'),
                    '\0' => PyOps.ReprDouble(abs),
                    _ => abs.ToString("F" + prec, CultureInfo.InvariantCulture),
                };
                if (type == '%')
                    body += "%";
                if (thousands)
                {
                    int dot = body.IndexOf('.');
                    string intPart = dot < 0 ? body : body[..dot];
                    string rest = dot < 0 ? "" : body[dot..];
                    body = AddThousands(intPart, thousandsChar) + rest;
                }
                break;
            }
            case 's' or '\0':
            {
                body = PyOps.Str(interp, value);
                if (precision >= 0 && body.Length > precision)
                    body = body[..precision];
                if (align == '\0')
                    align = '<';
                sign = '\0';
                break;
            }
            default:
                throw PyErr.ValueError($"Unknown format code '{type}' for object of type '{PyOps.TypeName(value)}'");
        }

        string signStr = (negative ? "-" : sign switch
        {
            '+' => "+",
            ' ' => " ",
            _ => "",
        }) + prefix;

        if (align == '\0')
            align = PyOps.IsNumber(value) ? '>' : '<';

        int pad = width - body.Length - signStr.Length;
        if (pad <= 0)
            return signStr + body;

        return align switch
        {
            '<' => signStr + body + new string(fill, pad),
            '>' => new string(fill, pad) + signStr + body,
            '^' => new string(fill, pad / 2) + signStr + body + new string(fill, pad - pad / 2),
            '=' => signStr + new string(fill, pad) + body,
            _ => signStr + body,
        };
    }

    private static string ToBase(BigInteger n, int numBase)
    {
        if (n.IsZero)
            return "0";
        const string digits = "0123456789abcdef";
        var sb = new StringBuilder();
        while (!n.IsZero)
        {
            sb.Insert(0, digits[(int)(n % numBase)]);
            n /= numBase;
        }
        return sb.ToString();
    }

    private static string AddThousands(string s, char sep)
    {
        var sb = new StringBuilder();
        int count = 0;
        for (int i = s.Length - 1; i >= 0; i--)
        {
            sb.Insert(0, s[i]);
            if (++count % 3 == 0 && i > 0)
                sb.Insert(0, sep);
        }
        return sb.ToString();
    }

    private static string FormatG(double d, int precision, bool upper)
    {
        string s = d.ToString("G" + precision, CultureInfo.InvariantCulture);
        s = upper ? s.Replace("e", "E") : s.Replace("E", "e");
        return s;
    }
}

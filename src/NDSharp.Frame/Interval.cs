// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using System.Globalization;

namespace NDSharp.Frame;

/// <summary>A pandas <c>Interval</c> (the elements of <c>cut</c>/<c>qcut</c> categories): bounds are int64 or float64 numbers, <see cref="Closed"/> is
/// right / left / both / neither.</summary>
public sealed class IntervalValue : IEquatable<IntervalValue>
{
    public object Left { get; }
    public object Right { get; }
    public string Closed { get; }

    public IntervalValue(object left, object right, string closed = "right") { Left = left; Right = right; Closed = closed; }

    public bool IsInt => Left is long && Right is long;
    public double LeftD => Convert.ToDouble(Left, CultureInfo.InvariantCulture);
    public double RightD => Convert.ToDouble(Right, CultureInfo.InvariantCulture);

    private static string Num(object v, bool asFloat)
    {
        if (v is long l && !asFloat) return l.ToString(CultureInfo.InvariantCulture);
        double d = Convert.ToDouble(v, CultureInfo.InvariantCulture);
        string t = d.ToString("R", CultureInfo.InvariantCulture);
        if (double.IsInfinity(d)) return d > 0 ? "inf" : "-inf";
        if (t.Contains('E')) t = t.Replace("E+", "e+").Replace("E-", "e-").Replace("E", "e");
        return t.Contains('.') || t.Contains('e') ? t : t + ".0";
    }

    /// <summary>The <c>(left, right]</c> text; <paramref name="asFloat"/> prints integer bounds as floats (pandas does when the array also holds missing values).</summary>
    public string Text(bool asFloat = false)
    {
        string open = Closed is "right" or "neither" ? "(" : "[";
        string close = Closed is "left" or "neither" ? ")" : "]";
        return open + Num(Left, asFloat) + ", " + Num(Right, asFloat) + close;
    }

    public override string ToString() => Text();

    public bool Contains(double x)
    {
        double l = LeftD, r = RightD;
        bool lo = Closed is "left" or "both" ? x >= l : x > l;
        bool hi = Closed is "right" or "both" ? x <= r : x < r;
        return lo && hi;
    }

    public bool Equals(IntervalValue? o) => o is not null && Closed == o.Closed && LeftD == o.LeftD && RightD == o.RightD;
    public override bool Equals(object? obj) => obj is IntervalValue o && Equals(o);
    public override int GetHashCode() => HashCode.Combine(LeftD, RightD, Closed);
}

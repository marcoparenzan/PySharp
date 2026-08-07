// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using PySharpLib.Runtime;

namespace PySharpLib.Modules;

/// <summary>colorsys: RGB/YIQ/HLS/HSV conversions — pure math, ported directly from CPython's
/// algorithms (small and self-contained, so implemented in full rather than just the functions a
/// probe happened to need). Found via pydantic v1's real dependency chain (the Color type). See
/// FASTAPI_PLAN.md Phase 1.9.</summary>
public static class ColorSysModule
{
    public static PyModule Create()
    {
        var m = new PyModule("colorsys");
        var d = m.Dict;

        d["rgb_to_yiq"] = new PyBuiltinFunction("rgb_to_yiq", (_, a, _) =>
        {
            var (r, g, b) = Args3(a);
            double y = 0.30 * r + 0.59 * g + 0.11 * b;
            double i = 0.74 * (r - y) - 0.27 * (b - y);
            double q = 0.48 * (r - y) + 0.41 * (b - y);
            return new PyTuple(new object[] { y, i, q });
        });
        d["yiq_to_rgb"] = new PyBuiltinFunction("yiq_to_rgb", (_, a, _) =>
        {
            var (y, i, q) = Args3(a);
            double r = y + 0.9469 * i + 0.6236 * q;
            double g = y - 0.2748 * i - 0.6357 * q;
            double b = y - 1.1 * i + 1.7 * q;
            return new PyTuple(new object[] { Clamp01(r), Clamp01(g), Clamp01(b) });
        });

        d["rgb_to_hls"] = new PyBuiltinFunction("rgb_to_hls", (_, a, _) =>
        {
            var (r, g, b) = Args3(a);
            double maxc = Math.Max(r, Math.Max(g, b));
            double minc = Math.Min(r, Math.Min(g, b));
            double sum = maxc + minc;
            double l = sum / 2.0;
            if (maxc == minc)
                return new PyTuple(new object[] { 0.0, l, 0.0 });
            double diff = maxc - minc;
            double s = l <= 0.5 ? diff / sum : diff / (2.0 - sum);
            double rc = (maxc - r) / diff;
            double gc = (maxc - g) / diff;
            double bc = (maxc - b) / diff;
            double h;
            if (r == maxc) h = bc - gc;
            else if (g == maxc) h = 2.0 + rc - bc;
            else h = 4.0 + gc - rc;
            h = h / 6.0 % 1.0;
            if (h < 0) h += 1.0;
            return new PyTuple(new object[] { h, l, s });
        });
        d["hls_to_rgb"] = new PyBuiltinFunction("hls_to_rgb", (_, a, _) =>
        {
            var (h, l, s) = Args3(a);
            if (s == 0.0)
                return new PyTuple(new object[] { l, l, l });
            double m2 = l <= 0.5 ? l * (1.0 + s) : l + s - l * s;
            double m1 = 2.0 * l - m2;
            return new PyTuple(new object[] { HlsValue(m1, m2, h + 1.0 / 3.0), HlsValue(m1, m2, h), HlsValue(m1, m2, h - 1.0 / 3.0) });
        });

        d["rgb_to_hsv"] = new PyBuiltinFunction("rgb_to_hsv", (_, a, _) =>
        {
            var (r, g, b) = Args3(a);
            double maxc = Math.Max(r, Math.Max(g, b));
            double minc = Math.Min(r, Math.Min(g, b));
            double v = maxc;
            if (maxc == minc)
                return new PyTuple(new object[] { 0.0, 0.0, v });
            double diff = maxc - minc;
            double s = diff / maxc;
            double rc = (maxc - r) / diff;
            double gc = (maxc - g) / diff;
            double bc = (maxc - b) / diff;
            double h;
            if (r == maxc) h = bc - gc;
            else if (g == maxc) h = 2.0 + rc - bc;
            else h = 4.0 + gc - rc;
            h = h / 6.0 % 1.0;
            if (h < 0) h += 1.0;
            return new PyTuple(new object[] { h, s, v });
        });
        d["hsv_to_rgb"] = new PyBuiltinFunction("hsv_to_rgb", (_, a, _) =>
        {
            var (h, s, v) = Args3(a);
            if (s == 0.0)
                return new PyTuple(new object[] { v, v, v });
            int i = (int)(h * 6.0);
            double f = h * 6.0 - i;
            double p = v * (1.0 - s);
            double q = v * (1.0 - s * f);
            double t = v * (1.0 - s * (1.0 - f));
            return (i % 6) switch
            {
                0 => new PyTuple(new object[] { v, t, p }),
                1 => new PyTuple(new object[] { q, v, p }),
                2 => new PyTuple(new object[] { p, v, t }),
                3 => new PyTuple(new object[] { p, q, v }),
                4 => new PyTuple(new object[] { t, p, v }),
                _ => new PyTuple(new object[] { v, p, q }),
            };
        });

        return m;
    }

    private static (double, double, double) Args3(object[] a) =>
        (PyOps.AsDouble(a[0]), PyOps.AsDouble(a[1]), PyOps.AsDouble(a[2]));

    private static double Clamp01(double x) => Math.Max(0.0, Math.Min(1.0, x));

    private static double HlsValue(double m1, double m2, double hue)
    {
        hue %= 1.0;
        if (hue < 0) hue += 1.0;
        if (hue < 1.0 / 6.0) return m1 + (m2 - m1) * hue * 6.0;
        if (hue < 0.5) return m2;
        if (hue < 2.0 / 3.0) return m1 + (m2 - m1) * (2.0 / 3.0 - hue) * 6.0;
        return m1;
    }
}

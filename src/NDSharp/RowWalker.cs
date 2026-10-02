// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

namespace NDSharp;

/// <summary>Walks up to three arrays of the same (broadcast) shape in C-order, one innermost "row"
/// at a time. Adjacent dimensions that are contiguous for every operand are coalesced first, so a
/// dense array is a single long row and the hot loop is a plain strided <c>for</c>.
///
/// Usage: <code>var w = new RowWalker(shape, sa, sb);
/// do { for (i &lt; w.InnerLen) { a[offA + w.Off0 + i*w.InnerStride0] ... } } while (w.Next());</code>
/// Offsets are relative to each operand's own starting offset (add <see cref="NDArray.Offset"/>).</summary>
internal struct RowWalker
{
    public int InnerLen;
    public int InnerStride0, InnerStride1, InnerStride2;
    public int Off0, Off1, Off2;

    private readonly int[] _shape;     // coalesced outer shape (excluding the inner dimension)
    private readonly int[] _s0, _s1, _s2; // coalesced outer strides
    private readonly int[] _idx;
    private readonly bool _empty;

    public RowWalker(int[] shape, int[] s0, int[]? s1 = null, int[]? s2 = null)
    {
        int n = shape.Length;
        var cs = new List<int>(n);
        var c0 = new List<int>(n);
        var c1 = new List<int>(n);
        var c2 = new List<int>(n);
        _empty = false;
        foreach (var d in shape)
            if (d == 0) _empty = true;

        for (int i = 0; i < n; i++)
        {
            if (shape[i] == 1) continue;
            int a0 = s0[i], a1 = s1 is null ? 0 : s1[i], a2 = s2 is null ? 0 : s2[i];
            if (cs.Count > 0)
            {
                int last = cs.Count - 1;
                // Merge into the previous (outer) dimension when this one is its dense continuation.
                if (c0[last] == a0 * shape[i] && c1[last] == a1 * shape[i] && c2[last] == a2 * shape[i])
                {
                    cs[last] *= shape[i];
                    c0[last] = a0;
                    c1[last] = a1;
                    c2[last] = a2;
                    continue;
                }
            }
            cs.Add(shape[i]);
            c0.Add(a0);
            c1.Add(a1);
            c2.Add(a2);
        }

        if (_empty)
        {
            InnerLen = 0;
            InnerStride0 = InnerStride1 = InnerStride2 = 0;
            _shape = _s0 = _s1 = _s2 = Array.Empty<int>();
            _idx = Array.Empty<int>();
        }
        else if (cs.Count == 0)
        {
            InnerLen = 1;
            InnerStride0 = InnerStride1 = InnerStride2 = 0;
            _shape = _s0 = _s1 = _s2 = Array.Empty<int>();
            _idx = Array.Empty<int>();
        }
        else
        {
            int last = cs.Count - 1;
            InnerLen = cs[last];
            InnerStride0 = c0[last];
            InnerStride1 = c1[last];
            InnerStride2 = c2[last];
            _shape = cs.Take(last).ToArray();
            _s0 = c0.Take(last).ToArray();
            _s1 = c1.Take(last).ToArray();
            _s2 = c2.Take(last).ToArray();
            _idx = new int[last];
        }
        Off0 = Off1 = Off2 = 0;
    }

    /// <summary>Advances to the next row; false when the walk is finished.</summary>
    public bool Next()
    {
        for (int d = _shape.Length - 1; d >= 0; d--)
        {
            _idx[d]++;
            Off0 += _s0[d];
            Off1 += _s1[d];
            Off2 += _s2[d];
            if (_idx[d] < _shape[d])
                return true;
            Off0 -= _s0[d] * _shape[d];
            Off1 -= _s1[d] * _shape[d];
            Off2 -= _s2[d] * _shape[d];
            _idx[d] = 0;
        }
        return false;
    }
}

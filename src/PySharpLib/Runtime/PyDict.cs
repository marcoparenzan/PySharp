// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

namespace PySharpLib.Runtime;

/// <summary>
/// Python dict: a map with preserved insertion order and Python equality semantics
/// for the keys (1 == 1.0 == True).
/// </summary>
public sealed class PyDict
{
    private readonly Dictionary<object, LinkedListNode<KeyValuePair<object, object>>> _index;
    private readonly LinkedList<KeyValuePair<object, object>> _entries = new();

    public PyDict()
        => _index = new Dictionary<object, LinkedListNode<KeyValuePair<object, object>>>(PyEqualityComparer.Instance);

    public int Count => _entries.Count;

    public bool TryGet(object key, out object value)
    {
        if (_index.TryGetValue(key, out var node))
        {
            value = node.Value.Value;
            return true;
        }
        value = PyNone.Instance;
        return false;
    }

    public object this[object key]
    {
        get => TryGet(key, out var v) ? v : throw PyErr.KeyError(key);
        set
        {
            if (_index.TryGetValue(key, out var node))
            {
                node.Value = new KeyValuePair<object, object>(node.Value.Key, value);
            }
            else
            {
                var newNode = _entries.AddLast(new KeyValuePair<object, object>(key, value));
                _index[key] = newNode;
            }
        }
    }

    public bool ContainsKey(object key) => _index.ContainsKey(key);

    public bool Remove(object key)
    {
        if (!_index.TryGetValue(key, out var node))
            return false;
        _entries.Remove(node);
        _index.Remove(key);
        return true;
    }

    public void Clear()
    {
        _index.Clear();
        _entries.Clear();
    }

    public IEnumerable<object> Keys => _entries.Select(e => e.Key);
    public IEnumerable<object> Values => _entries.Select(e => e.Value);
    public IEnumerable<KeyValuePair<object, object>> Entries => _entries;

    /// <summary>Prima coppia (per popitem LIFO si usa Last).</summary>
    public KeyValuePair<object, object>? LastEntry => _entries.Last?.Value;

    public PyDict Copy()
    {
        var d = new PyDict();
        foreach (var e in _entries)
            d[e.Key] = e.Value;
        return d;
    }

    public void Update(PyDict other)
    {
        foreach (var e in other.Entries)
            this[e.Key] = e.Value;
    }
}

/// <summary>
/// dict.keys(): a live view over the source dict — iterates in insertion order (unlike a plain
/// set/PySet, which real CPython's dict_keys is not, but PySharp's dict.keys() used to be
/// represented as before this type existed, losing order) while also supporting the set operators
/// (&amp;/|/-/^) real CPython's dict_keys view supports (dict keys are already unique, so treating
/// them as a set for those operators is exact, not an approximation). Found via pydantic's real
/// `kwargs.keys() &amp; some_set` usage (ModelMetaclass.__new__). See FASTAPI_PLAN.md Phase 1.9.
/// </summary>
public sealed class PyDictKeysView
{
    public PyDict Source { get; }
    public PyDictKeysView(PyDict source) => Source = source;
}

/// <summary>set Python.</summary>
public sealed class PySet
{
    public HashSet<object> Items { get; }
    public PySet() => Items = new HashSet<object>(PyEqualityComparer.Instance);
    public PySet(IEnumerable<object> items) => Items = new HashSet<object>(items, PyEqualityComparer.Instance);
}

/// <summary>frozenset Python (immutabile, hashabile).</summary>
public sealed class PyFrozenSet
{
    public HashSet<object> Items { get; }
    public PyFrozenSet(IEnumerable<object> items) => Items = new HashSet<object>(items, PyEqualityComparer.Instance);
}

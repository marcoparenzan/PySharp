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

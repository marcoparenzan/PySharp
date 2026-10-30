// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using PySharpLib.Runtime;

namespace PySharpLib.Importing;

/// <summary>
/// For a binding whose classes are process-wide singletons (pandas: <c>Series</c>, <c>DataFrame</c>, <c>Timestamp</c>… are static
/// <see cref="PyClass"/> objects shared by every interpreter) building the module fills those classes' dictionaries. Doing that on
/// every <c>import</c> is a data race as soon as two interpreters import at the same time (a <see cref="Dictionary{TKey,TValue}"/>
/// corrupted by concurrent writes stays corrupted for the whole process), and it also rewrites tables other threads are reading.
/// <para>This builds the module <b>once</b>, under a lock, and hands each importer its own <see cref="PyModule"/> holding a shallow
/// copy of the namespace: the shared classes and functions are the same objects, but assigning an attribute on the module in one
/// interpreter (<c>pd.x = 1</c>) stays private to that interpreter.</para>
/// </summary>
public sealed class SharedModule
{
    private readonly Func<PyModule> _build;
    private readonly object _gate;
    private PyModule? _template;

    /// <param name="build">Builds the module contents (called at most once successfully).</param>
    /// <param name="gate">Lock shared with the other modules of the same binding when their builders touch the same static
    /// classes, so that they are never built concurrently with each other; default: a lock of its own.</param>
    public SharedModule(Func<PyModule> build, object? gate = null)
    {
        _build = build;
        _gate = gate ?? new object();
    }

    /// <summary>A fresh module object for one importer; the first call builds the shared contents.</summary>
    public PyModule Create()
    {
        PyModule template;
        lock (_gate) template = _template ??= _build();
        var m = new PyModule(template.Name) { Builtins = template.Builtins, FileName = template.FileName };
        foreach (var e in template.Dict.Entries) m.Dict[e.Key] = e.Value;
        return m;
    }
}


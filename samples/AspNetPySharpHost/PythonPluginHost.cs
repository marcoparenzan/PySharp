// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using PySharpLib;
using PySharpLib.Runtime;

namespace AspNetPySharpHost;

/// <summary>
/// A small, real "Python plugin" host: each named plugin is a real <c>.py</c> file under
/// <c>plugins/</c>, executed once (defining its top-level functions) and cached — a request handler
/// then calls a named function in that already-loaded module directly, no re-parsing per request.
/// Arguments are marshalled host → Python via <see cref="ClrMarshal.ToPython"/> (the same machinery
/// every other embedding scenario already uses); the Python return value is marshalled back to a
/// plain, JSON-serializable .NET object graph via <see cref="ClrMarshal.ToPlainObject"/>.
///
/// One shared <see cref="PyEngine"/>/<see cref="Interp"/> for the whole host: plugin modules are
/// independent Python namespaces (each its own <c>__main__</c>-shaped <see cref="PyModule"/>), so
/// there's no cross-plugin state leakage from sharing the underlying interpreter.
/// </summary>
public sealed class PythonPluginHost
{
    private readonly string _pluginsDirectory;
    private readonly PyEngine _engine = new();
    private readonly Dictionary<string, PyModule> _loaded = new();
    private readonly object _lock = new();

    public PythonPluginHost(string pluginsDirectory) => _pluginsDirectory = pluginsDirectory;

    /// <summary>Calls <c>functionName</c> inside the named plugin module (loading/executing the
    /// plugin's <c>.py</c> file on first use), marshalling <paramref name="args"/> in and the
    /// return value back out to a plain .NET object graph. A real Python exception surfaces as a
    /// real <see cref="PyRaise"/> — callers decide how to translate that into an HTTP response.</summary>
    public object? Invoke(string pluginName, string functionName, params object?[] args)
    {
        var module = GetOrLoadModule(pluginName);
        if (!module.Dict.TryGet(functionName, out var fn))
            throw new InvalidOperationException($"plugin '{pluginName}' has no function '{functionName}'");

        var pyArgs = args.Select(ClrMarshal.ToPython).ToArray();
        object result = _engine.Interp.Call(fn, pyArgs);
        return ClrMarshal.ToPlainObject(result);
    }

    /// <summary>Drops the cached module so the next <see cref="Invoke"/> call re-reads and
    /// re-executes the plugin's <c>.py</c> file from disk — a real, observable hot-reload with no
    /// host restart, the actual point of "Python as a scripting/plugin layer" (ROADMAP.md scenario
    /// 11).</summary>
    public void Reload(string pluginName)
    {
        lock (_lock)
            _loaded.Remove(pluginName);
    }

    private PyModule GetOrLoadModule(string pluginName)
    {
        lock (_lock)
        {
            if (_loaded.TryGetValue(pluginName, out var cached))
                return cached;

            string path = Path.Combine(_pluginsDirectory, pluginName + ".py");
            if (!File.Exists(path))
                throw new FileNotFoundException($"no such plugin: '{pluginName}'", path);

            var module = _engine.Run(File.ReadAllText(path), path);
            _loaded[pluginName] = module;
            return module;
        }
    }
}

// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using PySharpLib.Importing;
using PySharpLib.Runtime;

namespace PySharpLib.Modules;

/// <summary>
/// clr: load .NET assemblies at run time and import their namespaces, in the style of pythonnet.
/// <code>
/// import clr
/// clr.AddReference(os.path.join(os.path.dirname(__file__), "Screen9.dll"))   # or an assembly name
/// from Screen9 import PCCanvas
/// from System import Random, TimeSpan                                         # core library: always importable
/// </code>
/// A namespace becomes a module whose attributes are its public types (generic types under their name without
/// the arity suffix when it is unambiguous). Assemblies that the referenced one depends on are resolved from
/// the folders of the referenced files, so a package can ship its .NET dependencies next to its DLL.
/// </summary>
public static class ClrModule
{
    /// <summary>Namespace → assemblies that contribute public types to it, per importer (one per engine).</summary>
    private sealed class Registry
    {
        public readonly Dictionary<string, List<Assembly>> Namespaces = new(StringComparer.Ordinal);
        public readonly HashSet<Assembly> Assemblies = [];
    }

    private static readonly ConditionalWeakTable<Importer, Registry> Registries = new();
    private static readonly List<string> ProbeFolders = [];
    private static bool resolverInstalled;

    public static void Register(Importer importer)
    {
        importer.RegisterBuiltin("clr", _ => Create(importer));
        Add(importer, typeof(object).Assembly); // System, System.Collections.Generic, ... importable even before `import clr`
    }

    private static PyModule Create(Importer importer)
    {
        var m = new PyModule("clr");
        m.Dict["AddReference"] = new PyBuiltinFunction("AddReference", (_, args, _) =>
        {
            if (args.Length != 1 || args[0] is not string reference)
                throw PyErr.TypeError("AddReference() takes an assembly path or name");
            var assembly = Load(reference);
            Add(importer, assembly);
            return new ClrObject(assembly);
        });
        m.Dict["GetClrType"] = new PyBuiltinFunction("GetClrType", (_, args, _) =>
            args.Length == 1 && args[0] is ClrType type
                ? new ClrObject(type.Type)
                : throw PyErr.TypeError("GetClrType() takes a .NET type"));
        return m;
    }

    /// <summary>Loads by path (a *.dll file) or by assembly name; an assembly already loaded with the same name is reused.</summary>
    private static Assembly Load(string reference)
    {
        bool isPath = reference.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || File.Exists(reference);
        if (!isPath)
        {
            try { return Assembly.Load(new AssemblyName(reference)); }
            catch (Exception e) when (e is FileNotFoundException or FileLoadException or BadImageFormatException)
            { throw PyErr.Raise(PyErr.FileNotFoundErrorClass, $"Unable to find assembly '{reference}'."); }
        }
        string path = Path.GetFullPath(reference);
        if (!File.Exists(path))
            throw PyErr.Raise(PyErr.FileNotFoundErrorClass, $"Unable to find assembly '{path}'.");
        lock (ProbeFolders)
        {
            string folder = Path.GetDirectoryName(path)!;
            if (!ProbeFolders.Contains(folder, StringComparer.OrdinalIgnoreCase)) ProbeFolders.Add(folder);
            if (!resolverInstalled) { AssemblyLoadContext.Default.Resolving += Resolve; resolverInstalled = true; }
        }
        var name = AssemblyName.GetAssemblyName(path);
        var loaded = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => AssemblyName.ReferenceMatchesDefinition(a.GetName(), name));
        try { return loaded ?? AssemblyLoadContext.Default.LoadFromAssemblyPath(path); }
        catch (Exception e) when (e is FileLoadException or BadImageFormatException)
        { throw PyErr.Raise(PyErr.ImportErrorClass, $"Cannot load assembly '{path}': {e.Message}"); }
    }

    private static Assembly? Resolve(AssemblyLoadContext context, AssemblyName name)
    {
        string[] folders;
        lock (ProbeFolders) folders = ProbeFolders.ToArray();
        foreach (var folder in folders)
        {
            string candidate = Path.Combine(folder, name.Name + ".dll");
            if (File.Exists(candidate)) return context.LoadFromAssemblyPath(candidate);
        }
        return null;
    }

    /// <summary>Makes the assembly's namespaces importable; modules already imported gain the new types.</summary>
    private static void Add(Importer importer, Assembly assembly)
    {
        var registry = Registries.GetOrCreateValue(importer);
        lock (registry)
        {
            if (!registry.Assemblies.Add(assembly)) return;
            foreach (var ns in PublicTypes(assembly).Select(t => t.Namespace).OfType<string>().Distinct())
            {
                // every prefix is a module too: `import System.Collections.Generic` loads System, System.Collections, ...
                var parts = ns.Split('.');
                for (int i = 1; i <= parts.Length; i++)
                {
                    string prefix = string.Join('.', parts[..i]);
                    if (!registry.Namespaces.TryGetValue(prefix, out var contributors))
                    {
                        registry.Namespaces[prefix] = contributors = [];
                        importer.RegisterBuiltin(prefix, _ => NamespaceModule(registry, prefix));
                    }
                    if (i == parts.Length && !contributors.Contains(assembly)) contributors.Add(assembly);
                }
                if (importer.Modules.TryGet(ns, out var cached) && cached is PyModule module) Fill(module, assembly, ns);
            }
        }
    }

    private static PyModule NamespaceModule(Registry registry, string ns)
    {
        var module = new PyModule(ns);
        lock (registry)
            foreach (var assembly in registry.Namespaces[ns])
                Fill(module, assembly, ns);
        return module;
    }

    private static void Fill(PyModule module, Assembly assembly, string ns)
    {
        var types = PublicTypes(assembly).Where(t => t.Namespace == ns).ToArray();
        foreach (var type in types)
        {
            int tick = type.Name.IndexOf('`');
            string name = tick < 0 ? type.Name : type.Name[..tick];
            // List`1 is importable as List unless a non-generic List exists (then only as List`1).
            if (tick >= 0 && types.Any(other => other != type && other.Name == name)) name = type.Name;
            if (tick >= 0 && module.Dict.TryGet(name, out _)) continue;
            module.Dict[name] = new ClrType(type);
        }
    }

    private static IEnumerable<Type> PublicTypes(Assembly assembly)
    {
        try { return assembly.GetExportedTypes().Where(t => !t.IsNested); }
        catch (ReflectionTypeLoadException e) { return e.Types.OfType<Type>().Where(t => t.IsPublic); }
        catch (NotSupportedException) { return []; }
    }
}

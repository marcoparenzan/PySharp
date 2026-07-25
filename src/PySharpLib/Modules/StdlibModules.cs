// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using PySharpLib.Importing;
using PySharpLib.Runtime;

namespace PySharpLib.Modules;

/// <summary>Registers all the stdlib modules implemented in C#.</summary>
public static class StdlibModules
{
    public static void RegisterAll(Importer importer)
    {
        importer.RegisterBuiltin("__future__", _ =>
        {
            // stub: the future features are already the default behavior
            var m = new Runtime.PyModule("__future__");
            foreach (var feature in new[] { "annotations", "generator_stop", "division", "print_function" })
                m.Dict[feature] = new Runtime.PyClass(feature, new List<PyClass>());
            return m;
        });
        importer.RegisterBuiltin("sys", interp => SysModule.Create(interp, importer));
        importer.RegisterBuiltin("time", _ => TimeModule.Create());
        importer.RegisterBuiltin("os", _ => OsModule.Create());
        importer.RegisterBuiltin("errno", _ => MiscModules.CreateErrno());
        importer.RegisterBuiltin("platform", _ => MiscModules.CreatePlatform());
        importer.RegisterBuiltin("string", _ => MiscModules.CreateString());
        importer.RegisterBuiltin("uuid", _ => MiscModules.CreateUuid());
        importer.RegisterBuiltin("warnings", _ => MiscModules.CreateWarnings());
        importer.RegisterBuiltin("typing", _ => MiscModules.CreateTyping());
        importer.RegisterBuiltin("dataclasses", _ => MiscModules.CreateDataclasses());
        importer.RegisterBuiltin("copy", _ => MiscModules.CreateCopy());
        importer.RegisterBuiltin("functools", _ => FunctoolsModule.Create());
        importer.RegisterBuiltin("struct", _ => StructModule.Create());
        importer.RegisterBuiltin("hashlib", _ => CryptoModules.CreateHashlib());
        importer.RegisterBuiltin("hmac", _ => CryptoModules.CreateHmac());
        importer.RegisterBuiltin("base64", _ => CryptoModules.CreateBase64());
        importer.RegisterBuiltin("urllib", _ => UrllibModule.Create());
        importer.RegisterBuiltin("urllib.parse", _ => UrllibModule.CreateParse());
        importer.RegisterBuiltin("urllib.request", _ => UrllibModule.CreateRequest());
        importer.RegisterBuiltin("json", _ => JsonModule.Create());
        importer.RegisterBuiltin("yaml", _ => YamlModule.Create());
        importer.RegisterBuiltin("collections", _ => CollectionsModule.Create());
        importer.RegisterBuiltin("threading", _ => ThreadingModule.Create());
        importer.RegisterBuiltin("socket", _ => SocketModule.Create());
        importer.RegisterBuiltin("ssl", _ => SslModule.Create());
        importer.RegisterBuiltin("select", _ => SelectModule.Create());
        importer.RegisterBuiltin("enum", _ => EnumModule.Create());
        importer.RegisterBuiltin("logging", _ => LoggingModule.Create());
        importer.RegisterBuiltin("ctypes", _ => CtypesModule.Create());
        importer.RegisterBuiltin("math", _ => MathModule.Create());
        importer.RegisterBuiltin("io", _ => IoModule.Create());
        importer.RegisterBuiltin("asyncio", _ => AsyncioModule.Create());
    }
}

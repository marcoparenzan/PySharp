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
        importer.RegisterBuiltin("pathlib", _ => PathlibModule.Create());
        importer.RegisterBuiltin("errno", _ => MiscModules.CreateErrno());
        importer.RegisterBuiltin("platform", _ => MiscModules.CreatePlatform());
        importer.RegisterBuiltin("string", _ => MiscModules.CreateString());
        importer.RegisterBuiltin("uuid", _ => MiscModules.CreateUuid());
        importer.RegisterBuiltin("warnings", _ => MiscModules.CreateWarnings());
        importer.RegisterBuiltin("keyword", _ => MiscModules.CreateKeyword());
        importer.RegisterBuiltin("typing", interp => MiscModules.CreateTyping(interp));
        importer.RegisterBuiltin("types", _ => MiscModules.CreateTypes());
        importer.RegisterBuiltin("dataclasses", _ => MiscModules.CreateDataclasses());
        importer.RegisterBuiltin("copy", _ => MiscModules.CreateCopy());
        importer.RegisterBuiltin("functools", _ => FunctoolsModule.Create());
        importer.RegisterBuiltin("operator", _ => OperatorModule.Create());
        importer.RegisterBuiltin("struct", _ => StructModule.Create());
        importer.RegisterBuiltin("hashlib", _ => CryptoModules.CreateHashlib());
        importer.RegisterBuiltin("hmac", _ => CryptoModules.CreateHmac());
        importer.RegisterBuiltin("base64", _ => CryptoModules.CreateBase64());
        importer.RegisterBuiltin("binascii", _ => BinasciiModule.Create());
        importer.RegisterBuiltin("urllib", _ => UrllibModule.Create());
        importer.RegisterBuiltin("urllib.parse", _ => UrllibModule.CreateParse());
        importer.RegisterBuiltin("urllib.request", _ => UrllibModule.CreateRequest());
        importer.RegisterBuiltin("json", _ => JsonModule.Create());
        importer.RegisterBuiltin("yaml", _ => YamlModule.Create());
        importer.RegisterBuiltin("itertools", _ => ItertoolsModule.Create());
        importer.RegisterBuiltin("weakref", _ => WeakrefModule.Create());
        importer.RegisterBuiltin("collections", _ => CollectionsModule.Create());
        importer.RegisterBuiltin("collections.abc", _ => CollectionsModule.CreateAbc());
        importer.RegisterBuiltin("threading", _ => ThreadingModule.Create());
        importer.RegisterBuiltin("socket", interp => SocketModule.Create(interp));
        importer.RegisterBuiltin("ssl", _ => SslModule.Create());
        importer.RegisterBuiltin("select", _ => SelectModule.Create());
        importer.RegisterBuiltin("enum", _ => EnumModule.Create());
        importer.RegisterBuiltin("logging", _ => LoggingModule.Create());
        importer.RegisterBuiltin("ctypes", _ => CtypesModule.Create());
        importer.RegisterBuiltin("math", _ => MathModule.Create());
        importer.RegisterBuiltin("decimal", _ => DecimalModule.Create());
        importer.RegisterBuiltin("datetime", _ => DateTimeModule.Create());
        importer.RegisterBuiltin("ipaddress", _ => IpAddressModule.Create());
        importer.RegisterBuiltin("re", _ => ReModule.Create());
        importer.RegisterBuiltin("colorsys", _ => ColorSysModule.Create());
        importer.RegisterBuiltin("shlex", _ => ShlexModule.Create());
        importer.RegisterBuiltin("contextvars", _ => ContextVarsModule.Create());
        importer.RegisterBuiltin("importlib", _ => ImportlibModule.Create(importer));
        importer.RegisterBuiltin("importlib.util", _ => ImportlibModule.CreateUtil(importer));
        importer.RegisterBuiltin("textwrap", _ => TextwrapModule.Create());
        importer.RegisterBuiltin("signal", interp => SignalModule.Create(interp));
        importer.RegisterBuiltin("concurrent", _ => ConcurrentModule.Create());
        importer.RegisterBuiltin("concurrent.futures", _ => ConcurrentModule.CreateFutures());
        importer.RegisterBuiltin("stat", _ => StatModule.Create());
        importer.RegisterBuiltin("subprocess", interp => SubprocessModule.Create(interp));
        importer.RegisterBuiltin("tempfile", _ => TempfileModule.Create());
        importer.RegisterBuiltin("http", interp => HttpModule.Create(interp));
        importer.RegisterBuiltin("http.client", _ => HttpModule.CreateClient());
        importer.RegisterBuiltin("http.cookies", interp => CookiesModule.Create(interp));
        importer.RegisterBuiltin("html", _ => HtmlModule.Create());
        importer.RegisterBuiltin("traceback", interp => TracebackModule.Create(interp));
        importer.RegisterBuiltin("email", _ => EmailModule.Create());
        importer.RegisterBuiltin("email.utils", _ => EmailModule.CreateUtils());
        importer.RegisterBuiltin("email.message", _ => EmailModule.CreateMessage());
        importer.RegisterBuiltin("mimetypes", _ => MimetypesModule.Create());
        importer.RegisterBuiltin("secrets", _ => SecretsModule.Create());
        importer.RegisterBuiltin("array", _ => ArrayModule.Create());
        importer.RegisterBuiltin("queue", _ => QueueModule.Create());
        importer.RegisterBuiltin("pickle", _ => PickleModule.Create());
        importer.RegisterBuiltin("io", _ => IoModule.Create());
        importer.RegisterBuiltin("asyncio", interp => AsyncioModule.Create(interp));
        importer.RegisterBuiltin("asyncio.base_events", _ => AsyncioModule.CreateBaseEvents());
        importer.RegisterBuiltin("contextlib", _ => ContextlibModule.Create());
        importer.RegisterBuiltin("abc", _ => AbcModule.Create());
        importer.RegisterBuiltin("inspect", _ => InspectModule.Create());
        // `import builtins` gets the same module every name implicitly falls back to.
        importer.RegisterBuiltin("builtins", _ => importer.BuiltinsModule);
    }
}

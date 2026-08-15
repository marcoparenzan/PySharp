# Copyright (c) 2026 Marco Parenzan
#
# Licensed under the MIT License. See the LICENSE file in the project
# root for full license information.

# A tiny real Python "plugin": the ASP.NET Core host calls run(name) per request and serializes
# whatever it returns straight to JSON. Nothing about this plugin is aware it's running inside a
# .NET web host -- it's a plain Python function, editable and hot-reloadable without recompiling
# or restarting the C# side (see PythonPluginHost.Reload in Program.cs).

import datetime


def run(name: str) -> dict:
    if not name:
        raise ValueError("name must not be empty")
    return {
        "message": f"Hello, {name}! (computed by a real Python plugin)",
        "shout": name.upper(),
        "length": len(name),
        "server_time": datetime.datetime.now().isoformat(),
    }

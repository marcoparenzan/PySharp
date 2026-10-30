# PySharp for VS Code

Write and run Python on **PySharp**, the Python 3 interpreter written in C# (no CPython). Part of the
ScienceSharp suite. MIT.

## Requirements

The `pysharp` .NET global tool (.NET 11):

```powershell
dotnet tool install --global PySharp --add-source D:\Dev\NuGetLocalFeed --prerelease
# or, for the whole suite:  D:\Dev\Science\ScienceSharp\Install-Suite.ps1 -Tools
```

If the command cannot be started, the status bar says "PySharp not found" and shows this command.

## What it does

| Feature | How |
| --- | --- |
| Run the open file | **PySharp: Run File**, the play button in the editor title bar, or the status bar item. Runs `pysharp run <file>` as a VS Code task; the output stays in the Terminal panel. |
| Run a selection or the current line | **PySharp: Run Selection or Line** (also in the editor context menu). The text is written to a temporary script and run with the file's folder as working directory. |
| Interactive session | **PySharp: Start REPL** opens `pysharp repl` in a terminal. |
| Install a package | **PySharp: Install Package...** runs `pysharp install <name>` into the workspace folder's `site-packages` (pure-Python packages only). |
| Version check | The status bar shows `PySharp <version>` when a `.py` or PySharp file is open. **PySharp: Show Version** shows it on demand. |
| Language | `.pys` files are the "PySharp" language, with Python syntax highlighting. `.py` files keep whatever Python extension you use; the PySharp commands work on both. |

Notebooks are not part of this extension: the JupyterSharp notebook extension runs `pysharp` cells.

## Settings

| Setting | Default | Meaning |
| --- | --- | --- |
| `pysharp.executablePath` | `pysharp` | The command to run. |
| `pysharp.runArguments` | `[]` | Extra arguments after the file name (become `sys.argv[1:]`). |
| `pysharp.showStatusBar` | `true` | Show the version item. |

## Limits

- **Not an interpreter for the Python extension.** The official Python extension (ms-python) expects a real CPython it can inspect, so it
  cannot use PySharp. This extension runs PySharp beside it.
- **Selection snippets cannot import sibling modules**, because `pysharp run` puts the temporary script's folder on `sys.path`. Use *Run File* for that.
- **No debugger, no IntelliSense, no diagnostics yet.** Errors appear in the terminal output as PySharp prints them; they are not underlined in the editor.
- PySharp covers part of Python 3; see the language notes in the PySharp README.
- Only tested on Windows with the .NET 11 SDK. The extension itself is plain JavaScript and is not platform-specific.
- The extension was unit-tested (the command-line building and the text clean-up) but has not been exercised by clicking through VS Code.

## Build

```powershell
cd D:\Dev\Science\PySharp\vscode-extension
npm install
npm test                       # compiles and runs the unit tests
npx --yes @vscode/vsce package # pysharp-0.1.0.vsix
code --install-extension pysharp-0.1.0.vsix
```

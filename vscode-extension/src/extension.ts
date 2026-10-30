import * as cp from "child_process";
import * as fs from "fs";
import * as os from "os";
import * as path from "path";
import * as vscode from "vscode";
import { dedent, installArgs, isBlank, parseVersion, runArgs } from "./tool";

let statusBar: vscode.StatusBarItem;
let tempDir: string | undefined;

function config() { return vscode.workspace.getConfiguration("pysharp"); }
function exe(): string { return config().get<string>("executablePath", "pysharp") || "pysharp"; }

const INSTALL_HELP = "Install it with: dotnet tool install --global PySharp --add-source D:\\Dev\\NuGetLocalFeed --prerelease";

/** Runs `pysharp --version`; undefined when the command cannot be started. */
function readVersion(): Promise<string | undefined> {
    return new Promise(resolve => {
        cp.execFile(exe(), ["--version"], { timeout: 15000, windowsHide: true }, (error, stdout) =>
            resolve(error ? undefined : parseVersion(stdout)));
    });
}

async function refreshStatusBar(): Promise<void> {
    const editor = vscode.window.activeTextEditor;
    const relevant = editor && ["python", "pysharp"].includes(editor.document.languageId);
    if (!config().get<boolean>("showStatusBar", true) || !relevant) { statusBar.hide(); return; }
    const version = await readVersion();
    if (version) {
        statusBar.text = `$(play) PySharp ${version}`;
        statusBar.tooltip = "PySharp: click to run the current file";
        statusBar.command = "pysharp.runFile";
    } else {
        statusBar.text = "$(warning) PySharp not found";
        statusBar.tooltip = `The command '${exe()}' could not be started. ${INSTALL_HELP}`;
        statusBar.command = "pysharp.showVersion";
    }
    statusBar.show();
}

/** Runs pysharp as a VS Code task. ProcessExecution passes each argument as is, so paths with spaces need no quoting. */
async function runTask(label: string, args: string[], cwd: string | undefined): Promise<void> {
    const task = new vscode.Task(
        { type: "pysharp" }, vscode.TaskScope.Workspace, label, "PySharp",
        new vscode.ProcessExecution(exe(), args, { cwd }));
    task.presentationOptions = { reveal: vscode.TaskRevealKind.Always, panel: vscode.TaskPanelKind.Shared, clear: true, focus: false };
    await vscode.tasks.executeTask(task);
}

function activeCwd(editor: vscode.TextEditor): string | undefined {
    const doc = editor.document;
    if (doc.uri.scheme === "file") return path.dirname(doc.uri.fsPath);
    return vscode.workspace.workspaceFolders?.[0]?.uri.fsPath;
}

async function runFile(): Promise<void> {
    const editor = vscode.window.activeTextEditor;
    if (!editor) { void vscode.window.showWarningMessage("PySharp: open a Python file first."); return; }
    const doc = editor.document;
    const extra = config().get<string[]>("runArguments", []);
    if (doc.uri.scheme !== "file") { await runText(doc.getText(), "untitled", activeCwd(editor)); return; }
    if (doc.isDirty && !(await doc.save())) return;
    await runTask(`run ${path.basename(doc.fileName)}`, runArgs(doc.fileName, extra), activeCwd(editor));
}

async function runSelection(): Promise<void> {
    const editor = vscode.window.activeTextEditor;
    if (!editor) { void vscode.window.showWarningMessage("PySharp: open a Python file first."); return; }
    const selection = editor.selection;
    const text = selection.isEmpty ? editor.document.lineAt(selection.active.line).text : editor.document.getText(selection);
    await runText(text, selection.isEmpty ? `line ${selection.active.line + 1}` : "selection", activeCwd(editor));
}

/** Writes the text to a temporary script and runs it, because `pysharp run` takes a file. */
async function runText(text: string, label: string, cwd: string | undefined): Promise<void> {
    if (isBlank(text)) { void vscode.window.showInformationMessage("PySharp: nothing to run."); return; }
    tempDir ??= fs.mkdtempSync(path.join(os.tmpdir(), "pysharp-"));
    const file = path.join(tempDir, "snippet.py");
    fs.writeFileSync(file, dedent(text), "utf8");
    // `pysharp run` puts the script's folder on sys.path, which here is the temp folder, so a snippet cannot import
    // modules that sit next to the edited file. The working directory is still the file's folder (relative paths and
    // ./site-packages work). Use "Run File" when the code needs sibling modules.
    await runTask(`run ${label}`, runArgs(file), cwd);
}

function startRepl(): void {
    const terminal = vscode.window.createTerminal({
        name: "PySharp REPL",
        shellPath: exe(),
        shellArgs: ["repl"],
        cwd: vscode.window.activeTextEditor ? activeCwd(vscode.window.activeTextEditor) : vscode.workspace.workspaceFolders?.[0]?.uri.fsPath,
    });
    terminal.show();
}

async function installPackage(): Promise<void> {
    const requirement = await vscode.window.showInputBox({
        title: "PySharp: install a pure-Python package",
        prompt: "PyPI name (optionally ==version), path to a .whl file, or a package name (use --find-links through the terminal for folders)",
        placeHolder: "paho-mqtt==2.1.0",
        validateInput: value => { try { installArgs(value); return undefined; } catch (e) { return (e as Error).message; } },
    });
    if (requirement === undefined) return;
    const folder = vscode.workspace.workspaceFolders?.[0]?.uri.fsPath
        ?? (vscode.window.activeTextEditor ? activeCwd(vscode.window.activeTextEditor) : undefined);
    if (!folder) { void vscode.window.showWarningMessage("PySharp: open a folder first; packages are installed into its site-packages."); return; }
    await runTask(`install ${requirement.trim()}`, installArgs(requirement), folder);
}

async function showVersion(): Promise<void> {
    const version = await readVersion();
    if (version) void vscode.window.showInformationMessage(`PySharp ${version} (${exe()})`);
    else void vscode.window.showErrorMessage(`PySharp: the command '${exe()}' could not be started. ${INSTALL_HELP}`);
}

export function activate(context: vscode.ExtensionContext): void {
    statusBar = vscode.window.createStatusBarItem(vscode.StatusBarAlignment.Right, 90);
    context.subscriptions.push(
        statusBar,
        vscode.commands.registerCommand("pysharp.runFile", runFile),
        vscode.commands.registerCommand("pysharp.runSelection", runSelection),
        vscode.commands.registerCommand("pysharp.startRepl", startRepl),
        vscode.commands.registerCommand("pysharp.installPackage", installPackage),
        vscode.commands.registerCommand("pysharp.showVersion", showVersion),
        vscode.window.onDidChangeActiveTextEditor(() => void refreshStatusBar()),
        vscode.workspace.onDidChangeConfiguration(e => { if (e.affectsConfiguration("pysharp")) void refreshStatusBar(); }),
    );
    void refreshStatusBar();
}

export function deactivate(): void {
    if (tempDir) { try { fs.rmSync(tempDir, { recursive: true, force: true }); } catch { /* best effort */ } }
}

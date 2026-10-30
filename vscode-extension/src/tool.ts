// Pure helpers for calling the pysharp command line. No vscode import, so node's test runner can load them.

/** Arguments of `pysharp run <file> [script args…]`. The file name is passed as one argument: no shell quoting is involved. */
export function runArgs(file: string, scriptArgs: readonly string[] = []): string[] {
    if (!file) throw new Error("A file name is required.");
    return ["run", file, ...scriptArgs];
}

/** Arguments of `pysharp install <requirement>`; a requirement is a PyPI name (optionally ==version), a .whl path or a folder. */
export function installArgs(requirement: string): string[] {
    const text = requirement.trim();
    if (!text) throw new Error("A package name is required.");
    if (/^-/.test(text)) throw new Error("A package name cannot start with '-'.");
    return ["install", text];
}

/** Extracts the version from the output of `pysharp --version` (for example "2.5.1-net11-preview.4"). */
export function parseVersion(output: string): string | undefined {
    const match = /\d+\.\d+\.\d+(?:\.\d+)?(?:-[0-9A-Za-z.-]+)?/.exec(output);
    return match?.[0];
}

/**
 * The text to run for "Run Selection or Line". A selection is run as it is; with no selection the line under the cursor is run.
 * Common leading indentation is removed so that an indented block copied from inside a function still parses.
 */
export function dedent(text: string): string {
    const lines = text.replace(/\r\n/g, "\n").split("\n");
    const indents = lines.filter(l => l.trim().length > 0).map(l => /^[ \t]*/.exec(l)![0].length);
    const cut = indents.length ? Math.min(...indents) : 0;
    return lines.map(l => l.slice(Math.min(cut, /^[ \t]*/.exec(l)![0].length))).join("\n").replace(/\s+$/, "") + "\n";
}

/** True when the text is empty or only whitespace, so there is nothing to run. */
export function isBlank(text: string): boolean {
    return text.trim().length === 0;
}

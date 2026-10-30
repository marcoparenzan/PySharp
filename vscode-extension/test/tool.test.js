// Run with: npm test   (compiles first; uses node's built-in test runner, no extra packages)
const test = require("node:test");
const assert = require("node:assert/strict");
const { runArgs, installArgs, parseVersion, dedent, isBlank } = require("../out/tool.js");

test("runArgs passes the file as one argument, even with spaces", () => {
    assert.deepEqual(runArgs("C:\\My Scripts\\a b.py"), ["run", "C:\\My Scripts\\a b.py"]);
});

test("runArgs appends script arguments after the file", () => {
    assert.deepEqual(runArgs("x.py", ["a", "b c"]), ["run", "x.py", "a", "b c"]);
});

test("runArgs rejects an empty file name", () => {
    assert.throws(() => runArgs(""), /file name/);
});

test("installArgs trims and rejects empty or option-like input", () => {
    assert.deepEqual(installArgs("  paho-mqtt==2.1.0 "), ["install", "paho-mqtt==2.1.0"]);
    assert.throws(() => installArgs("   "), /package name/);
    assert.throws(() => installArgs("--find-links x"), /cannot start/);
});

test("parseVersion reads release and preview versions", () => {
    assert.equal(parseVersion("2.5.1-net11-preview.4\n"), "2.5.1-net11-preview.4");
    assert.equal(parseVersion("PySharp 2.5.0"), "2.5.0");
    assert.equal(parseVersion("no version here"), undefined);
});

test("dedent removes common indentation and keeps relative indentation", () => {
    assert.equal(dedent("    if x:\n        y = 1\n    z = 2"), "if x:\n    y = 1\nz = 2\n");
});

test("dedent ignores blank lines, normalises CRLF and handles a single line", () => {
    assert.equal(dedent("  a = 1\r\n\r\n  b = 2\r\n"), "a = 1\n\nb = 2\n");
    assert.equal(dedent("\tprint(1)"), "print(1)\n");
});

test("isBlank", () => {
    assert.equal(isBlank(" \n\t "), true);
    assert.equal(isBlank("pass"), false);
});

# PySharp — Open points

Suite-wide points are in [../ScienceSharp/OPENPOINTS.md](../ScienceSharp/OPENPOINTS.md). Older work lists: [TODO.md](TODO.md), [ROADMAP.md](ROADMAP.md).

| # | Point | Status |
| --- | --- | --- |
| P-17 | Task.cancelling/uncancel andpre-start cancellation fixed fordeclaredPython3.12 (1546passed/13skipped,5newtests);FastAPI/anyio restored. 3.13+pending-cancel rescission,completeasyncio semantics andpost-changegate notclaimed yet. | verified scoped fix |
| P-18 | **numpy reductions on the shared kernels (common-core step 2)**, are now in NDSharp (`KernelPath`, which references `MathemaSharp.Kernels`; PySharp.Numpy just calls NDSharp), cover every real dtype including float16, any axes, any layout, `dtype=`, and also `argmax`/`argmin`, `any`/`all`, `count_nonzero`, `diff`, `median`, `quantile` and the whole `nan*` family. Open: `ptp`, `cov`, `sort` and the rest still run only in the NDSharp engine; the equivalence tests compare with NDSharp, not with CPython/NumPy (not installed here). | open |
| P-16 | Common-core step 0: NumpyArrayViews host adapter implemented and unit/in-interpreter tested. Source NDSharp dependency replaces numpy package pin; 28 dtype byte checks and Mat/Python storage round-trip passed; complete ecosystem gate and cross-language arithmetic corpus pending. Shared writes require synchronization; Base/Tag identity not transferred. | partial |
| P-1 | **VS Code extension** `marcoparenzan.pysharp` 0.1.0 is built and installed (run file, selection, REPL, install package, status bar). Open: diagnostics from `pysharp` errors, IntelliSense, debugger, and a click-through test in VS Code (only the command-building code is unit-tested, 8 tests). | partly done |
| P-2 | The kernel plugin `JupyterSharp.Kernels.PySharp` is not packed into the feed, and references `JupyterSharp.Kernels.Abstractions` `0.3.1-net11-preview.2` while the host is at `.3`. | open |
| P-3 | `pysharp` was updated globally from 2.5.0 to `2.5.1-net11-preview.4` on 2026-10-30. Note: `pysharp --version` prints the assembly version `2.5.1.0`, not the package version. | done |
| P-4 | libtorch and OpenCV natives are no longer bundled in the VS Code extension (`PySharpExternalNatives=true` in `JupyterSharp.Kernels.PySharp.csproj`); `ScienceSharp\Install-Natives.ps1` restores them into the NuGet cache. The default build (the gate's) still bundles them. | done on Windows |
| P-5 | The repo has uncommitted changes (renames, deletions in `NDSharp.Frame.Tests`, README and ROADMAP edits). Needs a commit plan. | open |
| P-6 | 13 tests are skipped: 12 need a PostgreSQL server, 1 downloads pretrained weights. Skips are reported, not counted as passed. | accepted |
| P-7 | `README.md` still has a long block of integration notes at the top (the "Science source-plot integration profile"). It should move to ARCHITECTURE.md. | open |
| P-8 | The README and several plan files still say "JupyterNet" (historical). The current name is JupyterSharp. | open |
| P-9 | `ctypes` on system DLLs works on Windows only. Linux and macOS are untested for the whole interpreter. | open |
| P-10 | Language gaps are tracked in the `Xfail` corpus (`PySharp.Tests/Corpus/CorpusTests.cs`) and in [ROADMAP.md](ROADMAP.md). | tracked |
| P-11 | `config.json` with a device key must stay out of git (`.gitignore`); end-to-end X.509 auth test not done. | open |
| P-12 | Extract `PySharpLib` as a standalone NuGet library and verify it pulls nothing from the host (from TODO.md). | open |
| P-13 | On first `import torch`, TorchSharp copies about 250 MB of native files from the NuGet cache into a `cpu` folder next to its `TorchSharp.dll`, which is inside the extension folder. That is TorchSharp's own mechanism (its "consolidate native binaries" step); it does not change the package size but doubles the disk use per installed extension version. | known |
| P-14 | OpenCV natives exist only for Windows: `NDSharp.Image` references only `OpenCvSharp4.runtime.win`. On Linux and macOS `import cv2` cannot work, and the kernel says so instead of asking for an install. | open |
| P-15 | The package versions in `ScienceSharp\Install-Natives.ps1` (libtorch-cpu 2.10.0, TorchSharp 0.107.0, OpenCvSharp4.runtime.win 4.11.0.20250507) are written by hand and must follow `PySharpLib.Torch.csproj` and `NDSharp.Image`. | open |

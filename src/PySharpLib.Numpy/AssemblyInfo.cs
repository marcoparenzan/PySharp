// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using System.Runtime.CompilerServices;

// The sibling Python bindings (cv2, matplotlib, ...) share this assembly's argument-binding and
// ndarray-wrapping helpers (Args, Conv, Native) instead of duplicating them.
[assembly: InternalsVisibleTo("PySharpLib.Cv2")]
[assembly: InternalsVisibleTo("PySharpLib.Matplotlib")]
[assembly: InternalsVisibleTo("PySharpLib.Pywt")]
[assembly: InternalsVisibleTo("PySharpLib.Torch")]

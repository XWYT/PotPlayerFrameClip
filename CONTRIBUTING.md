# Contributing

Keep changes focused and preserve these behavioral contracts:

- Menu commands must only dispatch from visible PotPlayer-owned menu windows.
- The first submenu command and nine-command layout are part of the skin hit map.
- Source media color encodings are preserved unless a conversion is explicit. Display rendering and implicit HDR-to-SDR tone mapping are not allowed in reference capture.
- Existing output, configuration, range state, and classification aliases are not deleted during upgrades.
- Fixed drive letters and machine-specific installation paths are not allowed.
- PowerShell files containing Chinese text must remain UTF-8 with BOM for Windows PowerShell 5.1 compatibility.

Run `scripts\build.ps1 -Version <major.minor.patch>` before submitting a change. UI regressions run on a separate non-input desktop; do not remove that isolation or fall back to showing test windows on the user's desktop.

A public release includes the versioned lightweight Setup EXE and its SHA-256 file. Full installers remain blocked until their exact corresponding sources, build recipes and dependency notices are archived. `dist\release` and `dist\obj` are build staging directories, not release assets.

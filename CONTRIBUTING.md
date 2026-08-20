# Contributing

Keep changes focused and preserve these behavioral contracts:

- Menu commands must only dispatch from visible PotPlayer-owned menu windows.
- The first submenu command and nine-command layout are part of the skin hit map.
- Source media color encodings are preserved unless a conversion is explicit. Display rendering and implicit HDR-to-SDR tone mapping are not allowed in reference capture.
- Existing output, configuration, range state, and classification aliases are not deleted during upgrades.
- Fixed drive letters and machine-specific installation paths are not allowed.
- PowerShell files containing Chinese text must remain UTF-8 with BOM for Windows PowerShell 5.1 compatibility.

Run `scripts\build.ps1 -Version <major.minor.patch>` before submitting a change. A release must upload only the versioned Setup EXE; `dist\release` and `dist\obj` are build staging directories.

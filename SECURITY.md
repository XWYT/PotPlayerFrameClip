# Security

Please report security issues privately to the repository owner before opening a
public issue. Include the FrameClip version, PotPlayer version and bitness,
Windows version, and a minimal reproduction without private media paths.

FrameClip only reads the current local media path, invokes the configured FFmpeg
tools, writes under the selected output root, and stores user state under
`%LOCALAPPDATA%\PotPlayerFrameClip`.


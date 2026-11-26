# winget manifests

Templates for submitting Kovan Term to [winget](https://github.com/microsoft/winget-pkgs). They point at the release's
**small** zip (`KovanTerm-<version>-win-x64-small.zip`), which needs the .NET 10 Desktop Runtime; winget installs that
first, as a dependency. The self-contained zip on the release page stays for people who don't use winget.

For each release:

1. Publish the GitHub release (the release workflow drafts it on a `v*` tag).
2. Replace `{version}` (without the `v`), `{date}` (`YYYY-MM-DD`) and `{sha256}` (from
   `KovanTerm-<version>-win-x64-small.zip.sha256` on the release) in the three files.
3. Check them: `winget validate --manifest packaging/winget`, and try them: `winget install --manifest packaging/winget`.
4. Submit a pull request to `microsoft/winget-pkgs` with the three files under
   `manifests/m/MuhammedKaradas/KovanTerm/<version>/`, or use `wingetcreate update MuhammedKaradas.KovanTerm`
   for later versions.

winget installs a zip's program as "portable": it's unpacked under `%LOCALAPPDATA%\Microsoft\WinGet\Packages` and
`kovan` starts it from a terminal.

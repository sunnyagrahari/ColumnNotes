# ColumnNotes

Local Windows notepad with **columns** and **checklists**. No account, no cloud, no telemetry.

## Download (Windows)

Latest portable build (v1.1.0): **[Releases](https://github.com/sunnyagrahari/ColumnNotes/releases/latest)**

1. Download `ColumnNotes-portable-win-x64.zip`
2. Unzip
3. Double-click **ColumnNotes.exe**

`portable.txt` next to the EXE stores settings beside the app (Notepad++-style portable mode).

## Stack

- **C# / WPF / .NET 8** (native Win32-style unpackaged EXE)
- Self-contained `win-x64` publish — no .NET runtime install on the PC
- Inno Setup script in `installer/` for `ColumnNotes-Setup.exe`

## Build

See [BUILD.md](BUILD.md).

```bat
dotnet publish native/src/ColumnNotes/ColumnNotes.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o artifacts/publish
powershell -File portable/make-portable.ps1
```

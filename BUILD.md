# Build ColumnNotes (Windows EXE)

ColumnNotes is a native WPF app (`native/src/ColumnNotes`). The live companion in this workspace is a full-fidelity editor with the same document model, shortcuts, and `.cnotes` format.

## Prerequisites

- Windows 10/11 x64
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (includes the Windows Desktop workload)
- Visual Studio 2022 (optional) **or** the `dotnet` CLI
- [Inno Setup 6](https://jrsoftware.org/isinfo.php) — only required to compile `ColumnNotes-Setup.exe`

## Portable EXE (self-contained, no runtime install)

From the repo root:

```bat
dotnet publish native/src/ColumnNotes/ColumnNotes.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o artifacts/publish
```

Equivalent path if you keep a copy at `src/ColumnNotes.csproj`:

```bat
dotnet publish src/ColumnNotes.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o artifacts/portable
```

Then stamp portable mode and zip:

```bat
powershell -File portable/make-portable.ps1
```

Result:

```
artifacts/portable/
  ColumnNotes.exe
  portable.txt
artifacts/ColumnNotes-portable-win-x64.zip
```

Double-click `ColumnNotes.exe`. Because `portable.txt` sits next to the EXE, settings, recent files, and autosave are written **beside the EXE**, not in `%APPDATA%`.

You can also start with a switch:

```bat
ColumnNotes.exe --portable
```

## Installed mode

Publish as above, then compile the Inno Setup script:

```bat
iscc installer\ColumnNotes.iss
```

That writes `artifacts\ColumnNotes-Setup.exe`.

The installer:

- Copies files to `%ProgramFiles%\ColumnNotes\`
- Adds a Start Menu shortcut
- Optional desktop shortcut
- Optional “Add to PATH”
- Associates `.cnotes`
- Registers Uninstall in Apps & Features

### Silent install

```bat
ColumnNotes-Setup.exe /VERYSILENT /NORESTART /TASKS="desktopicon,addtopath"
```

Uninstall:

```bat
ColumnNotes-Setup.exe /VERYSILENT /NORESTART
```

or use Apps & Features.

Installed-mode settings live in `%APPDATA%\ColumnNotes`.

## Visual Studio

1. Open `native/ColumnNotes.sln`
2. Set configuration to **Release**, runtime to **win-x64**
3. Build → Publish → self-contained, single file, win-x64

## How portable mode is detected

1. Command line contains `--portable`, **or**
2. A file named `portable.txt` exists in the same folder as `ColumnNotes.exe`

If either is true, `AppPaths.DataDirectory` is the EXE folder. Otherwise it is `%APPDATA%\ColumnNotes`.

## File format (`.cnotes`)

UTF-8 JSON:

```json
{
  "format": "cnotes",
  "version": 1,
  "title": "Welcome",
  "encoding": "UTF-8",
  "wordWrap": true,
  "createdAt": "2026-01-01T00:00:00.000Z",
  "modifiedAt": "2026-01-01T00:00:00.000Z",
  "plainText": "fallback concatenation of every column",
  "columns": [
    {
      "id": "uuid",
      "blocks": [
        {
          "id": "uuid",
          "type": "paragraph",
          "html": "<strong>Hello</strong>",
          "plainText": "Hello",
          "rtf": "{\\rtf1 Hello}"
        },
        {
          "id": "uuid",
          "type": "check",
          "isChecked": false,
          "html": "Milk",
          "plainText": "Milk"
        }
      ]
    }
  ]
}
```

Also opens `.txt`. Exports `.html`, `.md`, `.rtf`, `.txt`.

Reducing column count never deletes text: a Merge / Cancel dialog appends leftover columns onto the last remaining column.

## Keyboard

| Shortcut | Action |
|---|---|
| Ctrl+N | New |
| Ctrl+Shift+N | New window |
| Ctrl+O | Open |
| Ctrl+S | Save |
| Ctrl+Shift+S | Save As |
| Ctrl+P | Print |
| Ctrl+F | Find |
| Ctrl+H | Replace |
| F3 | Find next |
| Ctrl+Z / Ctrl+Y | Undo / Redo |
| Ctrl+Shift+K | Convert selected lines to checkboxes |
| Ctrl+Shift+V | Paste as plain text |
| F5 | Insert date/time |
| F11 | Full screen |
| Ctrl++ / Ctrl+- / Ctrl+0 | Zoom |

## GitHub Actions

`.github/workflows/build-windows.yml` publishes the portable folder on `windows-latest` and uploads it as an artifact. Compile the Inno Setup script on a Windows agent that has `iscc` installed (or add the `minmax/setup-innosetup` action).

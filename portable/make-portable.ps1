# Copies a published build into a portable folder with portable.txt beside the EXE.
# Usage (from repo root, after publish):
#   powershell -File portable/make-portable.ps1

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$src = Join-Path $root "artifacts\publish"
$dst = Join-Path $root "artifacts\portable"

if (-not (Test-Path $src)) {
  Write-Host "Publish output not found at $src"
  Write-Host "Run: dotnet publish native/src/ColumnNotes/ColumnNotes.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o artifacts/publish"
  exit 1
}

if (Test-Path $dst) { Remove-Item $dst -Recurse -Force }
New-Item -ItemType Directory -Path $dst | Out-Null
Copy-Item "$src\*" $dst -Recurse
Copy-Item (Join-Path $PSScriptRoot "portable.txt") $dst -Force

$zip = Join-Path $root "artifacts\ColumnNotes-portable-win-x64.zip"
if (Test-Path $zip) { Remove-Item $zip }
Compress-Archive -Path "$dst\*" -DestinationPath $zip
Write-Host "Portable folder: $dst"
Write-Host "Zip: $zip"

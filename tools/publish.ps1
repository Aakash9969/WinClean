# Builds the self-contained, single-file WinClean.exe for win-x64 into publish\win-x64.
# Usage: powershell -File tools\publish.ps1
#        powershell -File tools\publish.ps1 -Configuration Debug

param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "src\WinClean.App\WinClean.App.csproj"
$output = Join-Path $root "publish\win-x64"

Write-Output "Publishing WinClean ($Configuration, win-x64, self-contained single file)..."
dotnet publish $project `
    -c $Configuration `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=embedded `
    -o $output

if ($LASTEXITCODE -ne 0) {
    throw "Publish failed with exit code $LASTEXITCODE."
}

Write-Output ""
Write-Output "Published to: $output"
Get-ChildItem $output | ForEach-Object {
    Write-Output ("  {0}  ({1:N0} bytes)" -f $_.Name, $_.Length)
}
Write-Output ""
Write-Output "Next: compile the installer with Inno Setup (6.3+):"
Write-Output "  ISCC.exe installer\WinClean.iss"

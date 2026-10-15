# Captures WinClean window screenshots into docs\screenshots for the README.
# Usage: powershell -File tools\capture-screenshots.ps1
#
# What it does:
#   1. Backs up your real settings, then writes capture-friendly ones
#      (dark theme, admin rules disabled so the --scan launch shows no elevation prompt).
#   2. Launches the app, captures the idle window, closes it.
#   3. Launches the app with --scan (auto-scans), waits for the review tree, captures, closes it.
#   4. Restores your original settings.
#
# Only the WinClean window rectangle is captured, never the whole desktop.

$ErrorActionPreference = "Stop"

Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class CaptureNative {
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
}
"@
[CaptureNative]::SetProcessDPIAware() | Out-Null

$root = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $root "src\WinClean.App\bin\Debug\net8.0-windows\WinClean.exe"
$outDir = Join-Path $root "docs\screenshots"
if (-not (Test-Path $exe)) { throw "WinClean.exe not found at $exe - run 'dotnet build' first." }
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

function Wait-MainWindow([System.Diagnostics.Process]$process, [int]$timeoutSeconds) {
    $deadline = (Get-Date).AddSeconds($timeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        $process.Refresh()
        if (-not $process.HasExited -and $process.MainWindowHandle -ne [IntPtr]::Zero) { return $true }
        Start-Sleep -Milliseconds 200
    }
    return $false
}

function Capture-Window([System.Diagnostics.Process]$process, [string]$path) {
    $process.Refresh()
    [CaptureNative]::SetForegroundWindow($process.MainWindowHandle) | Out-Null
    Start-Sleep -Milliseconds 500

    $rect = New-Object CaptureNative+RECT
    [CaptureNative]::GetWindowRect($process.MainWindowHandle, [ref]$rect) | Out-Null
    $width = $rect.Right - $rect.Left
    $height = $rect.Bottom - $rect.Top
    if ($width -le 0 -or $height -le 0) { throw "Bad window rectangle ($width x $height)." }

    $bitmap = New-Object System.Drawing.Bitmap $width, $height
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.CopyFromScreen($rect.Left, $rect.Top, 0, 0, (New-Object System.Drawing.Size $width, $height))
        $bitmap.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $graphics.Dispose()
        $bitmap.Dispose()
    }
    Write-Output ("Captured: {0} ({1} x {2})" -f $path, $width, $height)
}

function Stop-App([System.Diagnostics.Process]$process) {
    if (-not $process.HasExited) {
        Stop-Process -Id $process.Id -ErrorAction SilentlyContinue
        $process.WaitForExit(5000) | Out-Null
    }
}

# --- 1. Capture-friendly settings (yours are backed up and restored at the end) -----------
$settingsPath = Join-Path $env:LOCALAPPDATA "WinClean\settings.json"
$settingsBackup = $null
if (Test-Path $settingsPath) { $settingsBackup = Get-Content $settingsPath -Raw }
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $settingsPath) | Out-Null
@'
{
  "theme": "Dark",
  "disabledRuleIds": [ "windows-temp", "windows-update-cache", "prefetch", "crash-dumps", "windows-error-reports" ]
}
'@ | Set-Content -Path $settingsPath -Encoding ASCII

try {
    # --- 2. Idle screen ---------------------------------------------------------------------
    $proc = Start-Process -FilePath $exe -PassThru
    if (-not (Wait-MainWindow $proc 20)) { Stop-App $proc; throw "The app window did not appear." }
    Start-Sleep -Seconds 3
    Capture-Window $proc (Join-Path $outDir "cleaner-idle.png")
    Stop-App $proc

    # --- 3. Review tree (--scan auto-starts the scan; wait for it to finish) ---------------
    $proc = Start-Process -FilePath $exe -ArgumentList "--scan" -PassThru
    if (-not (Wait-MainWindow $proc 20)) { Stop-App $proc; throw "The app window did not appear." }
    Start-Sleep -Seconds 12
    Capture-Window $proc (Join-Path $outDir "cleaner-review.png")
    Stop-App $proc
}
finally {
    # --- 4. Restore the user's real settings ------------------------------------------------
    if ($null -ne $settingsBackup) {
        Set-Content -Path $settingsPath -Value $settingsBackup -Encoding ASCII
    }
    elseif (Test-Path $settingsPath) {
        Remove-Item $settingsPath
    }
}

Write-Output ""
Write-Output "Done. Check the images in: $outDir"

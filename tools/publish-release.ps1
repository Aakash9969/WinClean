# Creates or updates a GitHub release for WinClean and uploads the built artifacts:
#   - WinClean-Setup-<version>.exe   (installer, from publish\installer)
#   - WinClean-Portable-<version>.zip (exe + rules.json, built here from publish\win-x64)
#
# Usage:
#   powershell -File tools\publish-release.ps1                     # tag v1.1.0
#   powershell -File tools\publish-release.ps1 -Tag v1.2.0
#
# Prerequisites:
#   1. tools\publish.ps1 has been run and the installer compiled for this version.
#   2. A stored GitHub credential exists (i.e. 'git push' works non-interactively);
#      the script reads it through Git Credential Manager and never prints it.

param(
    [string]$Tag = "v1.1.0",
    [string]$ReleaseNotes = ""
)

$ErrorActionPreference = "Stop"

# PowerShell 5.1 needs TLS 1.2 for the GitHub API.
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$repoOwner = "Aakash9969"
$repoName = "WinClean"
$root = Split-Path -Parent $PSScriptRoot
$version = $Tag.TrimStart("v")
$setupName = "WinClean-Setup-$version.exe"
$portableName = "WinClean-Portable-$version.zip"
$setupPath = Join-Path $root "publish\installer\$setupName"

if (-not (Test-Path $setupPath)) {
    throw "Not found: $setupPath. Run tools\publish.ps1 and the installer first."
}

if ([string]::IsNullOrWhiteSpace($ReleaseNotes)) {
    $ReleaseNotes = @"
WinClean $version

A safe, fast, personal junk cleaner for Windows 10/11. Scan first, review everything, delete only what you approve.

## Highlights
- Rules-driven targets: add new cleanable locations by editing rules.json
- Review tree with risk badges, exact file paths, and Safe-by-default selection
- Delete-time validation, locked-file skipping, and a full audit log
- Clean history, settings, dark/light theme
- Administrator elevation only when a rule needs it
- Memory page: reclaim RAM via working-set trimming (nothing is killed)

## Downloads
- **$setupName** - installer (recommended)
- **$portableName** - portable, no install needed (unzip and run)

Self-contained: no .NET installation required. Windows 10/11 (x64 or ARM64).

> The app is unsigned, so Windows SmartScreen may warn on first run: choose "More info" -> "Run anyway".
"@
}

# --- 1. GitHub credential from Git Credential Manager (never printed) -------------------
$env:GCM_INTERACTIVE = "Never"
$env:GIT_TERMINAL_PROMPT = "0"
$credentialLines = "protocol=https`nhost=github.com`n" | git credential fill
$token = ($credentialLines | Where-Object { $_ -like "password=*" }) -replace "^password=", ""
if ([string]::IsNullOrWhiteSpace($token)) {
    throw "No stored GitHub credential found. Push once with git (or run 'gh auth login'), then retry."
}

$headers = @{
    Authorization = "token $token"
    Accept        = "application/vnd.github+json"
    "User-Agent"  = "WinClean-release-script"
}

# --- 2. Repository visibility -----------------------------------------------------------
$repo = Invoke-RestMethod -Uri "https://api.github.com/repos/$repoOwner/$repoName" -Headers $headers
if ($repo.private) {
    Write-Warning "The repository is PRIVATE: only collaborators can download this release."
}

# --- 3. Create the release, or reuse it if it already exists -----------------------------
try {
    $release = Invoke-RestMethod -Uri "https://api.github.com/repos/$repoOwner/$repoName/releases/tags/$Tag" -Headers $headers
    Write-Output "Reusing existing release $Tag."
}
catch {
    $payload = @{
        tag_name         = $Tag
        target_commitish = "main"
        name             = "WinClean $version"
        body             = $ReleaseNotes
    } | ConvertTo-Json
    $release = Invoke-RestMethod -Uri "https://api.github.com/repos/$repoOwner/$repoName/releases" `
        -Method Post -Headers $headers -Body ([System.Text.Encoding]::UTF8.GetBytes($payload)) `
        -ContentType "application/json"
    Write-Output "Created release $Tag."
}

# --- 4. Build the portable zip -------------------------------------------------------------
$portablePath = Join-Path $root "publish\$portableName"
Compress-Archive `
    -Path (Join-Path $root "publish\win-x64\WinClean.exe"), (Join-Path $root "publish\win-x64\rules.json") `
    -DestinationPath $portablePath -Force

# --- 5. Replace same-named assets, then upload --------------------------------------------
foreach ($asset in @(
        @{ Name = $setupName;   Path = $setupPath },
        @{ Name = $portableName; Path = $portablePath }
    )) {
    $existing = Invoke-RestMethod -Uri "https://api.github.com/repos/$repoOwner/$repoName/releases/$($release.id)/assets" -Headers $headers |
        Where-Object { $_.name -eq $asset.Name }
    foreach ($old in $existing) {
        Invoke-RestMethod -Uri $old.url -Method Delete -Headers $headers | Out-Null
    }

    $uploadUrl = "https://uploads.github.com/repos/$repoOwner/$repoName/releases/$($release.id)/assets?name=$($asset.Name)"
    & curl.exe -sS -f -X POST `
        -H "Authorization: token $token" `
        -H "Content-Type: application/octet-stream" `
        --data-binary "@$($asset.Path)" `
        $uploadUrl | Out-Null
    Write-Output "Uploaded $($asset.Name)"
}

# --- 6. Verify ----------------------------------------------------------------------------
$latest = Invoke-RestMethod -Uri "https://api.github.com/repos/$repoOwner/$repoName/releases/latest" -Headers $headers
Write-Output ""
Write-Output "Release page: $($latest.html_url)"
$latest.assets | ForEach-Object {
    Write-Output ("  {0}  ({1:N1} MB)" -f $_.name, ($_.size / 1MB))
}

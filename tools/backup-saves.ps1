# Copy both games' save folders into local/saves/<timestamp>/ before any test that can write saves.
# Read-only on the live folders. Restoring is manual on purpose: copy back only the files you need,
# with both games closed, and never overwrite another project's files.
param(
    [string]$SaveRoot = (Join-Path $env:USERPROFILE 'AppData\LocalLow\Team Cherry')
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$running = Get-Process -Name 'hollow_knight', 'Hollow Knight Silksong' -ErrorAction SilentlyContinue
if ($running) { Write-Warning "A game is running ($($running.Name -join ', ')); the copy may catch a half-written save." }
$dest = Join-Path $repo ("local/saves/" + (Get-Date -Format 'yyyyMMdd-HHmmss'))
foreach ($pair in @(@('Hollow Knight', 'hollow-knight'), @('Hollow Knight Silksong', 'silksong'))) {
    $src = Join-Path $SaveRoot $pair[0]
    if (Test-Path $src) {
        Copy-Item -LiteralPath $src -Destination (Join-Path $dest $pair[1]) -Recurse
    } else {
        Write-Warning "No save folder at $src"
    }
}
Write-Host "Backed up to $dest"

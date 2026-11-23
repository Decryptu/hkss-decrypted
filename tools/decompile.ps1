# Decompile game code into local/decompiled/<game>/ (ignored by Git).
# Whole project:  pwsh tools/decompile.ps1 -Game hk
# One type:       pwsh tools/decompile.ps1 -Game silksong -Type HeroController
param(
    [Parameter(Mandatory)][ValidateSet('hk', 'silksong')][string]$Game,
    [string]$Type,
    [string]$Assembly = 'Assembly-CSharp.dll',
    [string]$SteamCommon = $(if ($env:STEAM_COMMON) { $env:STEAM_COMMON } else { 'C:\Program Files (x86)\Steam\steamapps\common' }),
    [string]$IlspyPath = $(if ($env:ILSPYCMD_PATH) { $env:ILSPYCMD_PATH } else { 'C:\Users\gurva\Documents\GitHub\spamgg\tools\local\ilspycmd.exe' })
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$managed = @{
    hk       = 'Hollow Knight\hollow_knight_Data\Managed'
    silksong = 'Hollow Knight Silksong\Hollow Knight Silksong_Data\Managed'
}[$Game]
if (!(Test-Path $IlspyPath)) { throw "ilspycmd not found at '$IlspyPath'. Set ILSPYCMD_PATH." }
$dll = Join-Path (Join-Path $SteamCommon $managed) $Assembly
if (!(Test-Path $dll)) { throw "Game assembly not found at '$dll'. Set STEAM_COMMON." }
$out = Join-Path $repo "local/decompiled/$Game"
New-Item -ItemType Directory -Force -Path $out | Out-Null
if ($Type) {
    $dest = Join-Path $out "$Type.cs"
    & $IlspyPath --disable-updatecheck -t $Type $dll | Set-Content -Encoding utf8 $dest
    if ($LASTEXITCODE) { throw "Decompilation failed for $Type" }
    Write-Host "Wrote $dest"
} else {
    $dest = Join-Path $out ([IO.Path]::GetFileNameWithoutExtension($Assembly))
    & $IlspyPath --disable-updatecheck -p -o $dest $dll
    if ($LASTEXITCODE) { throw "Decompilation failed for $Assembly" }
    Write-Host "Wrote project to $dest"
}

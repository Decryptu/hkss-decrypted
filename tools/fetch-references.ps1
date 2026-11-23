# Clone the upstream projects we port from into local/reference/ (ignored by Git) for reading.
# Nothing here is built or merged; code is ported file by file with provenance (see docs/ARCHITECTURE.md).
# Re-run to fast-forward existing checkouts.
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$ref = Join-Path $repo 'local/reference'
New-Item -ItemType Directory -Force -Path $ref | Out-Null
$sources = [ordered]@{
    'hk-archipelago-client' = 'https://github.com/ArchipelagoMW-HollowKnight/Archipelago.HollowKnight.git'  # MIT
    'hk-itemchanger'        = 'https://github.com/homothetyhk/HollowKnight.ItemChanger.git'               # LGPL-2.1
    'hk-randomizermod'      = 'https://github.com/homothetyhk/RandomizerMod.git'                          # LGPL-2.1
    'hk-randomizercore'     = 'https://github.com/homothetyhk/RandomizerCore.git'                         # LGPL-2.1
    'hk-menuchanger'        = 'https://github.com/homothetyhk/HollowKnight.MenuChanger.git'               # LGPL-2.1
    'hk-benchwarp'          = 'https://github.com/homothetyhk/HollowKnight.BenchwarpMod.git'              # LGPL-2.1
    'hk-modding-api'        = 'https://github.com/hk-modding/api.git'                                     # hooks we must replace
    'silksong-decrypted'    = 'https://github.com/Decryptu/silksong-decrypted.git'                       # MIT
}
foreach ($name in $sources.Keys) {
    $dir = Join-Path $ref $name
    if (Test-Path (Join-Path $dir '.git')) {
        git -C $dir pull --ff-only --quiet
    } else {
        git clone --filter=blob:none --quiet $sources[$name] $dir
    }
    if ($LASTEXITCODE) { throw "Failed to fetch $name" }
    Write-Host ("{0,-24} {1}" -f $name, (git -C $dir rev-parse --short HEAD))
}
# Archipelago's Hollow Knight world (worlds/hk), sparse so the full repository is not downloaded.
$ap = Join-Path $ref 'archipelago'
if (!(Test-Path (Join-Path $ap '.git'))) {
    git -c advice.detachedHead=false clone --filter=blob:none --sparse --quiet --branch 0.6.8 https://github.com/ArchipelagoMW/Archipelago.git $ap
    git -C $ap sparse-checkout set --no-cone /worlds/hk/ /worlds/generic/ /BaseClasses.py /Options.py /worlds/AutoWorld.py
}
Write-Host ("{0,-24} {1}" -f 'archipelago (0.6.8)', (git -C $ap rev-parse --short HEAD))

# Build both plugins and collect them with BepInEx into local/payload/, the folder the launcher installs from.
param([ValidateSet('Debug', 'Release')][string]$Configuration = 'Debug')
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$payload = Join-Path $repo 'local/payload'
& (Join-Path $PSScriptRoot 'fetch-deps.ps1')

if (Test-Path $payload) { Remove-Item $payload -Recurse -Force }
New-Item -ItemType Directory -Force -Path $payload | Out-Null
Copy-Item (Join-Path $repo 'local/deps/bepinex-5.4.23.6') (Join-Path $payload 'bepinex') -Recurse

foreach ($pair in @(@('HKSS.HollowKnight', 'hollow_knight'), @('HKSS.Silksong', 'silksong'))) {
    $project = Join-Path $repo "src/$($pair[0])"
    dotnet build $project -c $Configuration --nologo -v quiet
    if ($LASTEXITCODE) { throw "Building $($pair[0]) failed" }
    $dest = Join-Path $payload $pair[1]
    New-Item -ItemType Directory -Force -Path $dest | Out-Null
    foreach ($dll in "$($pair[0]).dll", 'HKSS.Core.dll', 'Archipelago.MultiClient.Net.dll') {
        Copy-Item (Join-Path $project "bin/$Configuration/netstandard2.1/$dll") $dest
    }
}
Write-Host "Payload ready in $payload"

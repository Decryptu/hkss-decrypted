# Download the pinned build dependencies into local/deps/ (ignored by Git) and verify their hashes.
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$deps = Join-Path $repo 'local/deps'
$version = '5.4.23.6'
$sha256 = '6c57a714a892efb4e9e1fef63d608a3391f30ec984031c465404e4ee381a0755'
$dest = Join-Path $deps "bepinex-$version"
if (Test-Path (Join-Path $dest 'BepInEx/core/BepInEx.dll')) { Write-Host "BepInEx $version is already in $dest"; return }
New-Item -ItemType Directory -Force -Path $dest | Out-Null
$zip = Join-Path $deps "BepInEx_win_x64_$version.zip"
Invoke-WebRequest "https://github.com/BepInEx/BepInEx/releases/download/v$version/BepInEx_win_x64_$version.zip" -OutFile $zip
$actual = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
if ($actual -ne $sha256) { Remove-Item $zip; throw "BepInEx hash mismatch: expected $sha256, got $actual" }
Expand-Archive $zip -DestinationPath $dest -Force
Remove-Item $zip
Write-Host "BepInEx $version verified and extracted to $dest"

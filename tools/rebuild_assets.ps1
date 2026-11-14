param(
    [Parameter(Mandatory=$true)][string]$Client,
    [string]$Godot = 'C:\SGO\Godot\Godot_v4.7.2-stable_win64_console.exe'
)
$ErrorActionPreference = 'Stop'
Push-Location (Split-Path -Parent $PSScriptRoot)
try {
    python tools/extract_client.py $Client .cache/client
    if ($LASTEXITCODE -ne 0) { throw 'Original client extraction failed' }
    python tools/build_assets.py .cache/client assets/generated
    if ($LASTEXITCODE -ne 0) { throw 'Atlas conversion failed; inspect catalog errors' }
    python tools/copy_support_assets.py .cache/client assets/generated
    if ($LASTEXITCODE -ne 0) { throw 'Support asset conversion failed' }
    python tools/build_maps.py .cache/client assets/generated --map 13
    if ($LASTEXITCODE -ne 0) { throw 'Original map conversion failed' }
    python tools/validate_assets.py .cache/client assets/generated --all-pixels
    if ($LASTEXITCODE -ne 0) { throw 'Atlas pixel validation failed' }
    & $Godot --headless --path . --script tools/benchmark_atlas.gd
    if ($LASTEXITCODE -ne 0) { throw 'Godot atlas load failed' }
} finally {
    Pop-Location
}

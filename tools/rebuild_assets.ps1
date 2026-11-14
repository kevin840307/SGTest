param(
    [Parameter(Mandatory=$true)][string]$Client,
    [string]$ServerDat = 'C:\SGO\Recovered\Old\服務端\Dat',
    [string]$Godot = 'C:\SGO\Godot\Godot_v4.7.2-stable_win64_console.exe'
)
$ErrorActionPreference = 'Stop'
Push-Location (Split-Path -Parent $PSScriptRoot)
try {
    python tools/extract_client.py $Client .cache/client
    if ($LASTEXITCODE -ne 0) { throw 'Original client extraction failed' }
    python tools/build_assets.py .cache/client client/assets/generated
    if ($LASTEXITCODE -ne 0) { throw 'Atlas conversion failed; inspect catalog errors' }
    python tools/copy_support_assets.py .cache/client client/assets/generated
    if ($LASTEXITCODE -ne 0) { throw 'Support asset conversion failed' }
    python tools/build_maps.py .cache/client client/assets/generated --map 13
    if ($LASTEXITCODE -ne 0) { throw 'Original map conversion failed' }
    python tools/validate_assets.py .cache/client client/assets/generated --all-pixels
    if ($LASTEXITCODE -ne 0) { throw 'Atlas pixel validation failed' }
    python tools/build_population.py $ServerDat --map 13
    if ($LASTEXITCODE -ne 0) { throw 'Original map monster population conversion failed' }
    python tools/apply_normalized_runtime_semantics.py --source .cache/client --runtime-assets client/assets/generated
    if ($LASTEXITCODE -ne 0) { throw 'Normalized direction/timing semantic overlay failed' }
    if (Test-Path 'content\monster_recipes') {
        Get-ChildItem 'content\monster_recipes' -Filter '*.monster.json' | Sort-Object Name | ForEach-Object {
            python tools/monster_workflow.py $_.FullName --source .cache/client --assets client/assets/generated --server-content server/content
            if ($LASTEXITCODE -ne 0) { throw ('Monster recipe failed: ' + $_.Name) }
        }
    }
    python tools/export_server_content.py
    if ($LASTEXITCODE -ne 0) { throw 'Server collision export failed' }
    & $Godot --headless --path client --script tools/benchmark_atlas.gd
    if ($LASTEXITCODE -ne 0) { throw 'Godot atlas load failed' }
} finally {
    Pop-Location
}

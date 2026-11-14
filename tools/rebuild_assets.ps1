param(
    [Parameter(Mandatory=$true)][string]$Client,
    [string]$ServerDat = '',
    [string]$Godot = 'C:\SGO\Godot\Godot_v4.7.2-stable_win64_console.exe'
)
$ErrorActionPreference = 'Stop'
Push-Location (Split-Path -Parent $PSScriptRoot)
try {
    if ([string]::IsNullOrWhiteSpace($ServerDat)) {
        $oldRoot = 'C:\SGO\Recovered\Old'
        $candidates = @(Get-ChildItem $oldRoot -Directory -ErrorAction Stop | ForEach-Object {
            $candidate = Join-Path $_.FullName 'Dat'
            if (Test-Path $candidate -PathType Container) { $candidate }
        })
        if ($candidates.Count -ne 1) {
            throw ('Cannot uniquely resolve Server Dat under ' + $oldRoot + '; pass -ServerDat explicitly')
        }
        $ServerDat = $candidates[0]
    }
    python tools/extract_client.py $Client .cache/client
    if ($LASTEXITCODE -ne 0) { throw 'Original client extraction failed' }

    # Non-SPR support resources and maps keep their own format-specific
    # converters. Runtime SPR atlases are built only by asset_pipeline.py.
    python tools/copy_support_assets.py .cache/client client/assets/generated
    if ($LASTEXITCODE -ne 0) { throw 'Support asset conversion failed' }
    python tools/build_maps.py .cache/client client/assets/generated --map 13
    if ($LASTEXITCODE -ne 0) { throw 'Original map conversion failed' }

    python tools/asset_pipeline.py --source .cache/client --assets client/assets/generated --server-dat $ServerDat --map 13
    if ($LASTEXITCODE -ne 0) { throw 'Unified lifecycle bundle build failed' }

    if (Test-Path 'content\monster_recipes') {
        Get-ChildItem 'content\monster_recipes' -Filter '*.monster.json' | Sort-Object Name | ForEach-Object {
            python tools/monster_workflow.py $_.FullName --source .cache/client --assets client/assets/generated --server-content server/content
            if ($LASTEXITCODE -ne 0) { throw ('Monster recipe failed: ' + $_.Name) }
        }
    }

    python tools/export_server_content.py
    if ($LASTEXITCODE -ne 0) { throw 'Server collision/map contract export failed' }

    # Runtime acceptance validates the unified manifests and normalized
    # direction/animation/layer semantics. Legacy atlas builders remain
    # research-only and are not part of the production rebuild path.
    & $Godot --headless --path client --script res://tests/test_directions.gd
    if ($LASTEXITCODE -ne 0) { throw 'Direction/animation bundle validation failed' }
    & $Godot --headless --path client --script res://tests/test_monster_visuals.gd
    if ($LASTEXITCODE -ne 0) { throw 'Map sprite bundle validation failed' }
    & $Godot --headless --path client --script res://tests/test_paper_doll_async.gd
    if ($LASTEXITCODE -ne 0) { throw 'Player base bundle validation failed' }
} finally {
    Pop-Location
}

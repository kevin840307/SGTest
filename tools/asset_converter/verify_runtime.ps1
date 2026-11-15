param(
    [Parameter(Mandatory=$true)][string]$Client,
    [string]$ServerDat = '',
    [string]$Godot = 'C:\SGO\Godot\Godot_v4.7.2-stable_win64_console.exe'
)
$ErrorActionPreference = 'Stop'
$Repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
Push-Location $Repo
try {
    $arguments = @(
        'tools/asset_converter/cli.py',
        'build',
        $Client,
        '--runtime-only',
        '--runtime-output',
        'client/assets'
    )
    if (-not [string]::IsNullOrWhiteSpace($ServerDat)) {
        $arguments += @('--server-dat', $ServerDat)
    }
    python @arguments
    if ($LASTEXITCODE -ne 0) { throw 'Asset converter build failed' }

    python tools/export_server_content.py --source client/assets --output server/content
    if ($LASTEXITCODE -ne 0) { throw 'Server map/collision contract export failed' }

    $tests = @(
        'test_directions.gd',
        'test_monster_visuals.gd',
        'test_map_objects_async.gd',
        'test_paper_doll_async.gd',
        'test_runtime_asset_store.gd',
        'test_navigation.gd'
    )
    foreach ($test in $tests) {
        & $Godot --headless --quit-after 1200 --path client --script ('res://tests/' + $test)
        if ($LASTEXITCODE -ne 0) { throw ('Godot asset regression failed: ' + $test) }
    }
} finally {
    Pop-Location
}

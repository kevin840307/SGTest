param(
    [string]$Client = '',
    [string]$ServerDat = ''
)
$ErrorActionPreference = 'Stop'
$Repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
Push-Location $Repo
try {
    if (-not [string]::IsNullOrWhiteSpace($Client)) {
        $arguments = @(
            'tools/asset_converter/cli.py', 'build', $Client,
            '--runtime-only', '--runtime-output', 'client/assets'
        )
        if (-not [string]::IsNullOrWhiteSpace($ServerDat)) {
            $arguments += @('--server-dat', $ServerDat)
        }
        python @arguments
        if ($LASTEXITCODE -ne 0) { throw 'Converter build failed' }

        python tools/export_server_content.py --source client/assets --output server/content
        if ($LASTEXITCODE -ne 0) { throw 'Server content data export failed' }
    }

    python -m unittest discover -s tools/tests -p 'test_*.py' -q
    if ($LASTEXITCODE -ne 0) { throw 'Asset converter tests failed' }

    python -m unittest discover -s tools -p 'test_*.py' -q
    if ($LASTEXITCODE -ne 0) { throw 'Pure Python content contracts failed' }

    Write-Host 'CONVERTER_TESTS_PASS (no playable Client/Server required)'
} finally {
    Pop-Location
}

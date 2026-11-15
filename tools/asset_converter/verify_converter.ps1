param(
    [string]$Client = '',
    [string]$ExtractedSource = '',
    [int]$PreviewGroups = 2
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$Repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
if ($PreviewGroups -lt 1) { throw 'PreviewGroups must be >= 1' }

Push-Location $Repo
try {
    # A clean-slate converter has no runnable Godot Client or .NET Server.
    # First verify pure Python decoders, metadata, planning and content contracts.
    python -m unittest discover -s tools/tests -p 'test_*.py' -q
    if ($LASTEXITCODE -ne 0) { throw 'Asset converter tests failed' }

    # Legacy standalone tests were retired; the single suite above verifies
    # original source formats, Height-Dense, converter planning and PCK policy.

    if (-not [string]::IsNullOrWhiteSpace($Client)) {
        $original = (Resolve-Path $Client -ErrorAction Stop).Path
        if (-not (Test-Path (Join-Path $original 'Data1.dat'))) {
            throw ('Original client is missing Data1.dat: ' + $original)
        }
        # Only test independent extraction -> atlas conversion. Do NOT run
        # --runtime-only against an empty client/assets in the clean-slate tree.
        $preview = Join-Path $Repo '.cache\asset_converter\verify-preview'
        $arguments = @(
            'tools/asset_converter/cli.py', 'build', $original,
            '--preview-groups', $PreviewGroups,
            '--archive-only', '--archive-output', $preview
        )
        if (-not [string]::IsNullOrWhiteSpace($ExtractedSource)) {
            $extracted = (Resolve-Path $ExtractedSource -ErrorAction Stop).Path
            $arguments += @('--extracted-source', $extracted)
        }
        python @arguments
        if ($LASTEXITCODE -ne 0) { throw 'Original source converter preview failed' }
    }

    Write-Host 'CONVERTER_TESTS_PASS (standalone: no playable Client/Server)'
} finally {
    Pop-Location
}

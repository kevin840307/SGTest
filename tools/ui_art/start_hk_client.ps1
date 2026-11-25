#Requires -Version 5.1
<#
.SYNOPSIS
  Launch the actual SGTest Windows client only when locally decoded HK UI art is ready.
.DESCRIPTION
  UI-001_ACCOUNT_LOGIN: prevents a missing private HK source export from
  silently launching the unrelated SGTest development login skin. Original HK
  data and generated private PNG/bitmap fonts never enter Git.
  This is an asset preflight, not a gameplay/UI parity or multiplayer Gate.
#>
[CmdletBinding()]
param(
    [string]$GodotPath = 'C:\SGO\Godot\Godot_v4.7.2-stable_win64.exe',
    [string]$SourceInterface = 'C:\SGO\Stargate Online IE HK\Interface',
    [string]$PackPath = '',
    [switch]$VerifyOnly,
    [switch]$NoImport
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repo = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$client = Join-Path $repo 'client'
$private = Join-Path $client 'ui\private_ie_hk2'
$sync = Join-Path $repo 'tools\ui_art\sync_original_ie_ui.py'
if (-not $PackPath) {
    $PackPath = Join-Path $repo '.cache\client-release\assets\assets.pck'
}

# Dimensions originate from 001_ACCOUNT_LOGIN and the original HK SPR tables.
# Testing both account and character pages prevents a single login sprite
# from incorrectly enabling a broken "HK" frontend.
$required = [ordered]@{
    'ACCOUNT_LOGIN_HK.png' = @(800, 600)
    'ACCOUNT_LOGIN_HK_CAPTIONS.png' = @(800, 600)
    'SELECT_CHARACTER.png' = @(800, 600)
    'CREATE_CHARACTER.png' = @(800, 600)
    'YELLOW_BUTTON_0.png' = @(85, 21)
    'B_ONLINEHELP_0.png' = @(16, 16)
    'CHANNEL.png' = @(469, 90)
    'BG_CHANNEL_BIG.png' = @(289, 578)
    'BG_FRIENDLIST_SMALL.png' = @(203, 90)
    'B_CHANNEL_CA_2.png' = @(43, 15)
    'B_CHANNEL_CB_2.png' = @(43, 15)
    'B_CHANNEL_CC_2.png' = @(43, 15)
    'B_CHANNEL_CD_2.png' = @(43, 15)
    'B_CHANNEL_CE_2.png' = @(43, 15)
    'ICON_CHANNEL_3.png' = @(24, 24)
    'ICON_BUBBLE_0.png' = @(24, 24)
    'B_EMOTETION_0.png' = @(24, 24)
    'SCROLLUP_S_3.png' = @(17, 25)
    'SCROLLDOWN_S_3.png' = @(17, 25)
    'SCROLLUP_BUTTON_0.png' = @(17, 36)
    'SCROLLDOWN_BUTTON_0.png' = @(17, 36)
    'B_LIST_ADD_0.png' = @(24, 24)
    'B_LIST_DEL_0.png' = @(24, 24)
    'B_LIST_EDIT_0.png' = @(24, 24)
    'B_LIST_TO_INGORE_0.png' = @(24, 24)
    'B_LIST_FRIENDLIST_0.png' = @(24, 24)
    'B_LIST_IGNORELIST_0.png' = @(24, 24)
}
$fonts = @('ACCOUNT_LOGIN_HK_INPUT_FONT.fnt', 'HK_SHARED_UI_FONT.fnt')

function Test-OriginalHkArt {
    Add-Type -AssemblyName System.Drawing
    foreach ($name in $required.Keys) {
        $path = Join-Path $private $name
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { return $false }
        $bitmap = $null
        try {
            $bitmap = [System.Drawing.Image]::FromFile($path)
            $size = $required[$name]
            if ($bitmap.Width -ne $size[0] -or $bitmap.Height -ne $size[1]) {
                return $false
            }
        } catch {
            return $false
        } finally {
            if ($null -ne $bitmap) { $bitmap.Dispose() }
        }
    }
    foreach ($name in $fonts) {
        $path = Join-Path $private $name
        if (-not (Test-Path -LiteralPath $path -PathType Leaf) -or
            (Get-Item -LiteralPath $path).Length -eq 0) { return $false }
    }
    return $true
}

if (-not (Test-OriginalHkArt)) {
    if ($NoImport) {
        throw 'HK original account/select/create/bottom HUD artwork is missing or has wrong dimensions; -NoImport refused the developer-skin fallback.'
    }
    if (-not (Test-Path -LiteralPath $SourceInterface -PathType Container)) {
        throw "HK source Interface not found: $SourceInterface. Supply -SourceInterface; do not substitute synthetic art."
    }
    if (-not (Test-Path -LiteralPath $sync -PathType Leaf)) {
        throw "HK private source decoder not found: $sync"
    }
    $python = Get-Command python -ErrorAction SilentlyContinue
    if ($null -eq $python) { throw 'Python is required to decode the local HK source sprites.' }
    & $python.Source $sync '--source' $SourceInterface | ForEach-Object {
        if ($_ -match '^CLASSIC_ASSETS_READY') { Write-Output $_ }
    }
    if ($LASTEXITCODE -ne 0 -or -not (Test-OriginalHkArt)) {
        throw 'HK source conversion did not produce the required original account/select/create/bottom HUD artwork.'
    }
}

if (-not (Test-Path -LiteralPath $GodotPath -PathType Leaf)) {
    throw "Godot executable not found: $GodotPath"
}
if (-not (Test-Path -LiteralPath $PackPath -PathType Leaf) -or
    (Get-Item -LiteralPath $PackPath).Length -eq 0) {
    throw "Formal client runtime pack missing: $PackPath"
}

Write-Output 'SGTEST_HK_SOURCE_READY account=800x600 select=800x600 create=800x600 origin=local_private'
if ($VerifyOnly) { return }

# Only spawn our own process. Never steal focus, stop another Client, kill a
# shared Server, change the OS display mode, or mutate another Session's DB.
# The 800x600 argument selects the original HK client viewport baseline only.
$process = Start-Process -FilePath $GodotPath -WorkingDirectory $repo -ArgumentList @(
    '--path', ('"' + $client + '"'), '--resolution', '800x600',
    '--', '--pack', ('"' + $PackPath + '"')
) -PassThru
Write-Output "SGTEST_HK_CLIENT_STARTED pid=$($process.Id) parity_gate=NOT_VERIFIED"

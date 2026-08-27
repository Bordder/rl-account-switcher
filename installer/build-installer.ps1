<#
    Builds the Rocket League Account Switcher MSI installer.

        .\build-installer.ps1 -Author "YourHandle"

    Steps:
      1. Publishes the app self-contained (single RLSwitcher.exe, no PDB).
      2. Generates license.rtf from the template with your name/handle.
      3. Compiles the MSI with WiX, showing that license during install.

    The app ships compiled only. Note that .NET assemblies can still be
    decompiled by a determined person; see the notes in the chat for how to
    raise that bar with obfuscation.
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Author,
    [string]$Version = "0.1.0",
    [string]$ReleaseDir = "",
    [switch]$SkipPublish
)

$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$root = Split-Path $here -Parent
$dotnetRoot = Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet'
$dotnet = Join-Path $dotnetRoot 'dotnet.exe'
if (-not (Test-Path $dotnet)) { $dotnet = (Get-Command dotnet).Source }

# 1. Publish -----------------------------------------------------------------
if (-not $SkipPublish) {
    Write-Host "Publishing app..." -ForegroundColor Cyan
    & $dotnet publish (Join-Path $root 'RLSwitcher.csproj') `
        -c Release -r win-x64 --self-contained true `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:DebugType=none -p:DebugSymbols=false `
        -o (Join-Path $root 'publish\app')
    if ($LASTEXITCODE -ne 0) { throw "publish failed" }
}

$exe = Join-Path $root 'publish\app\RLSwitcher.exe'
if (-not (Test-Path $exe)) { throw "Published exe not found: $exe" }

# 2. License -----------------------------------------------------------------
Write-Host "Generating license.rtf for '$Author'..." -ForegroundColor Cyan
$year = (Get-Date).Year
$template = Get-Content (Join-Path $here 'license.template.rtf') -Raw
$license = $template.Replace('@@AUTHOR@@', $Author).Replace('@@YEAR@@', "$year")
Set-Content -Path (Join-Path $here 'license.rtf') -Value $license -Encoding Ascii

# Plain-text copy for the repo / app folder.
$plain = @"
Rocket League Account Switcher
End User License Agreement

Copyright (c) $year $Author. All rights reserved.

This software is licensed, not sold, for personal non-commercial use only.
You may not copy, distribute, sell, sublicense, reverse engineer, modify, or
claim it as your own work. The software is provided "AS IS" without warranty
of any kind. It is an unofficial tool and is not affiliated with Epic Games or
Psyonix; all trademarks belong to their respective owners.
"@
Set-Content -Path (Join-Path $root 'LICENSE.txt') -Value $plain -Encoding utf8

# 3. Compile MSI -------------------------------------------------------------
Write-Host "Building MSI..." -ForegroundColor Cyan
$msi = Join-Path $root ("publish\RLSwitcher-$Version.msi")
& wix build (Join-Path $here 'Product.wxs') `
    -ext WixToolset.UI.wixext `
    -d "Author=$Author" -d "Version=$Version" `
    -o $msi
if ($LASTEXITCODE -ne 0) { throw "wix build failed" }

Write-Host "`nDone: $msi" -ForegroundColor Green

# Drop a copy into the releases folder for distribution.
if (-not $ReleaseDir) { $ReleaseDir = Join-Path (Split-Path $root -Parent) 'Releases' }
New-Item -ItemType Directory -Force -Path $ReleaseDir | Out-Null
Copy-Item $msi -Destination $ReleaseDir -Force
Write-Host "Copied to: $ReleaseDir" -ForegroundColor Green

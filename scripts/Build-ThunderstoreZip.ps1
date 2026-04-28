#Requires -Version 5.1
<#
.SYNOPSIS
  Builds Release and creates a Thunderstore-ready .zip (manifest + README + icon + plugins/ValheimElytra/).

.NOTES
  - BepInEx is not copied into the zip; it is listed in manifest.json as a dependency.
  - Package icon: prefers repo root icon.png, then optional thunderstore/icon.png.
  - Thunderstore expects 256x256; other sizes are scaled with bilinear resampling.
#>
[CmdletBinding()]
param(
    [string] $Configuration = "Release",
    [string] $ValheimPath = $env:VALHEIM,
    [switch] $SkipBuild
)

$ErrorActionPreference = "Stop"
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$outDir = Join-Path $repoRoot "dist"
$manifestPath = Join-Path $repoRoot "manifest.json"
$readmeTsPath = Join-Path $repoRoot "thunderstore\README.md"
$buildDll = Join-Path $repoRoot "dist\BepInEx\plugins\ValheimElytra\ValheimElytra.dll"
$tsSubdir = Join-Path $repoRoot "thunderstore"

if (-not (Test-Path $manifestPath)) {
    Write-Error "Missing $manifestPath"
}

if ([string]::IsNullOrWhiteSpace($ValheimPath) -and -not $SkipBuild) {
    Write-Error "Set VALHEIM or pass -ValheimPath to the Valheim install root (for assembly references during build)."
}

if (-not $SkipBuild) {
    $sln = Join-Path $repoRoot "ValheimElytra.sln"
    & dotnet build $sln -c $Configuration -p:ValheimPath="$ValheimPath"
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

if (-not (Test-Path -LiteralPath $buildDll)) {
    Write-Error "Build output not found: $buildDll (build first or use -SkipBuild with an existing build)"
}

# --- icon.png at zip root (256x256 for Thunderstore) ---
$iconCandidates = @(
    (Join-Path $repoRoot "icon.png"),
    (Join-Path $tsSubdir "icon.png")
)
$iconSrc = $iconCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iconSrc) {
    Write-Warning "No icon found; generating 256x256 placeholder at $tsSubdir\icon.png"
    if (-not (Test-Path $tsSubdir)) { New-Item -ItemType Directory -Path $tsSubdir -Force | Out-Null }
    Add-Type -AssemblyName System.Drawing
    $bmp = New-Object System.Drawing.Bitmap 256, 256
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.Clear([System.Drawing.Color]::FromArgb(255, 34, 40, 52))
    $brush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 140, 190, 255))
    $font = New-Object System.Drawing.Font "Segoe UI", 36, ([System.Drawing.FontStyle]::Bold)
    $g.DrawString("VE", $font, $brush, 52, 96)
    $g.Dispose()
    $iconSrc = Join-Path $tsSubdir "icon.png"
    $bmp.Save($iconSrc, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    $font.Dispose()
    $brush.Dispose()
}
else {
    Add-Type -AssemblyName System.Drawing
    $img = [System.Drawing.Image]::FromFile((Resolve-Path $iconSrc))
    try {
        if ($img.Width -ne 256 -or $img.Height -ne 256) {
            Write-Warning "Resizing icon from $($img.Width)x$($img.Height) to 256x256 for Thunderstore (source: $iconSrc)"
            $resized = New-Object System.Drawing.Bitmap 256, 256
            $g2 = [System.Drawing.Graphics]::FromImage($resized)
            $g2.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::Bilinear
            $g2.DrawImage($img, 0, 0, 256, 256)
            $g2.Dispose()
            $iconSrc = Join-Path $env:TEMP "valheimelytra-ts-icon.png"
            $resized.Save($iconSrc, [System.Drawing.Imaging.ImageFormat]::Png)
            $resized.Dispose()
        }
    }
    finally {
        if ($null -ne $img) { $img.Dispose() }
    }
}

if (Test-Path $readmeTsPath) {
    $readmePath = $readmeTsPath
}
else {
    $readmePath = Join-Path $repoRoot "README.md"
}

$manifest = Get-Content $manifestPath -Raw | ConvertFrom-Json
$ver = $manifest.version_number
$zipName = "ValheimElytra-$ver.zip"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$zipPath = Join-Path $outDir $zipName
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }

$staging = Join-Path $env:TEMP "thunderstore-pack-valheimelytra-$([Guid]::NewGuid().ToString('N'))"
$pluginDest = Join-Path $staging "plugins\ValheimElytra"
try {
    New-Item -ItemType Directory -Force -Path $pluginDest | Out-Null

    Copy-Item -LiteralPath $manifestPath -Destination (Join-Path $staging "manifest.json")
    Copy-Item -LiteralPath $readmePath -Destination (Join-Path $staging "README.md")
    Copy-Item -LiteralPath $iconSrc -Destination (Join-Path $staging "icon.png")
    Copy-Item -LiteralPath $buildDll -Destination (Join-Path $pluginDest "ValheimElytra.dll")

    Compress-Archive -Path (Join-Path $staging "*") -DestinationPath $zipPath -Force
}
finally {
    Remove-Item -LiteralPath $staging -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host "Created: $zipPath"
Write-Host "Upload this zip on Thunderstore (Valheim). Dependencies are resolved from manifest.json — do not bundle BepInEx in the zip."

#Requires -Version 5.1
<#
.SYNOPSIS
  Builds ValheimElytra (optional) and copies the plugin DLL into an r2modman / Thunderstore Mod Manager Valheim profile.

.DESCRIPTION
  Default profile folder (Windows):
    $env:APPDATA\r2modmanPlus-local\Valheim\profiles\<ProfileName>\BepInEx\plugins\ValheimElytra\

.PARAMETER Profile
  Profile folder name (default: Default).

.PARAMETER R2ProfilesRoot
  Override the profiles root if your install uses a custom location.

.PARAMETER SkipBuild
  Only copy; do not run dotnet build.

.PARAMETER ValheimPath
  Valheim install root for assembly references during build. Defaults to $env:VALHEIM.

.EXAMPLE
  .\scripts\Install-ToR2Profile.ps1

.EXAMPLE
  .\scripts\Install-ToR2Profile.ps1 -Profile "MyValheimMods"
#>
[CmdletBinding()]
param(
    [string] $Profile = "Default",
    [string] $R2ProfilesRoot = "",
    [string] $ValheimPath = $env:VALHEIM,
    [switch] $SkipBuild
)

$ErrorActionPreference = "Stop"
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$dllName = "ValheimElytra.dll"
$builtDll = Join-Path $repoRoot "dist\BepInEx\plugins\ValheimElytra\$dllName"

if (-not $SkipBuild) {
    if ([string]::IsNullOrWhiteSpace($ValheimPath)) {
        Write-Error "Set VALHEIM or pass -ValheimPath for dotnet build assembly references."
    }
    Push-Location $repoRoot
    try {
        Write-Host "Building Release..." -ForegroundColor Cyan
        dotnet build "ValheimElytra.sln" -c Release -p:ValheimPath="$ValheimPath" -v q
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    }
    finally {
        Pop-Location
    }
}

if (-not (Test-Path -LiteralPath $builtDll)) {
    Write-Error "Build output not found: $builtDll. Build first or use -SkipBuild after building manually."
}

if ([string]::IsNullOrWhiteSpace($R2ProfilesRoot)) {
    $R2ProfilesRoot = Join-Path $env:APPDATA "r2modmanPlus-local\Valheim\profiles"
}

$profileDir = Join-Path $R2ProfilesRoot $Profile
$pluginFolder = Join-Path $profileDir "BepInEx\plugins\ValheimElytra"
$pluginsDir = Join-Path $profileDir "BepInEx\plugins"

if (-not (Test-Path -LiteralPath $profileDir)) {
    Write-Error @"
Profile folder not found:
  $profileDir

In r2modman: Valheim → your profile → Settings → Browse profile folder.
Then run: .\scripts\Install-ToR2Profile.ps1 -Profile "ExactFolderName"

Or pass -R2ProfilesRoot if profiles live elsewhere.
"@
}

if (-not (Test-Path -LiteralPath (Join-Path $profileDir "BepInEx"))) {
    Write-Host "WARNING: No BepInEx folder under profile. Install BepInEx Pack for Valheim in r2modman first." -ForegroundColor Yellow
}

New-Item -ItemType Directory -Path $pluginFolder -Force | Out-Null
Copy-Item -LiteralPath $builtDll -Destination (Join-Path $pluginFolder $dllName) -Force

$iconRoot = Join-Path $repoRoot "icon.png"
if (Test-Path -LiteralPath $iconRoot) {
    Copy-Item -LiteralPath $iconRoot -Destination (Join-Path $pluginFolder "icon.png") -Force
}

$installed = Join-Path $pluginFolder $dllName
Write-Host "Installed: $installed" -ForegroundColor Green

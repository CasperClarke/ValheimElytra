#Requires -Version 5.0
<#
.SYNOPSIS
  Build ValheimElytra; with -Package, creates the Thunderstore zip via scripts\Build-ThunderstoreZip.ps1.

.PARAMETER ValheimPath
  Game root (contains valheim_Data\Managed). Falls back to $env:VALHEIM.

.PARAMETER Package
  Builds Release and packs manifest + README + icon + DLL to dist\ValheimElytra-<version>.zip
#>
param(
  [string] $ValheimPath = $env:VALHEIM,
  [switch] $Package
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $root

if ([string]::IsNullOrWhiteSpace($ValheimPath)) {
  Write-Error "Set -ValheimPath or the VALHEIM environment variable to your Valheim install root."
}

if ($Package) {
  & "$root\scripts\Build-ThunderstoreZip.ps1" -ValheimPath $ValheimPath
  exit $LASTEXITCODE
}

Write-Host "Using ValheimPath: $ValheimPath"
dotnet build "$root\ValheimElytra.sln" -c Release -p:ValheimPath="$ValheimPath"
exit $LASTEXITCODE

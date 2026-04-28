#Requires -Version 5.1
<#
.SYNOPSIS
  Builds ValheimElytra and copies ValheimElytra.dll to the r2modman profile "Mod Test".

.NOTES
  Adjust `-Profile` by calling Install-ToR2Profile.ps1 directly if your test profile uses another name.
#>
param(
    [string] $ValheimPath = $env:VALHEIM,
    [switch] $SkipBuild
)

$params = @{
    Profile      = "Mod Test"
    ValheimPath   = $ValheimPath
}
if ($SkipBuild) {
    $params.SkipBuild = $true
}

& "$PSScriptRoot\Install-ToR2Profile.ps1" @params

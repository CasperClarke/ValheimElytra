# Development & compilation

This document explains **how the project compiles**, **where references come from**, and **how we produce a Thunderstore zip**.

## Prerequisites

- [.NET SDK](https://dotnet.microsoft.com/download) (SDK-style projects; tested with **.NET 8 SDK** building **net472** targets)
- A Valheim installation with **BepInEx** extracted (Thunderstore **BepInExPack_Valheim** matches what players use)

## Reference assemblies (critical)

We reference **managed** binaries from Valheim:

| Assembly | Typical path (Windows Steam) |
| --- | --- |
| `assembly_valheim.dll`, `assembly_utils.dll`, Unity modules | `<Valheim>\valheim_Data\Managed\` |

From BepInEx core (or a local copy):

| Assembly | Typical path |
| --- | --- |
| `BepInEx.dll` | `<Valheim>\BepInEx\core\` **or** repo [`libs/BepInEx.dll`](../libs/README.md) |
| Harmony (`HarmonyLib`) | NuGet package **HarmonyX** (no separate `0Harmony.dll` path needed) |

The MSBuild variables are defined in [`Directory.Build.props`](../Directory.Build.props):

- `VALHEIM` **or** `/p:ValheimPath=...` → root of the game install  
- `VALHEIM_MANAGED` → overrides managed folder directly  
- `BEPINEX_CORE` → overrides BepInEx core folder  

Example **PowerShell** (Windows):

```powershell
$env:VALHEIM = "C:\Program Files (x86)\Steam\steamapps\common\Valheim"
dotnet build .\ValheimElytra.sln -c Release
```

The compiled plugin is emitted to [`dist/BepInEx/plugins/ValheimElytra/ValheimElytra.dll`](../dist/BepInEx/plugins/ValheimElytra/) (gitignored until you build).

### Why `net472`

Valheim’s Unity/Mono toolchain targets **.NET Framework 4.x** compatible profile. `net472` matches the ecosystem used by BepInEx plugins in the wild.

## Build scripts

| Script | Purpose |
| --- | --- |
| [`build.ps1`](../build.ps1) | Quick `dotnet build`, or `-Package` → delegates to Thunderstore packaging |
| [`scripts/Build-ThunderstoreZip.ps1`](../scripts/Build-ThunderstoreZip.ps1) | Full Thunderstore zip: resize icon to 256×256, stage `plugins/ValheimElytra/`, zip to **`dist/`** |
| [`scripts/Install-ToR2Profile.ps1`](../scripts/Install-ToR2Profile.ps1) | Build (optional) + copy DLL into an **r2modman** profile under `...\BepInEx\plugins\ValheimElytra\` |
| [`scripts/Publish-ModTest.ps1`](../scripts/Publish-ModTest.ps1) | Shortcut: install into profile **`Mod Test`** |

```powershell
$env:VALHEIM = "C:\Program Files (x86)\Steam\steamapps\common\Valheim"
.\build.ps1 -Package
```

Thunderstore artifact: **`dist/ValheimElytra-<version>.zip`** (version from [`manifest.json`](../manifest.json)).

Quick loop into r2modman Default profile:

```powershell
.\scripts\Install-ToR2Profile.ps1 -Profile "Default"
```

## Thunderstore layout

Thunderstore expects (simplified):

```text
manifest.json
icon.png
README.md
plugins/
  ValheimElytra/
    ValheimElytra.dll
```

The `manifest.json` [`dependencies`](../manifest.json) array lists other Thunderstore packages by **dependency string** (e.g. `denikson-BepInExPack_Valheim-5.4.2333`). Update the patch level if your profile uses a newer pack — r2modman resolves compatible versions automatically in most cases.

## Versioning checklist

1. Bump `Version` in [`ValheimElytraPlugin.cs`](../src/ValheimElytra/ValheimElytraPlugin.cs).
2. Bump `version_number` and optional `dependencies` in [`manifest.json`](../manifest.json).
3. Add an entry to [`CHANGELOG.md`](../CHANGELOG.md).

## Debugging tips

- Enable `DebugLogging` in config for periodic velocity logs.
- For Harmony issues after updates, inspect `LogOutput.log` at `Valheim/BepInEx/`.
- Use dnSpy / ILSpy on `assembly_valheim.dll` when Iron Gate renames methods or fields referenced via reflection (`WaterCheck`, `CapeDetection`).

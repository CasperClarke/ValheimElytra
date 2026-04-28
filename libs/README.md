# Local reference: `BepInEx.dll`

Put **`BepInEx.dll`** in this folder (`libs/BepInEx.dll`) if you compile without pointing MSBuild at a Valheim install that already has **BepInEx** under `Valheim/BepInEx/core/`.

Copy the file from your installed game:

`…/Valheim/BepInEx/core/BepInEx.dll`

The project resolves references in this order:

1. `$(BEPINEX_CORE)` or `<Valheim>/BepInEx/core/BepInEx.dll`
2. **`libs/BepInEx.dll`** (this folder)
3. Legacy fallback: `lib/BepInEx.dll` at repo root

Harmony is supplied by the **HarmonyX** NuGet package; you do not need `0Harmony.dll` here.

Do not commit `BepInEx.dll` to public repos if your license policy forbids redistributing it; keep it local or use env vars instead.

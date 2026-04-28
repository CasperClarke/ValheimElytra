# Manual validation checklist

Use this before publishing a new Thunderstore version. All testers should share the **same mod version** and **same BepInExPack_Valheim** dependency.

## Single-player

- [ ] With **only** BepInEx + ValheimElytra, equip Feather Cape, jump from height — glide engages while airborne.
- [ ] **Yaw**: move mouse horizontally — horizontal motion follows within configured turn responsiveness.
- [ ] **Pitch down**: gain horizontal speed / acceleration feel (not instant teleport).
- [ ] **Pitch up**: rise modestly at the cost of horizontal speed; cannot climb forever without speeding up again.
- [ ] **Land / water**: glide ends on ground contact; entering deep water cancels glide.
- [ ] **Stamina**: drain per second while gliding matches config; setting drain to `0` disables drain.
- [ ] **Toggle** `General.Enabled = false` — vanilla fall returns (mod does not crash).

## Multiplayer (2+ clients)

- [ ] Both clients install the mod; connect to a **dedicated** or **listen** server supported by your test setup.
- [ ] Remote player observes **no extreme rubber-banding** beyond baseline Valheim netcode when gliding.
- [ ] `ZDO` telemetry: optional — with debug tools, verify `FlightSync` keys only update while gliding (no spam on ground).

## Packaging

- [ ] `dotnet build -c Release` succeeds against a real game `Managed` folder.
- [ ] `.\build.ps1 -Package` produces a zip whose root contains `manifest.json`, `icon.png`, `README.md`, `plugins/...`.
- [ ] `manifest.json` dependency string matches an existing `BepInExPack_Valheim` Thunderstore release (or newer compatible).

## Regression triggers (re-test when Valheim updates)

- Harmony errors mentioning `Player.FixedUpdate`, `VisEquipment`, `ZDO`, or `Character.m_body`.
- Unexpected fall damage while wearing Feather Cape — indicates interaction with vanilla fall code; capture `LogOutput.log`.

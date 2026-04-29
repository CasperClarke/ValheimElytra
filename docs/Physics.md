# Elytra physics model (design notes)

This document describes **what we approximate**, **what we intentionally do not clone**, and **which config knobs map to math**.

## Minecraft Elytra (reference behaviour)

In Minecraft, Elytra flight is **camera-relative**:

- Horizontal steering follows where you look (yaw).
- Pitch strongly affects glide ratio: diving accelerates forward, climbing trades speed for height.
- Drag and gravity dissipate motion; stall happens if you climb without enough speed.

Exact equations differ from Valheim’s physics integrator and float behavior, so we implement a **gameplay analogue**, not a 1:1 port.

## Valheim integration points

We run after the resolved character tick (`Patches/CharacterUpdatePatch`) in a Harmony **postfix**. At that moment vanilla movement + water checks have run; we then rewrite `Rigidbody.linearVelocity` on the owning client if:

- Feather Cape (`CapeFeather`) is equipped (`Flight/CapeDetection.cs`)
- Character is airborne (`!IsOnGround()`)
- Not deep in water (`Flight/WaterCheck.cs`)

Access to the body is via the private Character field `m_body` (`Flight/CharacterBodyAccess.cs`) because Valheim does not expose a stable public setter used by all community mods.

## Integration step (high level)

`FlightPhysics.IntegrateGlide`:

1. **Gravity**: `Physics.gravity` (same magnitude as Valheim / Unity during glide; no extra multiplier).
2. **Lift / drag**: NACA 4415 \(C_L(\alpha)\), \(C_D(\alpha)\) from an XFOIL polar (see `FlightPhysics.cs`); drag magnitude is scaled by **`DragMultiplier`**. Force magnitudes use a small **minimum reference airspeed** for \(q\propto |v|^2\) so lift/drag do not fully collapse after heavy speed bleed; **AoA** still uses the real velocity direction. Diving faster comes from AoA / trajectory and gravity, not a separate thrust term.
3. **Yaw alignment**: blend horizontal velocity toward flattened camera forward (degrees/sec = **`TurnResponsiveness`**), with speed bleed while turning (**`TurnLossCoefficient`**).
4. **Safety clamp**: velocity caps derived from **`MaxGlideSpeed`** to limit runaway interaction with other mods.

## Network / authority model

Physics runs only if `ZNetView.IsOwner()` — the same rule most client-side movement mods follow. Remote players rely on vanilla network transform updates. We optionally mirror small numbers into `ZDO` (`Networking/FlightSync.cs`) for telemetry, not authoritative simulation.

## Config (user-facing)

- **`[General]`** — `Enabled`, `DebugLogging`
- **`[Elytra Physics]`** — `DragMultiplier`, `TurnResponsiveness`, `TurnLossCoefficient`, `MaxGlideSpeed`, `StaminaDrainPerSecond`
- **`[Visual]`** — `EnableVisualFlightPose`

Tuning: use **`DragMultiplier`** first for overall glide sink vs carry; then **`TurnResponsiveness`** for yaw authority.

## Known limitations

- Optional visual pose rotates the model toward velocity; optional smoothing helpers exist in `Networking/RemoteGlideSmoother.cs` for future work.
- Feather cape’s vanilla **terminal velocity cap** is effectively replaced while gliding; on the ground normal cape rules return.
- Competing mods that **also** rewrite `Rigidbody.velocity` in the same tick may fight us; Harmony patch ordering is outside this repository’s control.

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

1. **Gravity**: By default **no explicit `Physics.gravity` term** when the player’s **`Rigidbody.useGravity`** is true — Unity’s physics step already applies the same gravity vector each simulation step; adding it again from our Harmony postfix was roughly doubling effective \(g\) vs \(\sqrt{2gh}\) for steep dives. When **`useGravity`** is false, we still apply **`Physics.gravity`** once here so aerodynamics still have something to balance against pathological setups.
2. **Lift / drag**: NACA 4415 \(C_L(\alpha)\), \(C_D(\alpha)\) from an XFOIL polar (see `FlightPhysics.cs`); **`DragMultiplier`** scales \(C_D\) only. Dimensional lift/drag use **\(L,D=\tfrac12\rho S C_{L,D}|\mathbf{v}|^2\)** with **`FlightPhysics.AirDensityKgPerM3`** (ISA sea level), **`WingReferenceAreaM2`** from config (defaults to 15 m²), and **`FlightPhysics.GliderMassKg`** for \(\mathbf{a}=\mathbf{F}/m\). Same \(S\) for lift and drag; \(S\) is an effective reference area (not mesh-derived). **`DragMultiplier`** is gameplay trim on \(C_D\); lift and drag magnitudes both scale with \(S\). Optional future: `Rigidbody.mass` or config for \(m\) (and \(\rho\)).
3. **Yaw alignment**: blend horizontal velocity toward flattened camera forward (degrees/sec = **`TurnResponsiveness`**), with speed bleed while turning (**`TurnLossCoefficient`**).
4. **Safety clamp**: velocity caps derived from **`MaxGlideSpeed`** to limit runaway interaction with other mods.

## Network / authority model

Physics runs only if `ZNetView.IsOwner()` — the same rule most client-side movement mods follow. Remote players rely on vanilla network transform updates. We optionally mirror small numbers into `ZDO` (`Networking/FlightSync.cs`) for telemetry, not authoritative simulation.

## Config (user-facing)

- **`[General]`** — `Enabled`, `DebugLogging`
- **`[Elytra Physics]`** — `DragMultiplier`, `WingReferenceAreaM2`, `TurnResponsiveness`, `TurnLossCoefficient`, `MaxGlideSpeed`, `StaminaDrainPerSecond`
- **`[Visual]`** — `EnableVisualFlightPose`

Tuning: **`WingReferenceAreaM2`** (m², config, default 15) scales lift and drag together; **`DragMultiplier`** trims glide range via \(C_D\). Then **`TurnResponsiveness`** for yaw authority.

## Known limitations

- Optional visual pose rotates the model toward velocity; optional smoothing helpers exist in `Networking/RemoteGlideSmoother.cs` for future work.
- Feather cape’s vanilla **terminal velocity cap** is effectively replaced while gliding; on the ground normal cape rules return.
- Competing mods that **also** rewrite `Rigidbody.velocity` in the same tick may fight us; Harmony patch ordering is outside this repository’s control.

# Elytra physics model (design notes)

This document describes **what we approximate**, **what we intentionally do not clone**, and **which config knobs map to math**.

## Minecraft Elytra (reference behaviour)

In Minecraft, Elytra flight is **camera-relative**:

- Horizontal steering follows where you look (yaw).
- Pitch strongly affects glide ratio: diving accelerates forward, climbing trades speed for height.
- Drag and gravity dissipate motion; stall happens if you climb without enough speed.

Exact equations differ from Valheim’s physics integrator and float behavior, so we implement a **gameplay analogue**, not a 1:1 port.

## Valheim integration points

We run **after** `Player.FixedUpdate` in a Harmony **postfix** (`Patches/PlayerFixedUpdatePatch`). At that moment vanilla movement + water checks have run; we then rewrite `Rigidbody.velocity` on the owning client if:

- Feather Cape (`CapeFeather`) is equipped (`Flight/CapeDetection.cs`)
- Character is airborne (`!IsOnGround()`)
- Not deep in water (`Flight/WaterCheck.cs`)

Access to the body is via the private Character field `m_body` (`Flight/CharacterBodyAccess.cs`) because Valheim does not expose a stable public setter used by all community mods.

## Integration step (high level)

`FlightPhysics.IntegrateGlide`:

1. **Yaw alignment**: blend horizontal velocity toward flattened camera forward (degrees/sec = `TurnResponsiveness`).
2. **Pitch dive acceleration**: nose-down pitch adds forward acceleration scaled by `PitchDiveAcceleration`.
3. **Pitch climb lift**: nose-up pitch adds vertical lift proportional to horizontal speed, while shaving horizontal magnitude (scaled by `PitchClimbLift`).
4. **Gravity**: apply `Physics.gravity * GravityMultiplier`.
5. **Drag**: approximate air resistance as `AirDrag * |v| * v` directionally.
6. **Safety clamp**: prevent runaway speeds if another mod multiplies velocity.

## Network / authority model

Physics runs only if `ZNetView.IsOwner()` — the same rule most client-side movement mods follow. Remote players rely on vanilla network transform updates. We optionally mirror small numbers into `ZDO` (`Networking/FlightSync.cs`) for telemetry, not authoritative simulation.

## Tuning strategy

Start with defaults, then adjust in this order:

1. **`GravityMultiplier`** — overall floatiness.
2. **`AirDrag`** — how quickly excess speed bleeds off (stabilizes dives).
3. **`PitchDiveAcceleration` / `PitchClimbLift`** — Minecraft-like feel (trade-offs between speed and altitude).
4. **`TurnResponsiveness`** — snappy vs floaty turning.

## Known limitations

- We do not alter animations/VFX; optional smoothing helpers exist in `Networking/RemoteGlideSmoother.cs` for future work.
- Feather cape’s vanilla **terminal velocity cap** is effectively replaced while gliding; on the ground normal cape rules return.
- Competing mods that **also** rewrite `Rigidbody.velocity` in the same tick may fight us; Harmony patch ordering is outside this repository’s control.

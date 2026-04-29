using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;
using ValheimElytra.Networking;

namespace ValheimElytra.Flight
{
    /// <summary>
    /// Owns glide state tables and performs the per-tick simulation for eligible players.
    /// <para>
    /// Runs from a Harmony postfix on <see cref="Character.FixedUpdate"/> so we execute after vanilla movement
    /// logic for that frame, then override velocity for Elytra glide. Only the owning client applies forces
    /// for a given player — remotes rely on vanilla network transform sync.
    /// </para>
    /// </summary>
    public static class ElytraFlightSimulation
    {
        private static readonly Dictionary<int, FlightState> StatesByPlayerId = new Dictionary<int, FlightState>();
        private static int _debugTickCounter;

        private static ConfigEntry<float> DragMultiplier { get; set; } = null!;
        private static ConfigEntry<float> TurnAlignment { get; set; } = null!;
        private static ConfigEntry<float> TurnLossCoefficient { get; set; } = null!;
        private static ConfigEntry<float> MaxGlideSpeed { get; set; } = null!;
        private static ConfigEntry<float> StaminaDrainPerSecond { get; set; } = null!;
        private static ConfigEntry<bool> VisualPoseEnabled { get; set; } = null!;

        public static void BindConfig(ConfigFile config)
        {
            DragMultiplier = config.Bind(
                "Elytra Physics",
                "DragMultiplier",
                1.2f,
                new ConfigDescription(
                    "Multiplier on NACA 4415 Cd(alpha) from the polar. >1.0 more drag, <1.0 less. Large values for testing.",
                    new AcceptableValueRange<float>(0.01f, 50000f)));

            TurnAlignment = config.Bind(
                "Elytra Physics",
                "TurnResponsiveness",
                240f,
                new ConfigDescription(
                    "Degrees per second to align horizontal motion to camera yaw (higher = snappier turns).",
                    new AcceptableValueRange<float>(30f, 720f)));

            TurnLossCoefficient = config.Bind(
                "Elytra Physics",
                "TurnLossCoefficient",
                0.04f,
                new ConfigDescription(
                    "Extra horizontal speed bleed while turning (scales with turn rate × speed). 0 = no turn cost.",
                    new AcceptableValueRange<float>(0f, 0.2f)));

            MaxGlideSpeed = config.Bind(
                "Elytra Physics",
                "MaxGlideSpeed",
                42f,
                new ConfigDescription(
                    "Reference cap (m/s) for velocity limits; total speed may reach 1.5× this value.",
                    new AcceptableValueRange<float>(10f, 120f)));

            StaminaDrainPerSecond = config.Bind(
                "Elytra Physics",
                "StaminaDrainPerSecond",
                4f,
                new ConfigDescription(
                    "Stamina drain per second while gliding (0 disables).",
                    new AcceptableValueRange<float>(0f, 50f)));

            VisualPoseEnabled = config.Bind(
                "Visual",
                "EnableVisualFlightPose",
                true,
                "Rotate the player model toward direction of travel while gliding.");
        }

        public static void TickPlayer(Player player, float dt)
        {
            if (!ValheimElytraPlugin.ModEnabled.Value)
            {
                return;
            }

            if (player == null || player.IsDead())
            {
                return;
            }

            // In case we hook Update() fallback, use frame dt but keep a reasonable range.
            if (dt <= 0f)
            {
                dt = Time.deltaTime;
            }
            dt = Mathf.Clamp(dt, 0.005f, 0.05f);

            // Only the peer that owns this player should apply custom velocity (client-side authority model).
            ZNetView? nview = CharacterNetAccess.GetZNetView(player);
            if (nview == null || !nview.IsValid() || !nview.IsOwner())
            {
                if (ValheimElytraPlugin.DebugLogging.Value && ++_debugTickCounter % 120 == 0)
                {
                    ValheimElytraPlugin.Log.LogInfo("Elytra skip: non-owner or invalid ZNetView.");
                }
                return;
            }

            int id = player.GetInstanceID();
            if (!StatesByPlayerId.TryGetValue(id, out FlightState? state))
            {
                state = new FlightState();
                StatesByPlayerId[id] = state;
            }

            // Disqualify states that can't use elytra-like movement (deep liquid uses swimming locomotion).
            if (WaterCheck.IsInDeepWater(player))
            {
                state.IsGliding = false;
                FlightSync.ClearLocalZdo(player);
                if (state.VisualPoseApplied)
                {
                    VisualFlightPose.Clear(player);
                    state.VisualPoseApplied = false;
                }
                if (ValheimElytraPlugin.DebugLogging.Value && ++_debugTickCounter % 60 == 0)
                {
                    ValheimElytraPlugin.Log.LogInfo("Elytra skip: deep water.");
                }
                return;
            }

            bool hasCape = CapeDetection.IsWearingFeatherCape(player);
            bool airborne = CapeDetection.IsAirborne(player);
            bool canConsider = hasCape && airborne;

            if (!canConsider)
            {
                if (state.IsGliding && ValheimElytraPlugin.DebugLogging.Value)
                {
                    Vector3 v = CharacterBodyAccess.GetVelocity(player);
                    ValheimElytraPlugin.Log.LogInfo(
                        $"Elytra glide ended (sim stopped): isOnGround={player.IsOnGround()}, hasCape={hasCape}, " +
                        $"speedH={new Vector3(v.x, 0f, v.z).magnitude:0.00}, |v|={v.magnitude:0.00}");
                }

                if (state.IsGliding)
                {
                    state.ResetSession();
                }

                if (state.VisualPoseApplied)
                {
                    VisualFlightPose.Clear(player);
                    state.VisualPoseApplied = false;
                }

                FlightSync.ClearLocalZdo(player);
                if (ValheimElytraPlugin.DebugLogging.Value && ++_debugTickCounter % 45 == 0)
                {
                    ValheimElytraPlugin.Log.LogInfo(
                        $"Elytra inactive: hasCape={hasCape}, airborne={airborne}, " +
                        $"grounded={!airborne}, capeState=[{CapeDetection.DescribeCapeState(player)}]");
                }
                return;
            }

            // Feather cape normally hard-caps downward speed; our override replaces that behavior while gliding.

            Vector3 camForward = CameraSteering.GetCameraForward(flattenYawOnly: false);
            float pitch = CameraSteering.GetCameraPitchDegrees();

            var fp = new FlightPhysics.Params
            {
                DragMultiplier = DragMultiplier.Value,
                TurnAlignment = TurnAlignment.Value,
                TurnLossCoefficient = TurnLossCoefficient.Value,
                MaxGlideSpeed = MaxGlideSpeed.Value,
            };

            Vector3 vel = CharacterBodyAccess.GetVelocity(player);

            FlightPhysics.IntegrateGlide(
                dt,
                ref vel,
                camForward,
                fp,
                out float horizSpeed);

            CharacterBodyAccess.SetVelocity(player, vel);

            state.IsGliding = true;
            state.LastHorizontalSpeed = horizSpeed;
            state.GlideTime += dt;
            Vector3 horizLook = new Vector3(camForward.x, 0f, camForward.z);
            state.LastGlideDirectionHorizontal = horizLook.sqrMagnitude > 0.001f ? horizLook.normalized : Vector3.forward;

            if (VisualPoseEnabled.Value)
            {
                VisualFlightPose.Apply(player, vel, dt);
                state.VisualPoseApplied = true;
            }

            // Stamina cost (same peer that owns movement)
            if (StaminaDrainPerSecond.Value > 0f)
            {
                player.UseStamina(StaminaDrainPerSecond.Value * dt);
            }

            FlightSync.ApplyLocalGlideState(
                player,
                isGliding: true,
                horizontalSpeed: horizSpeed,
                glideDir: state.LastGlideDirectionHorizontal);

            if (ValheimElytraPlugin.DebugLogging.Value && Time.frameCount % 45 == 0)
            {
                ValheimElytraPlugin.Log.LogInfo(
                    $"Elytra tick: speedH={horizSpeed:0.0} speed3D={vel.magnitude:0.0} pitch={pitch:0.0} " +
                    $"glide={state.IsGliding}, hasCape={hasCape}, airborne={airborne}, dt={dt:0.000}, " +
                    $"capeState=[{CapeDetection.DescribeCapeState(player)}]");
            }
        }
    }
}

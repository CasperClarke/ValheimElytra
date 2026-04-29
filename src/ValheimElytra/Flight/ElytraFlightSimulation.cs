using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;
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
        private static ConfigEntry<float> WingReferenceAreaM2 { get; set; } = null!;
        private static ConfigEntry<float> TurnAlignment { get; set; } = null!;
        private static ConfigEntry<float> TurnLossCoefficient { get; set; } = null!;
        private static ConfigEntry<float> MaxGlideSpeed { get; set; } = null!;
        private static ConfigEntry<float> StaminaDrainPerSecond { get; set; } = null!;
        private static ConfigEntry<bool> VisualPoseEnabled { get; set; } = null!;

        /// <summary>
        /// When true, feather cape suppresses vanilla peak-distance fall damage and applies damage from max impact speed
        /// over a short recent window (see <see cref="OnUpdateGroundContactPrefix"/>).
        /// </summary>
        private static ConfigEntry<bool> FallDamageVelocityCap { get; set; } = null!;

        private static ConfigEntry<float> ImpactVelocityWindowSeconds { get; set; } = null!;
        private static ConfigEntry<bool> ImpactUseVerticalSpeedOnly { get; set; } = null!;
        private static ConfigEntry<float> ImpactDamageMinSpeed { get; set; } = null!;
        private static ConfigEntry<float> ImpactDamageMaxSpeed { get; set; } = null!;
        private static ConfigEntry<float> ImpactMaxDamage { get; set; } = null!;

        /// <summary>
        /// Allowed extra downward speed (m/s) above the sustained airborne peak within the impact window.
        /// Landing-frame rigidbody velocity often spikes vs descent — trim those impulses without using vanilla peak altitude.
        /// </summary>
        private static ConfigEntry<float> ImpactLandingSolverMargin { get; set; } = null!;

        /// <summary>Verbose per-landing logs for cape impact fall damage (BepInEx console).</summary>
        private static ConfigEntry<bool> FallDamageLogging { get; set; } = null!;

        private static readonly FieldInfo? MaxAirAltitudeField =
            AccessTools.Field(typeof(Character), "m_maxAirAltitude");

        /// <summary>
        /// Vanilla <c>m_groundContact</c>; prefix must only adjust altitude/damage when true (same guard as
        /// <c>UpdateGroundContact</c>) so we never rewrite state while airborne.
        /// </summary>
        private static readonly FieldInfo? GroundContactField =
            AccessTools.Field(typeof(Character), "m_groundContact");

        public static void BindConfig(ConfigFile config)
        {
            DragMultiplier = config.Bind(
                "Elytra Physics",
                "DragMultiplier",
                6f,
                new ConfigDescription(
                    "Multiplier on polar Cd. Larger WingReferenceAreaM2 increases lift and drag together (both ∝ S); use lower values here than with a tiny area if limiting glide range.",
                    new AcceptableValueRange<float>(0.01f, 50000f)));

            WingReferenceAreaM2 = config.Bind(
                "Elytra Physics",
                "WingReferenceAreaM2",
                12f,
                new ConfigDescription(
                    "Effective wing reference area in m² (same S for lift and drag in L = ½ρSV²C). Not mesh area.",
                    new AcceptableValueRange<float>(1f, 120f)));

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
                0.2f,
                new ConfigDescription(
                    "Extra horizontal speed bleed while turning (scales with turn rate × speed). 0 = no turn cost.",
                    new AcceptableValueRange<float>(0f, 0.8f)));

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

            FallDamageVelocityCap = config.Bind(
                "Elytra Physics",
                "FallDamageVelocityCap",
                true,
                "Feather cape: disable vanilla peak-distance fall damage and apply damage from max speed over ImpactVelocityWindowSeconds (impact metric).");

            ImpactVelocityWindowSeconds = config.Bind(
                "Elytra Physics",
                "ImpactVelocityWindowSeconds",
                0.1f,
                new ConfigDescription(
                    "Seconds of recent airborne-only velocity samples for sustained descent (merged with landing instant).",
                    new AcceptableValueRange<float>(0.03f, 0.5f)));

            ImpactUseVerticalSpeedOnly = config.Bind(
                "Elytra Physics",
                "ImpactUseVerticalSpeedOnly",
                true,
                "If true, impact metric uses max(0,-vy) from rigidbody velocity (recommended). False uses |velocity| — slopes/slides often read huge horizontal speed and falsely inflate damage.");

            ImpactDamageMinSpeed = config.Bind(
                "Elytra Physics",
                "ImpactDamageMinSpeed",
                11.8f,
                new ConfigDescription(
                    "Merged impact (m/s) at/below which cape fall deals no HP (exclusive upper band starts above this). Tune with ImpactDamageMaxSpeed — e.g. ~11–12 for light ~4 m-style impacts, ~28 for hard hits.",
                    new AcceptableValueRange<float>(0f, 80f)));

            ImpactDamageMaxSpeed = config.Bind(
                "Elytra Physics",
                "ImpactDamageMaxSpeed",
                28f,
                new ConfigDescription(
                    "Merged impact (m/s) at which cape fall reaches ImpactMaxDamage (linear between min and max). Typical terminal-style landings ~27–29 m/s in testing.",
                    new AcceptableValueRange<float>(4f, 120f)));

            ImpactMaxDamage = config.Bind(
                "Elytra Physics",
                "ImpactMaxDamage",
                100f,
                new ConfigDescription(
                    "Maximum damage from cape velocity landing (same scale as vanilla fall damage).",
                    new AcceptableValueRange<float>(1f, 500f)));

            ImpactLandingSolverMargin = config.Bind(
                "Elytra Physics",
                "ImpactLandingSolverMargin",
                6f,
                new ConfigDescription(
                    "Landing uses merged = min(max(sustained, raw), sustained + margin). Sustained = max airborne metric in window; raw = landing instant. Prevents physics impulse spikes from dominating tiny slopes.",
                    new AcceptableValueRange<float>(0f, 40f)));

            FallDamageLogging = config.Bind(
                "Elytra Physics",
                "FallDamageLogging",
                false,
                "Log cape impact landing metrics when UpdateGroundContact applies custom damage.");
        }

        /// <summary>Reflection / Harmony targets used by fall damage — log once at startup if debugging.</summary>
        internal static void LogFallDamageStartupDiagnostics()
        {
            MethodInfo? ugc = AccessTools.Method(typeof(Character), "UpdateGroundContact", new[] { typeof(float) });
            ValheimElytraPlugin.Log.LogInfo(
                $"[ValheimElytra] Fall damage: Character.UpdateGroundContact(float) {(ugc == null ? "NOT FOUND (Harmony patch may fail)" : "ok")}");
            ValheimElytraPlugin.Log.LogInfo(
                $"[ValheimElytra] Fall damage: field m_maxAirAltitude {(MaxAirAltitudeField == null ? "NOT FOUND" : "ok")}, " +
                $"m_groundContact {(GroundContactField == null ? "NOT FOUND" : "ok")}");
        }

        /// <summary>
        /// Samples rigidbody vertical velocity after each movement tick (same postfix as <see cref="TickPlayer"/>).
        /// Prefer fixed-timestep hooks for glide; Update fallback still updates samples so cape impact metrics stay fresh.
        /// </summary>
        public static void RecordPhysicsAlignedVerticalVelocity(Player player)
        {
            if (!ValheimElytraPlugin.ModEnabled.Value)
            {
                return;
            }

            ZNetView? nview = CharacterNetAccess.GetZNetView(player);
            if (nview == null || !nview.IsValid() || !nview.IsOwner())
            {
                return;
            }

            FlightState state = GetOrCreateState(player);
            Vector3 v = CharacterBodyAccess.GetVelocity(player);
            float vy = v.y;
            state.VerticalVelocityEndOfPreviousPhysicsStep = vy;

            if (!player.IsOnGround())
            {
                state.VerticalVelocityLastAirbornePhysicsStep = vy;
            }
            else
            {
                state.VerticalVelocityLastAirbornePhysicsStep = 0f;
            }

            float metric = ComputeImpactMetric(v);
            // Downhill slides while grounded pollute rigidbody samples with solver noise; only accumulate while airborne.
            if (!player.IsOnGround())
            {
                state.PushImpactSpeedSample(Time.time, metric);
            }
        }

        /// <summary>
        /// Prefix: with feather cape, clamp <c>m_maxAirAltitude</c> so vanilla distance-based fall damage does not apply,
        /// and compute impact from airborne descent samples plus landing instant, clipping physics impulse spikes.
        /// </summary>
        internal static void OnUpdateGroundContactPrefix(Character character)
        {
            if (!ValheimElytraPlugin.ModEnabled.Value || !FallDamageVelocityCap.Value || MaxAirAltitudeField == null ||
                GroundContactField == null)
            {
                return;
            }

            if (character is not Player player)
            {
                return;
            }

            ZNetView? nview = CharacterNetAccess.GetZNetView(player);
            if (nview == null || !nview.IsValid() || !nview.IsOwner())
            {
                return;
            }

            if (!(bool)GroundContactField.GetValue(player)!)
            {
                return;
            }

            if (!CapeDetection.IsWearingFeatherCape(player))
            {
                return;
            }

            FlightState state = GetOrCreateState(player);
            float y = player.transform.position.y;
            float maxAir = (float)MaxAirAltitudeField.GetValue(player)!;
            MaxAirAltitudeField.SetValue(player, Mathf.Min(maxAir, y + 4f));

            Vector3 v = CharacterBodyAccess.GetVelocity(player);
            float window = Mathf.Clamp(ImpactVelocityWindowSeconds.Value, 0.03f, 0.5f);
            float metricNow = ComputeImpactMetric(v);
            float sustained = state.MaxImpactMetricInWindow(Time.time, window);
            float margin = Mathf.Max(0f, ImpactLandingSolverMargin.Value);
            float impactMerged = Mathf.Min(Mathf.Max(sustained, metricNow), sustained + margin);
            state.PendingCapeImpactDamageSpeed = impactMerged;

            if (FallDamageLogging.Value)
            {
                ValheimElytraPlugin.Log.LogInfo(
                    $"[FallDamage] prefix sustained={sustained:F2} rawNow={metricNow:F2} merged={impactMerged:F2} m/s " +
                    $"(margin≤{margin:F1}, window={window:F2}s, airborneSamplesOnly)");
            }
        }

        /// <summary>
        /// Postfix: apply custom fall damage from queued merged impact when vanilla distance damage was suppressed.
        /// </summary>
        internal static void OnUpdateGroundContactPostfix(Character character)
        {
            if (!ValheimElytraPlugin.ModEnabled.Value || !FallDamageVelocityCap.Value)
            {
                return;
            }

            if (character is not Player player)
            {
                return;
            }

            ZNetView? nview = CharacterNetAccess.GetZNetView(player);
            if (nview == null || !nview.IsValid() || !nview.IsOwner())
            {
                return;
            }

            FlightState state = GetOrCreateState(player);
            float impact = state.PendingCapeImpactDamageSpeed;
            state.PendingCapeImpactDamageSpeed = -1f;

            // Prefix did not run our cape landing path — keep accumulating airborne samples.
            if (impact < 0f)
            {
                return;
            }

            state.ClearImpactSpeedSamples();

            if (player.IsDead())
            {
                return;
            }

            float minS = ImpactDamageMinSpeed.Value;
            float maxS = Mathf.Max(minS + 0.01f, ImpactDamageMaxSpeed.Value);
            float dmgScalar = Mathf.InverseLerp(minS, maxS, impact);
            dmgScalar = Mathf.Clamp01(dmgScalar);
            float damage = dmgScalar * ImpactMaxDamage.Value;

            if (damage < 0.5f)
            {
                if (FallDamageLogging.Value)
                {
                    ValheimElytraPlugin.Log.LogInfo(
                        $"[FallDamage] postfix skipped (below threshold): impact={impact:F2} m/s → damage={damage:F1}");
                }

                return;
            }

            HitData hit = new HitData();
            hit.m_damage.m_damage = damage;
            hit.m_hitType = HitData.HitType.Fall;
            hit.m_point = player.GetCenterPoint();
            player.Damage(hit);

            if (FallDamageLogging.Value)
            {
                ValheimElytraPlugin.Log.LogInfo(
                    $"[FallDamage] postfix Damage({damage:F1}) impact={impact:F2} m/s (min={minS:F1}, max={maxS:F1})");
            }
        }

        private static float ComputeImpactMetric(Vector3 velocity)
        {
            if (ImpactUseVerticalSpeedOnly.Value)
            {
                return Mathf.Max(0f, -velocity.y);
            }

            return velocity.magnitude;
        }

        /// <summary>
        /// Live HUD values matching cape fall-damage logic (merged impact uses same formula as landing prefix).
        /// </summary>
        internal static void ComputeFallDamageDebugOverlay(Player player, Vector3 rigidbodyVelocity,
            out float sustainedAirborneWindowMax,
            out float metricNow,
            out float mergedImpact,
            out float previewDamageHp,
            out int impactSampleCount)
        {
            sustainedAirborneWindowMax = 0f;
            metricNow = 0f;
            mergedImpact = 0f;
            previewDamageHp = 0f;
            impactSampleCount = 0;

            if (player == null || !ValheimElytraPlugin.ModEnabled.Value || !FallDamageVelocityCap.Value)
            {
                return;
            }

            FlightState state = GetOrCreateState(player);
            float window = Mathf.Clamp(ImpactVelocityWindowSeconds.Value, 0.03f, 0.5f);
            sustainedAirborneWindowMax = state.MaxImpactMetricInWindow(Time.time, window);
            impactSampleCount = state.ImpactSpeedSampleCount;
            metricNow = ComputeImpactMetric(rigidbodyVelocity);
            float margin = Mathf.Max(0f, ImpactLandingSolverMargin.Value);
            mergedImpact = Mathf.Min(Mathf.Max(sustainedAirborneWindowMax, metricNow), sustainedAirborneWindowMax + margin);

            float minS = ImpactDamageMinSpeed.Value;
            float maxS = Mathf.Max(minS + 0.01f, ImpactDamageMaxSpeed.Value);
            previewDamageHp = Mathf.Clamp01(Mathf.InverseLerp(minS, maxS, mergedImpact)) * ImpactMaxDamage.Value;
        }

        internal static string FormatImpactCurveSummary()
        {
            return
                $"Impact curve: {ImpactDamageMinSpeed.Value:F1}-{ImpactDamageMaxSpeed.Value:F1} m/s → ≤{ImpactMaxDamage.Value:F0} hp (solver margin {ImpactLandingSolverMargin.Value:F1})";
        }

        internal static float GetImpactVelocityWindowSecondsClamped()
        {
            return Mathf.Clamp(ImpactVelocityWindowSeconds.Value, 0.03f, 0.5f);
        }

        internal static bool CapeVelocityFallDamageEnabled => FallDamageVelocityCap.Value;

        private static FlightState GetOrCreateState(Player player)
        {
            int id = player.GetInstanceID();
            if (!StatesByPlayerId.TryGetValue(id, out FlightState? state))
            {
                state = new FlightState();
                StatesByPlayerId[id] = state;
            }

            return state;
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

            FlightState state = GetOrCreateState(player);

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
                WingReferenceAreaM2 = WingReferenceAreaM2.Value,
                TurnAlignment = TurnAlignment.Value,
                TurnLossCoefficient = TurnLossCoefficient.Value,
                MaxGlideSpeed = MaxGlideSpeed.Value,
            };

            // Unity integrates Physics.gravity for Rigidbody.useGravity in the physics step; do not add gravity again here
            // or steep dives roughly double vs √(2gh) from altitude-only estimates.
            bool rbUsesGravity = CharacterBodyAccess.TryGetBody(player) is { useGravity: true };

            Vector3 vel = CharacterBodyAccess.GetVelocity(player);

            FlightPhysics.IntegrateGlide(
                dt,
                ref vel,
                camForward,
                fp,
                rbUsesGravity,
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

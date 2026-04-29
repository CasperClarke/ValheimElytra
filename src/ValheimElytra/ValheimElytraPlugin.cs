using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using ValheimElytra.Flight;
using ValheimElytra.Patches;

namespace ValheimElytra
{
    /// <summary>
    /// BepInEx plugin entry for Elytra-style movement when wearing the vanilla Feather Cape.
    /// <para>
    /// This class is intentionally small: it loads config, applies Harmony patches, and initializes
    /// <see cref="ElytraFlightSimulation"/> static hooks that run gameplay logic during <c>FixedUpdate</c>.
    /// </para>
    /// </summary>
    [BepInPlugin(PluginGuid, Name, Version)]
    public sealed class ValheimElytraPlugin : BaseUnityPlugin
    {
        /// <summary>
        /// Stable BepInEx plugin identifier (also used as Harmony instance id).
        /// Config filename becomes <c>BepInEx/config/&lt;PluginGuid&gt;.cfg</c>.
        /// </summary>
        public const string PluginGuid = "team.valheimelytra.elytra";

        public const string Name = "ValheimElytra";

        /// <summary>Semantic version; keep in sync with Thunderstore manifest.json.</summary>
        public const string Version = "1.0.0";

        internal static ManualLogSource Log { get; private set; } = null!;

        internal static ConfigEntry<bool> ModEnabled { get; private set; } = null!;
        internal static ConfigEntry<bool> DebugLogging { get; private set; } = null!;

        /// <summary>Live HUD readout of rigidbody vertical velocity and speed (local player).</summary>
        internal static ConfigEntry<bool> ShowVelocityDebugOverlay { get; private set; } = null!;

        /// <summary>Longitudinal pitch ω, AoA, stamina authority (local player, while gliding).</summary>
        internal static ConfigEntry<bool> ShowGlidePitchDebugOverlay { get; private set; } = null!;

        private Harmony? _harmony;

        private void Awake()
        {
            Log = Logger;

            ModEnabled = Config.Bind(
                "General",
                "Enabled",
                true,
                "Master switch. When false, Harmony patches stay loaded but glide simulation exits immediately.");

            DebugLogging = Config.Bind(
                "General",
                "DebugLogging",
                false,
                "Verbose logs for troubleshooting desync / physics (spammy).");

            ShowVelocityDebugOverlay = Config.Bind(
                "Debug",
                "ShowVelocityDebugOverlay",
                false,
                "HUD: vy, |v|, height above solid (ray), vacuum equiv. fall height, cape impact preview / curve.");

            ShowGlidePitchDebugOverlay = Config.Bind(
                "Debug",
                "ShowGlidePitchDebugOverlay",
                true,
                "HUD: glide pitch moment (Ω_ctrl, Ω_stab), commanded vs effective AoA, stamina authority, stamina drain estimate.");

            _harmony = new Harmony(PluginGuid);
            _harmony.PatchAll(typeof(CharacterUpdatePatch).Assembly);

            ElytraFlightSimulation.BindConfig(Config);
            ElytraFlightSimulation.LogFallDamageStartupDiagnostics();

            Log.LogInfo($"{Name} {Version} loaded (Harmony patched on {CharacterUpdatePatch.ActiveMethodName}).");
        }

        private void OnGUI()
        {
            if (!ModEnabled.Value)
            {
                return;
            }

            bool showVel = ShowVelocityDebugOverlay.Value;
            bool showGlidePitch = ShowGlidePitchDebugOverlay.Value;
            if (!showVel && !showGlidePitch)
            {
                return;
            }

            Player? local = Player.m_localPlayer;
            if (local == null)
            {
                return;
            }

            float yTop = 12f;
            const float pad = 12f;
            const float width = 472f;

            if (showVel)
            {
                Vector3 v = CharacterBodyAccess.GetVelocity(local);
                Vector3 feet = local.transform.position;

                string heightStr = GroundHeightProbe.TryHeightAboveGround(feet, out float hAg)
                    ? $"{hAg:F2} m"
                    : "(no ray hit)";

                float vyDown = Mathf.Max(0f, -v.y);
                const float g = 9.81f;
                float vacuumEquivM = vyDown > 1e-4f ? (vyDown * vyDown) / (2f * g) : 0f;

                GUILayout.BeginArea(new Rect(pad, yTop, width, 280f));
                GUILayout.BeginVertical(GUI.skin.box);
                GUILayout.Label($"Vertical (vy): {v.y:F2} m/s");
                GUILayout.Label($"|velocity|: {v.magnitude:F2} m/s");
                GUILayout.Label($"Height above solid (ray): {heightStr}");
                GUILayout.Label($"Vacuum equiv. drop from |vy↓|: {vacuumEquivM:F2} m (compare to height ray)");

                if (ElytraFlightSimulation.CapeVelocityFallDamageEnabled)
                {
                    ElytraFlightSimulation.ComputeFallDamageDebugOverlay(
                        local,
                        v,
                        out float sustained,
                        out float metricNow,
                        out float merged,
                        out float previewHp,
                        out int nSamples);

                    GUILayout.Label(ElytraFlightSimulation.FormatImpactCurveSummary());
                    GUILayout.Label(
                        $"Impact now: {metricNow:F2}  sustained(max {ElytraFlightSimulation.GetImpactVelocityWindowSecondsClamped():F2}s): {sustained:F2}  merged≈{merged:F2}");
                    GUILayout.Label($"Airborne samples buffered: {nSamples}  cape preview if landed now: {previewHp:F0} hp");
                }
                else
                {
                    GUILayout.Label("Cape velocity fall damage (FallDamageVelocityCap): off");
                }

                GUILayout.EndVertical();
                GUILayout.EndArea();
                yTop += 288f;
            }

            if (showGlidePitch)
            {
                GUILayout.BeginArea(new Rect(pad, yTop, width, 352f));
                GUILayout.BeginVertical(GUI.skin.box);

                if (ElytraFlightSimulation.TryGetGlidePitchDebug(local, out GlidePitchDebugSnapshot g))
                {
                    GUILayout.Label("Glide pitch (static stability)");
                    GUILayout.Label(
                        $"AoA: cmd {g.AlphaCmdDeg:F2} deg  eff {g.AlphaEffBeforeDeg:F2} deg -> {g.AlphaEffAfterDeg:F2} deg  trim {g.TrimAoADeg:F1} deg");
                    GUILayout.Label(
                        $"d_alpha {g.PitchDemandDeg:F2} deg   K_c {g.PitchControlGainKc:F1}/s   K_s {g.PitchStaticStabilityKs:F1}/s");
                    GUILayout.Label(
                        $"Omega ctrl: raw {g.OmegaCtrlRawDegPerSec:F2} deg/s   applied {g.OmegaCtrlAppliedDegPerSec:F2} deg/s (stamina)");
                    GUILayout.Label(
                        $"Omega stab {g.OmegaStaticStabilityDegPerSec:F2} deg/s   Omega total {g.OmegaTotalDegPerSec:F2} deg/s");
                    GUILayout.Label(
                        $"Authority {g.AuthorityApplied:F3} ({(g.PitchAuthorityExhaustedBranch ? "exhausted" : "ok")} tier  healthy {g.PitchAuthorityHealthyConfigured:F2} / exhausted {g.PitchAuthorityExhaustedConfigured:F3}  thr {g.PitchAuthorityStaminaFracThreshold:F3})");
                    GUILayout.Label(
                        $"stamina {g.StaminaCurrent:F0}/{g.StaminaMax:F0}  frac@auth {g.StaminaFracWhenAuthorityChosen:F3}  frac after drain {g.StaminaFracAfterDrain:F3}");
                    GUILayout.Label(
                        $"Drain: moment ~{g.MomentDrainPerSecEstimated:F2}/s = scale {g.GlideMomentStaminaDrainScale:F2} x |Omega| x air {g.MomentDrainAirspeedFactor:F3}   |V| {g.VelocityMagnitudeForMomentDrainMps:F1} m/s");
                    GUILayout.Label(
                        $"Flat {g.FlatStaminaDrainPerSec:F2}/s   tick est. {g.EstimatedTotalStaminaSubtractedThisTick:F2} (flat {g.EstimatedFlatStaminaThisTick:F3} + moment {g.EstimatedMomentStaminaThisTick:F3})   dt {g.SimDtSeconds:F4}s");
                }
                else
                {
                    GUILayout.Label("Glide pitch debug: not gliding (need active feather-cape glide)");
                }

                GUILayout.EndVertical();
                GUILayout.EndArea();
            }
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
            _harmony = null;
        }
    }
}

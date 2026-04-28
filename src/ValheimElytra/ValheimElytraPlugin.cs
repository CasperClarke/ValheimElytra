using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
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

            _harmony = new Harmony(PluginGuid);
            _harmony.PatchAll(typeof(CharacterUpdatePatch).Assembly);

            ElytraFlightSimulation.BindConfig(Config);

            Log.LogInfo($"{Name} {Version} loaded (Harmony patched on {CharacterUpdatePatch.ActiveMethodName}).");
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
            _harmony = null;
        }
    }
}

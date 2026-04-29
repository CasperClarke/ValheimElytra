using System.Reflection;
using HarmonyLib;
using UnityEngine;
using ValheimElytra.Flight;

namespace ValheimElytra.Patches
{
    /// <summary>
    /// Runtime-resolved patch target for player movement tick.
    /// We try multiple method names because Valheim updates have moved/renamed tick methods.
    /// </summary>
    [HarmonyPatch]
    public static class CharacterUpdatePatch
    {
        private static readonly string[] CandidateMethods =
        {
            "CustomFixedUpdate",
            "FixedUpdate",
            "Update",
        };

        internal static string ActiveMethodName { get; private set; } = "none";

        private static MethodBase? TargetMethod()
        {
            foreach (string method in CandidateMethods)
            {
                MethodInfo? found = AccessTools.Method(typeof(Player), method);
                if (found != null)
                {
                    ActiveMethodName = $"Player.{method}";
                    return found;
                }
            }

            foreach (string method in CandidateMethods)
            {
                MethodInfo? found = AccessTools.Method(typeof(Character), method);
                if (found != null)
                {
                    ActiveMethodName = $"Character.{method}";
                    return found;
                }
            }

            ActiveMethodName = "none";
            return null;
        }

        private static void Postfix(Character __instance)
        {
            if (!(__instance is Player player))
            {
                return;
            }

            float dt = ActiveMethodName.EndsWith(".Update") ? Time.deltaTime : Time.fixedDeltaTime;
            ElytraFlightSimulation.TickPlayer(player, dt);

            // Cape impact fall damage samples rigidbody velocity each tick (including Player.Update fallback).
            ElytraFlightSimulation.RecordPhysicsAlignedVerticalVelocity(player);
        }
    }
}

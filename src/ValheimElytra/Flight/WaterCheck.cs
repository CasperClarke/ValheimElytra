using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace ValheimElytra.Flight
{
    /// <summary>
    /// Best-effort "am I in deep water" test without relying on a specific public API name across game updates.
    /// <para>
    /// We only use this to avoid applying air gliding while the character is using swim locomotion. The exact
    /// field/method name for liquid depth can change; we try a public method first, then a known private field.
    /// </para>
    /// </summary>
    public static class WaterCheck
    {
        private static readonly MethodInfo? GetLiquidLevelMethod =
            AccessTools.Method(typeof(Character), "GetLiquidLevel", null);

        private static readonly FieldInfo? LiquidLevelField =
            AccessTools.Field(typeof(Character), "m_liquidLevel");

        /// <summary>Returns 0 on land, &gt;0 in water. Interpretation is game-defined (meters).</summary>
        public static float GetLiquidLevel(Character character)
        {
            if (character == null)
            {
                return 0f;
            }

            if (GetLiquidLevelMethod != null)
            {
                object? v = GetLiquidLevelMethod.Invoke(character, null);
                if (v is float f)
                {
                    return f;
                }
            }

            if (LiquidLevelField != null)
            {
                object? v = LiquidLevelField.GetValue(character);
                if (v is float f2)
                {
                    return f2;
                }
            }

            return 0f;
        }

        public static bool IsInDeepWater(Character character, float minDepth = 0.55f)
        {
            return GetLiquidLevel(character) > minDepth;
        }
    }
}

using HarmonyLib;
using UnityEngine;

namespace ValheimElytra.Patches
{
    /// <summary>
    /// Disables vanilla Feather Cape equip status effect so the game's slow-fall cap
    /// does not fight the custom Elytra flight model.
    /// </summary>
    [HarmonyPatch(typeof(ObjectDB), "Awake")]
    public static class FeatherCapeEffectPatch
    {
        private static bool _applied;

        private static void Postfix(ObjectDB __instance)
        {
            if (_applied || __instance == null || __instance.m_items == null)
            {
                return;
            }

            foreach (GameObject itemObj in __instance.m_items)
            {
                if (itemObj == null || itemObj.name != "CapeFeather")
                {
                    continue;
                }

                ItemDrop itemDrop = itemObj.GetComponent<ItemDrop>();
                if (itemDrop == null || itemDrop.m_itemData == null || itemDrop.m_itemData.m_shared == null)
                {
                    continue;
                }

                if (itemDrop.m_itemData.m_shared.m_equipStatusEffect != null)
                {
                    itemDrop.m_itemData.m_shared.m_equipStatusEffect = null;
                    ValheimElytraPlugin.Log.LogInfo("Disabled vanilla Feather Cape equip status effect (slow-fall clamp).");
                }
                else
                {
                    ValheimElytraPlugin.Log.LogInfo("Feather Cape equip status effect already null.");
                }

                _applied = true;
                return;
            }

            ValheimElytraPlugin.Log.LogWarning("CapeFeather not found in ObjectDB; vanilla feather-fall effect was not disabled.");
        }
    }
}

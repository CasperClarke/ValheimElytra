using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace ValheimElytra.Flight
{
    /// <summary>
    /// Detects whether the local player has the vanilla Feather Cape equipped.
    /// <para>
    /// Valheim stores the visual shoulder slot (capes / cloaks) on <see cref="VisEquipment"/> as a private field
    /// (<c>m_shoulderItem</c>) in most versions. We access it via Harmony's <see cref="AccessTools"/> so we don't
    /// allocate reflection objects every frame.
    /// </para>
    /// <para>
    /// If a future Valheim patch renames that field, compilation still succeeds, but this resolver will return null
    /// until updated — see README troubleshooting.
    /// </para>
    /// </summary>
    public static class CapeDetection
    {
        /// <summary>Vanilla internal item id (spawn command: <c>spawn CapeFeather</c>).</summary>
        public const string FeatherCapeSharedName = "CapeFeather";

        private static readonly FieldInfo? VisShoulderField =
            AccessTools.Field(typeof(VisEquipment), "m_shoulderItem");

        private static readonly FieldInfo? HumanoidVisEquipmentField =
            AccessTools.Field(typeof(Humanoid), "m_visEquipment");

        private static readonly FieldInfo? ItemEquippedField =
            AccessTools.Field(typeof(ItemDrop.ItemData), "m_equipped")
            ?? AccessTools.Field(typeof(ItemDrop.ItemData), "m_equiped");

        /// <summary>
        /// Returns true when the player's equipped shoulder item matches the feather cape prefab name.
        /// </summary>
        public static bool IsWearingFeatherCape(Player player)
        {
            if (player == null)
            {
                return false;
            }

            ItemDrop.ItemData? shoulder = TryGetShoulderItemData(player);
            if (shoulder?.m_shared != null && shoulder.m_shared.m_name == FeatherCapeSharedName)
            {
                return true;
            }

            // Fallback: scan equipped items for the internal CapeFeather id (VisEquipment field renames, modded UIs, etc.).
            Inventory? inv = player.GetInventory();
            if (inv == null)
            {
                return false;
            }

            foreach (ItemDrop.ItemData? item in inv.GetAllItems())
            {
                if (item == null || item.m_shared == null)
                {
                    continue;
                }

                if (!IsEquipped(item))
                {
                    continue;
                }

                if (item.m_shared.m_name == FeatherCapeSharedName)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Best-effort read of the shoulder/cape slot <see cref="ItemDrop.ItemData"/>.</summary>
        private static bool IsEquipped(ItemDrop.ItemData item)
        {
            if (ItemEquippedField == null)
            {
                return false;
            }

            object? v = ItemEquippedField.GetValue(item);
            return v is bool equipped && equipped;
        }

        public static ItemDrop.ItemData? TryGetShoulderItemData(Player player)
        {
            VisEquipment? vis = HumanoidVisEquipmentField?.GetValue(player) as VisEquipment;
            if (vis == null || VisShoulderField == null)
            {
                return null;
            }

            object? value = VisShoulderField.GetValue(vis);
            if (value is ItemDrop.ItemData data)
            {
                return data;
            }

            if (value is ItemDrop drop)
            {
                return drop.m_itemData;
            }

            return null;
        }

        /// <summary>Utility: true if the player is falling/jumping according to Character ground state.</summary>
        public static bool IsAirborne(Player player)
        {
            // Character.IsOnGround() is the game's consolidated ground test.
            return player != null && !player.IsOnGround();
        }
    }
}

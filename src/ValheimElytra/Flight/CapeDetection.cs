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

        private static readonly FieldInfo? ItemDropPrefabField =
            AccessTools.Field(typeof(ItemDrop.ItemData), "m_dropPrefab");

        private static readonly MethodInfo? HumanoidIsItemEquippedMethod =
            AccessTools.Method(typeof(Humanoid), "IsItemEquiped", new[] { typeof(ItemDrop.ItemData) })
            ?? AccessTools.Method(typeof(Humanoid), "IsItemEquipped", new[] { typeof(ItemDrop.ItemData) });

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
            if (IsFeatherCapeItem(shoulder))
            {
                return true;
            }

            // Fallback: scan inventory items and ask Humanoid if each item is equipped when possible.
            // This handles builds/mod-setups where VisEquipment no longer stores ItemData in m_shoulderItem.
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

                if (!IsProbablyEquippedByPlayer(player, item))
                {
                    continue;
                }

                if (IsFeatherCapeItem(item))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Debug helper describing what the detector currently sees.
        /// Safe to call when debugging only; includes reflection reads.
        /// </summary>
        public static string DescribeCapeState(Player player)
        {
            if (player == null)
            {
                return "player=null";
            }

            ItemDrop.ItemData? shoulder = TryGetShoulderItemData(player);
            string shoulderName = shoulder?.m_shared?.m_name ?? "<null>";
            bool shoulderIsFeather = IsFeatherCapeItem(shoulder);

            Inventory? inv = player.GetInventory();
            if (inv == null)
            {
                return $"shoulder={shoulderName}, shoulderFeather={shoulderIsFeather}, inventory=<null>";
            }

            int equippedCount = 0;
            int featherEquippedCount = 0;
            foreach (ItemDrop.ItemData? item in inv.GetAllItems())
            {
                if (item == null)
                {
                    continue;
                }

                bool equipped = IsProbablyEquippedByPlayer(player, item);
                if (!equipped)
                {
                    continue;
                }

                equippedCount++;
                if (IsFeatherCapeItem(item))
                {
                    featherEquippedCount++;
                }
            }

            return $"shoulder={shoulderName}, shoulderFeather={shoulderIsFeather}, equippedItems={equippedCount}, featherEquipped={featherEquippedCount}";
        }

        private static bool IsFeatherCapeItem(ItemDrop.ItemData? item)
        {
            if (item == null || item.m_shared == null)
            {
                return false;
            }

            // Newer Valheim builds often use localized token names in m_shared.m_name.
            string sharedName = item.m_shared.m_name ?? string.Empty;
            if (MatchesFeatherCapeId(sharedName))
            {
                return true;
            }

            // Additional robust check via drop prefab name if present.
            if (ItemDropPrefabField != null)
            {
                object? dropPrefab = ItemDropPrefabField.GetValue(item);
                if (dropPrefab is GameObject go && MatchesFeatherCapeId(go.name))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool MatchesFeatherCapeId(string raw)
        {
            if (string.IsNullOrEmpty(raw))
            {
                return false;
            }

            // Accept internal prefab id and localization token variants.
            return raw == FeatherCapeSharedName ||
                   raw == "$item_cape_feather" ||
                   raw.IndexOf("capefeather", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                   raw.IndexOf("cape_feather", System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsProbablyEquippedByPlayer(Player player, ItemDrop.ItemData item)
        {
            // Prefer the game API when available.
            if (HumanoidIsItemEquippedMethod != null)
            {
                object? equippedObj = HumanoidIsItemEquippedMethod.Invoke(player, new object[] { item });
                if (equippedObj is bool equippedViaMethod)
                {
                    return equippedViaMethod;
                }
            }

            // Fallback to item field if method unavailable.
            return IsEquipped(item);
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

using UnityEngine;
using ValheimElytra.Flight;

namespace ValheimElytra.Networking
{
    /// <summary>
    /// Persists lightweight glide telemetry into the player's replicated <see cref="ZDO"/> data.
    /// <para>
    /// Why ZDO instead of a custom RPC? Valheim already replicates ZDO fields for each networked object. Writing
    /// small floats/bools here lets other peers (and dedicated servers) observe glide state without inventing a
    /// parallel networking layer. Movement itself remains client-owned: the authoritative transform sync is still
    /// vanilla; this is auxiliary state for cross-mod compatibility and optional future visuals.
    /// </para>
    /// <para>
    /// Hash keys use <see cref="ZdoStringHash.Of"/> so they remain stable across processes (unlike
    /// <see cref="string.GetHashCode"/> which may vary).
    /// </para>
    /// </summary>
    public static class FlightSync
    {
        private static readonly int ProtoVersionHash = ZdoStringHash.Of("VE_Elytra_V1_Proto");
        private static readonly int GlideActiveHash = ZdoStringHash.Of("VE_Elytra_V1_Glide");
        private static readonly int SpeedHorizontalHash = ZdoStringHash.Of("VE_Elytra_V1_SpeedH");
        private static readonly int DirXHash = ZdoStringHash.Of("VE_Elytra_V1_DirX");
        private static readonly int DirZHash = ZdoStringHash.Of("VE_Elytra_V1_DirZ");

        private const int ProtoVersion = 1;

        /// <summary>Called by the owning peer while gliding.</summary>
        public static void ApplyLocalGlideState(Player player, bool isGliding, float horizontalSpeed, Vector3 glideDir)
        {
            ZNetView? netView = CharacterNetAccess.GetZNetView(player);
            ZDO? zdo = netView != null ? netView.GetZDO() : null;
            if (zdo == null)
            {
                return;
            }

            zdo.Set(ProtoVersionHash, ProtoVersion);
            zdo.Set(GlideActiveHash, isGliding);
            zdo.Set(SpeedHorizontalHash, horizontalSpeed);
            zdo.Set(DirXHash, glideDir.x);
            zdo.Set(DirZHash, glideDir.z);
        }

        /// <summary>Clears glide flags when returning to grounded / ineligible states.</summary>
        public static void ClearLocalZdo(Player player)
        {
            ZNetView? netView = CharacterNetAccess.GetZNetView(player);
            ZDO? zdo = netView != null ? netView.GetZDO() : null;
            if (zdo == null)
            {
                return;
            }

            zdo.Set(ProtoVersionHash, 0);
            zdo.Set(GlideActiveHash, false);
            zdo.Set(SpeedHorizontalHash, 0f);
            zdo.Set(DirXHash, 0f);
            zdo.Set(DirZHash, 0f);
        }

        /// <summary>Optional reader for remote players / UI / companion mods.</summary>
        public static bool TryReadRemote(Player player, out RemoteElytraSnapshot snapshot)
        {
            snapshot = default;
            ZNetView? netView = CharacterNetAccess.GetZNetView(player);
            ZDO? zdo = netView != null ? netView.GetZDO() : null;
            if (zdo == null)
            {
                return false;
            }

            int proto = zdo.GetInt(ProtoVersionHash, 0);
            if (proto <= 0)
            {
                return false;
            }

            snapshot = new RemoteElytraSnapshot
            {
                Proto = proto,
                IsGliding = zdo.GetBool(GlideActiveHash, false),
                HorizontalSpeed = zdo.GetFloat(SpeedHorizontalHash, 0f),
                Direction = new Vector3(zdo.GetFloat(DirXHash, 0f), 0f, zdo.GetFloat(DirZHash, 0f)),
            };
            return true;
        }

        /// <summary>Serializable snapshot read from ZDO on any peer.</summary>
        public struct RemoteElytraSnapshot
        {
            public int Proto;
            public bool IsGliding;
            public float HorizontalSpeed;
            public Vector3 Direction;
        }
    }
}

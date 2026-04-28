using UnityEngine;

namespace ValheimElytra.Flight
{
    /// <summary>
    /// Lightweight per-player glide bookkeeping (not networked by itself — see Networking/FlightSync.cs).
    /// </summary>
    public sealed class FlightState
    {
        /// <summary>True while we are applying Elytra-like forces for this simulation step / glide session.</summary>
        public bool IsGliding;

        /// <summary>Horizon-speed from the previous physics step (used for smoothing and debug).</summary>
        public float LastHorizontalSpeed;

        /// <summary>Time spent gliding this session (seconds) for stamina integration.</summary>
        public float GlideTime;

        /// <summary>Last computed glide direction (horizontal), used for ZDO sync / interpolation.</summary>
        public Vector3 LastGlideDirectionHorizontal = Vector3.forward;

        /// <summary>Cooldown after leaving glide before stamina can recharge (optional future use).</summary>
        public float GlideExitTimer;

        /// <summary>Whether visual pose override is currently active for this player.</summary>
        public bool VisualPoseApplied;


        public void ResetSession()
        {
            IsGliding = false;
            GlideTime = 0f;
            VisualPoseApplied = false;
        }
    }
}

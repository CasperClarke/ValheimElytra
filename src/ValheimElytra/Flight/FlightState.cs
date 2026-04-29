using System.Collections.Generic;
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

        /// <summary>
        /// Longitudinal static-stability state: effective angle of attack (degrees) fed to the polar. Reset when glide session ends.
        /// </summary>
        public float EffectiveAoADeg;

        /// <summary>False until first glide tick initializes <see cref="EffectiveAoADeg"/> from commanded AoA.</summary>
        public bool GlidePitchStateInitialized;

        /// <summary>HUD: last glide tick longitudinal tuning snapshot (<see cref="ElytraFlightSimulation.TryGetGlidePitchDebug"/>).</summary>
        internal GlidePitchDebugSnapshot LastGlidePitchDebug;

        /// <summary>False until the first glide physics tick has populated <see cref="LastGlidePitchDebug"/>.</summary>
        internal bool GlidePitchDebugReady;

        /// <summary>Last computed glide direction (horizontal), used for ZDO sync / interpolation.</summary>
        public Vector3 LastGlideDirectionHorizontal = Vector3.forward;

        /// <summary>Cooldown after leaving glide before stamina can recharge (optional future use).</summary>
        public float GlideExitTimer;

        /// <summary>
        /// Vertical rigidbody velocity sampled at end of the last physics-aligned movement tick (CustomFixedUpdate /
        /// FixedUpdate via <see cref="Patches.CharacterUpdatePatch"/>, not Update).
        /// </summary>
        public float VerticalVelocityEndOfPreviousPhysicsStep;

        /// <summary>
        /// Last sampled vertical velocity while <see cref="Character.IsOnGround"/> was false (physics step).
        /// Cleared each grounded physics tick so walking does not retain stale air samples.
        /// </summary>
        public float VerticalVelocityLastAirbornePhysicsStep;

        /// <summary>Whether visual pose override is currently active for this player.</summary>
        public bool VisualPoseApplied;

        /// <summary>
        /// Rolling samples for cape impact damage (airborne ticks only — see <see cref="ElytraFlightSimulation.RecordPhysicsAlignedVerticalVelocity"/>).
        /// </summary>
        private readonly List<(float time, float metric)> _impactSpeedSamples = new List<(float, float)>();

        /// <summary>
        /// Non-negative impact metric queued from <c>UpdateGroundContact</c> prefix when suppressing vanilla fall damage;
        /// <c>-1</c> when none. Cleared in postfix after applying custom damage.
        /// </summary>
        internal float PendingCapeImpactDamageSpeed = -1f;

        internal void PushImpactSpeedSample(float timeSeconds, float metric)
        {
            _impactSpeedSamples.Add((timeSeconds, metric));
            const float maxAge = 0.25f;
            while (_impactSpeedSamples.Count > 0 && timeSeconds - _impactSpeedSamples[0].time > maxAge)
            {
                _impactSpeedSamples.RemoveAt(0);
            }

            while (_impactSpeedSamples.Count > 160)
            {
                _impactSpeedSamples.RemoveAt(0);
            }
        }

        internal float MaxImpactMetricInWindow(float timeSeconds, float windowSeconds)
        {
            float cutoff = timeSeconds - windowSeconds;
            float max = 0f;
            for (int i = 0; i < _impactSpeedSamples.Count; i++)
            {
                (float t, float m) = _impactSpeedSamples[i];
                if (t >= cutoff)
                {
                    max = Mathf.Max(max, m);
                }
            }

            return max;
        }

        internal void ClearImpactSpeedSamples()
        {
            _impactSpeedSamples.Clear();
        }

        internal int ImpactSpeedSampleCount => _impactSpeedSamples.Count;

        public void ResetSession()
        {
            IsGliding = false;
            GlideTime = 0f;
            GlidePitchStateInitialized = false;
            LastGlidePitchDebug = default;
            GlidePitchDebugReady = false;
            VisualPoseApplied = false;
            // Do not clear velocity snapshots here — same rationale as fall-damage caps (airborne flicker).
        }
    }
}

using UnityEngine;

namespace ValheimElytra.Networking
{
    /// <summary>
    /// Demonstrates how a non-owner peer can smooth auxiliary telemetry from <see cref="FlightSync"/> for visuals.
    /// <para>
    /// The default mod does not change animations; this helper is here so future features (wing vfx, sound loops)
    /// have a numeric target to lerp toward without affecting gameplay physics.
    /// </para>
    /// </summary>
    public sealed class RemoteGlideSmoother
    {
        public float SmoothedSpeed { get; private set; }

        public void UpdateSmoothing(Player player, float dt, float smoothTime = 0.15f)
        {
            if (!FlightSync.TryReadRemote(player, out FlightSync.RemoteElytraSnapshot snap))
            {
                SmoothedSpeed = Mathf.MoveTowards(SmoothedSpeed, 0f, dt * 50f);
                return;
            }

            if (!snap.IsGliding)
            {
                SmoothedSpeed = Mathf.MoveTowards(SmoothedSpeed, 0f, dt * 50f);
                return;
            }

            float target = snap.HorizontalSpeed;
            SmoothedSpeed = Mathf.SmoothDamp(SmoothedSpeed, target, ref _vel, smoothTime);
        }

        private float _vel;
    }
}

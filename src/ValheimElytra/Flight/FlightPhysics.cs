using UnityEngine;

namespace ValheimElytra.Flight
{
    /// <summary>
    /// Elytra-inspired gliding model adapted to Valheim's CharacterController / Rigidbody integration.
    /// <para>
    /// This is not a perfect clone of Minecraft's equations (those depend on Minecraft's specific drag and
    /// tick order), but it captures the gameplay pattern:
    /// - camera forward sets your "nose"
    /// - pitching down trades altitude for horizontal speed
    /// - pitching up trades speed for lift (limited), at risk of stall
    /// </para>
    /// <para>
    /// We operate on the Character's <c>Rigidbody.velocity</c> (<see cref="Player.m_body"/> on Humanoid),
    /// after vanilla movement for the tick, via a Harmony postfix.
    /// </para>
    /// </summary>
    public static class FlightPhysics
    {
        public struct Params
        {
            public float GravityMultiplier;
            public float BaseDrag;
            public float PitchDiveAccel;
            public float PitchClimbLift;
            public float MinGlideSpeed;
            public float MaxGlideSpeed;
            public float TurnAlignment;
            public float StaminaDrainPerSecond;
        }

        /// <summary>
        /// Integrate one FixedUpdate-style step for elytra gliding.
        /// </summary>
        /// <param name="dt">Fixed delta time (<see cref="Time.fixedDeltaTime"/>).</param>
        /// <param name="vel">Current rigidbody velocity (modified in-place).</param>
        /// <param name="cameraForward">Camera forward (full 3D direction).</param>
        /// <param name="pitchDegrees">Pitch from <see cref="CameraSteering.GetCameraPitchDegrees"/>.</param>
        /// <param name="p">Tuning parameters from BepInEx config.</param>
        /// <param name="horizontalSpeedOut">Horizontal speed magnitude after integration (for sync / debug).</param>
        public static void IntegrateGlide(
            float dt,
            ref Vector3 vel,
            Vector3 cameraForward,
            float pitchDegrees,
            Params p,
            out float horizontalSpeedOut)
        {
            // Direct Elytra-style equations (same structure/constants as snippet), with minimal dt normalization.
            // Snippet assumes a 20 TPS step (~0.05s), so we scale additive terms by dt/0.05 and exponentiate damping.
            float tickScale = Mathf.Clamp(dt / 0.05f, 0.1f, 3f);

            Vector3 look = cameraForward.sqrMagnitude > 0.001f ? cameraForward.normalized : Vector3.forward;
            float lookX = look.x;
            float lookY = look.y;
            float lookZ = look.z;

            float pitchRad = pitchDegrees * Mathf.Deg2Rad;
            float pitchCos = Mathf.Cos(pitchRad);
            float pitchSin = Mathf.Sin(pitchRad);

            float hVel = Mathf.Sqrt((vel.x * vel.x) + (vel.z * vel.z));
            float hLook = Mathf.Sqrt((lookX * lookX) + (lookZ * lookZ));

            // Equivalent of: sqrpitchcos = pitchcos^2 * min(1, |look| / 0.4)
            float lookMag = Mathf.Sqrt((lookX * lookX) + (lookY * lookY) + (lookZ * lookZ));
            float sqrPitchCos = pitchCos * pitchCos * Mathf.Min(1f, lookMag / 0.4f);

            // Vanilla-ish gravity term from snippet: velY += -0.08 + sqrpitchcos * 0.06
            float mcGravityStep = (-0.08f + (sqrPitchCos * 0.06f)) * p.GravityMultiplier;
            vel.y += mcGravityStep * tickScale;

            if (vel.y < 0f && hLook > 0.001f)
            {
                float yacc = vel.y * -0.1f * sqrPitchCos * (p.PitchDiveAccel / 18f) * tickScale;
                vel.y += yacc;
                vel.x += lookX * yacc / hLook;
                vel.z += lookZ * yacc / hLook;
            }

            // In the original snippet: if (pitch < 0) { ... }, with pitch where looking up is negative.
            if (pitchRad < 0f && hLook > 0.001f)
            {
                float yacc = hVel * -pitchSin * 0.04f * (p.PitchClimbLift / 28f) * tickScale;
                vel.y += yacc * 3.5f;
                vel.x -= lookX * yacc / hLook;
                vel.z -= lookZ * yacc / hLook;
            }

            if (hLook > 0.001f)
            {
                float align = 1f - Mathf.Pow(1f - 0.1f, tickScale * (p.TurnAlignment / 240f));
                vel.x += ((lookX / hLook) * hVel - vel.x) * align;
                vel.z += ((lookZ / hLook) * hVel - vel.z) * align;
            }

            float dragXZ = Mathf.Pow(0.99f, tickScale * (p.BaseDrag / 0.15f));
            float dragY = Mathf.Pow(0.98f, tickScale * (p.BaseDrag / 0.15f));
            vel.x *= dragXZ;
            vel.y *= dragY;
            vel.z *= dragXZ;

            hVel = Mathf.Sqrt((vel.x * vel.x) + (vel.z * vel.z));
            horizontalSpeedOut = Mathf.Clamp(hVel, 0f, p.MaxGlideSpeed * 2f); // clamp only absurd values

            // Hard cap to avoid runaway numbers if another mod boosts speed
            if (vel.magnitude > p.MaxGlideSpeed * 1.5f)
            {
                vel = vel.normalized * (p.MaxGlideSpeed * 1.5f);
            }

            // Tiny anti-stall nudge for low-speed dives (not part of vanilla snippet, kept minimal).
            if (horizontalSpeedOut < p.MinGlideSpeed && lookY < -0.15f && hLook > 0.001f)
            {
                Vector3 nudge = new Vector3(lookX / hLook, 0f, lookZ / hLook) * (p.MinGlideSpeed * 0.15f * dt);
                vel += nudge;
            }
        }
    }
}

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
            // Minecraft's formulas are tuned around a 20 TPS update.
            float tickScale = Mathf.Clamp(dt / 0.05f, 0.15f, 2f);

            Vector3 look = cameraForward.sqrMagnitude > 0.001f ? cameraForward.normalized : Vector3.forward;
            float lookX = look.x;
            float lookY = look.y;
            float lookZ = look.z;

            Vector3 horizontal = new Vector3(vel.x, 0f, vel.z);
            float hVel = horizontal.magnitude;
            float hLook = Mathf.Sqrt((lookX * lookX) + (lookZ * lookZ));
            float pitchCos = Mathf.Cos(pitchDegrees * Mathf.Deg2Rad);
            pitchCos = Mathf.Abs(pitchCos);
            pitchCos *= pitchCos;

            // Baseline glide gravity (customizable). This is the primary sink term.
            vel.y += Physics.gravity.y * p.GravityMultiplier * dt;

            // Elytra-like down-velocity conversion into forward motion.
            if (vel.y < 0f && hLook > 0.001f)
            {
                float yacc = vel.y * -0.10f * pitchCos * tickScale;
                vel.y += yacc;
                vel.x += (lookX / hLook) * yacc;
                vel.z += (lookZ / hLook) * yacc;
            }

            // Looking down converts horizontal speed into additional downward/forward travel.
            if (lookY < 0f && hLook > 0.001f)
            {
                float diveFactor = p.PitchDiveAccel / 18f; // Keep old config usable; 18 ~= baseline.
                float yacc = hVel * (-lookY) * 0.04f * diveFactor * tickScale;
                vel.y += yacc * 3.5f;
                vel.x -= (lookX / hLook) * yacc;
                vel.z -= (lookZ / hLook) * yacc;
            }

            // Looking up trades horizontal speed for lift (limited climb).
            // This is the key "pull up from a dive" behavior.
            if (lookY > 0f && hLook > 0.001f && hVel > p.MinGlideSpeed * 0.5f)
            {
                float climbFactor = p.PitchClimbLift / 28f; // 28 ~= baseline.
                float baseLift = hVel * lookY * 0.03f * climbFactor * tickScale;
                float pullUpBoost = Mathf.Max(0f, hVel - p.MinGlideSpeed) * lookY * 0.05f * climbFactor * tickScale;
                float yacc = baseLift + pullUpBoost;
                vel.y += yacc;
                vel.x -= (lookX / hLook) * (yacc * 0.35f);
                vel.z -= (lookZ / hLook) * (yacc * 0.35f);

                // Extra sink cancellation so a strong pull-up can actually arrest descent.
                if (vel.y < 0f)
                {
                    vel.y += Mathf.Min(-vel.y, yacc * 0.9f);
                }
            }

            // Smoothly align horizontal velocity with camera heading.
            if (hLook > 0.001f)
            {
                float alignGain = 0.10f * (p.TurnAlignment / 240f) * tickScale;
                vel.x += ((lookX / hLook) * hVel - vel.x) * alignGain;
                vel.z += ((lookZ / hLook) * hVel - vel.z) * alignGain;
            }

            // Drag terms similar to Elytra damping behavior.
            float dragLin = Mathf.Clamp01(1f - (0.01f * tickScale) - (p.BaseDrag * 0.01f * tickScale));
            float dragY = Mathf.Clamp01(1f - (0.02f * tickScale) - (p.BaseDrag * 0.01f * tickScale));
            vel.x *= dragLin;
            vel.z *= dragLin;
            vel.y *= dragY;

            // While pulling up, slightly reduce vertical damping so lift isn't immediately erased.
            if (lookY > 0.1f)
            {
                float liftRetention = 1f + (lookY * 0.03f * (p.PitchClimbLift / 28f) * tickScale);
                vel.y *= liftRetention;
            }

            hVel = new Vector3(vel.x, 0f, vel.z).magnitude;
            horizontalSpeedOut = Mathf.Clamp(hVel, 0f, p.MaxGlideSpeed * 2f); // clamp only absurd values

            // Hard cap to avoid runaway numbers if another mod boosts speed
            if (vel.magnitude > p.MaxGlideSpeed * 1.5f)
            {
                vel = vel.normalized * (p.MaxGlideSpeed * 1.5f);
            }

            // Ensure a minimum forward glide when diving (anti-stall nudge).
            if (horizontalSpeedOut < p.MinGlideSpeed && lookY < -0.15f && hLook > 0.001f)
            {
                Vector3 nudge = new Vector3(lookX / hLook, 0f, lookZ / hLook) * (p.MinGlideSpeed * 0.15f * dt);
                vel += nudge;
            }
        }
    }
}

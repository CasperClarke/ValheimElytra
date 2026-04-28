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
        /// <param name="cameraForwardFlattened">Camera forward flattened to XZ for horizontal steering.</param>
        /// <param name="pitchDegrees">Pitch from <see cref="CameraSteering.GetCameraPitchDegrees"/>.</param>
        /// <param name="p">Tuning parameters from BepInEx config.</param>
        /// <param name="horizontalSpeedOut">Horizontal speed magnitude after integration (for sync / debug).</param>
        public static void IntegrateGlide(
            float dt,
            ref Vector3 vel,
            Vector3 cameraForwardFlattened,
            float pitchDegrees,
            Params p,
            out float horizontalSpeedOut)
        {
            Vector3 desiredHorizDir = cameraForwardFlattened.sqrMagnitude > 0.001f
                ? cameraForwardFlattened.normalized
                : Vector3.forward;

            Vector3 horizontal = new Vector3(vel.x, 0f, vel.z);
            float horizSpeed = horizontal.magnitude;

            // --- Turn / bank: slowly align horizontal velocity toward camera yaw direction ---
            if (desiredHorizDir.sqrMagnitude > 0.001f && horizSpeed > 0.01f)
            {
                Vector3 currentDir = horizontal.normalized;
                float turn = p.TurnAlignment * dt;
                Vector3 blended = Vector3.RotateTowards(currentDir, new Vector3(desiredHorizDir.x, 0f, desiredHorizDir.z), turn * Mathf.Deg2Rad, 0f);
                vel = new Vector3(blended.x * horizSpeed, vel.y, blended.z * horizSpeed);
                horizontal = new Vector3(vel.x, 0f, vel.z);
                horizSpeed = horizontal.magnitude;
            }

            // Convert pitch to "nose-down" factor: negative pitch in Unity (look down) => positive diveFactor
            float diveFactor = -Mathf.Clamp(pitchDegrees / 45f, -1f, 1f);

            // Horizontal acceleration when diving (Minecraft: look down to go faster).
            float forwardAccel = diveFactor * p.PitchDiveAccel * dt;
            if (forwardAccel > 0f)
            {
                Vector3 accelVec = new Vector3(desiredHorizDir.x, 0f, desiredHorizDir.z).normalized * forwardAccel;
                vel += accelVec;
                horizontal = new Vector3(vel.x, 0f, vel.z);
                horizSpeed = horizontal.magnitude;
            }

            // Lift when pitching up while moving fast (trades horizontal speed for vertical lift)
            float climbFactor = Mathf.Clamp(pitchDegrees / 35f, 0f, 1f);
            if (climbFactor > 0.01f && horizSpeed > p.MinGlideSpeed * 0.75f)
            {
                float lift = climbFactor * p.PitchClimbLift * dt;
                // Cost: remove some horizontal speed when trading for height
                float horizontalCost = lift * 0.35f;
                horizSpeed = Mathf.Max(0f, horizSpeed - horizontalCost);
                vel.y += lift;
                if (horizontal.sqrMagnitude > 1e-6f)
                {
                    Vector3 horizDir = horizontal.normalized;
                    vel = new Vector3(horizDir.x * horizSpeed, vel.y, horizDir.z * horizSpeed);
                }
                else
                {
                    vel = new Vector3(0f, vel.y, 0f);
                }
            }

            // Gravity with multiplier (lighter than free fall while gliding)
            float gravity = Physics.gravity.y * p.GravityMultiplier;
            vel.y += gravity * dt;

            // Quadratic-ish drag against excess speed (stabilizes high-speed dive)
            float speed = vel.magnitude;
            if (speed > 0.01f)
            {
                Vector3 drag = -vel.normalized * (p.BaseDrag * speed * dt);
                vel += drag;
            }

            horizSpeed = new Vector3(vel.x, 0f, vel.z).magnitude;
            horizontalSpeedOut = Mathf.Clamp(horizSpeed, 0f, p.MaxGlideSpeed * 2f); // clamp only absurd values

            // Hard cap to avoid runaway numbers if another mod boosts speed
            if (vel.magnitude > p.MaxGlideSpeed * 1.5f)
            {
                vel = vel.normalized * (p.MaxGlideSpeed * 1.5f);
            }

            // Ensure a minimum forward glide when moving fast horizontally (anti-stall nudge)
            if (horizontalSpeedOut < p.MinGlideSpeed && diveFactor > 0.15f && desiredHorizDir.sqrMagnitude > 0.001f)
            {
                Vector3 nudge = new Vector3(desiredHorizDir.x, 0f, desiredHorizDir.z).normalized * (p.MinGlideSpeed * 0.15f * dt);
                vel += nudge;
            }
        }
    }
}

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
            public float DragMultiplier;
            public float PitchDiveAccel;
            public float PitchClimbLift;
            public float MinGlideSpeed;
            public float MaxGlideSpeed;
            public float TurnAlignment;
            public float StaminaDrainPerSecond;
        }

        // Approximate low-Re NACA 4415 polar samples (AoA deg, Cl, Cd).
        // Intentionally smoothed and clipped for game stability.
        private static readonly float[] PolarAoADeg =
        {
            -12f, -10f, -8f, -6f, -4f, -2f, 0f, 2f, 4f, 6f, 8f, 10f, 12f, 14f, 16f, 18f, 20f
        };

        private static readonly float[] PolarCl =
        {
            -0.55f, -0.40f, -0.20f, 0.00f, 0.20f, 0.40f, 0.62f, 0.82f, 1.00f, 1.16f, 1.28f, 1.38f, 1.46f, 1.50f, 1.42f, 1.20f, 0.95f
        };

        private static readonly float[] PolarCd =
        {
            0.050f, 0.036f, 0.026f, 0.020f, 0.016f, 0.014f, 0.013f, 0.014f, 0.016f, 0.019f, 0.024f, 0.030f, 0.040f, 0.055f, 0.080f, 0.120f, 0.180f
        };

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
            // Point-mass glider model:
            //   a = g + L + D, where L ⟂ v and D opposes v.
            // Camera controls body attitude, which changes angle-of-attack and thus Cl/Cd.
            Vector3 look = cameraForward.sqrMagnitude > 0.001f ? cameraForward.normalized : Vector3.forward;
            float speed = Mathf.Max(vel.magnitude, 0.1f);
            Vector3 vHat = vel / speed;

            // Body frame from camera.
            Vector3 bodyRight = Vector3.Cross(look, Vector3.up);
            if (bodyRight.sqrMagnitude < 0.001f)
            {
                bodyRight = Vector3.right;
            }
            bodyRight.Normalize();
            Vector3 bodyUp = Vector3.Cross(bodyRight, look).normalized;
            if (Vector3.Dot(bodyUp, Vector3.up) < 0f)
            {
                bodyUp = -bodyUp;
            }

            // Lift is bodyUp projected onto plane normal to velocity.
            Vector3 liftDir = bodyUp - (Vector3.Dot(bodyUp, vHat) * vHat);
            float liftDirMag = liftDir.magnitude;
            if (liftDirMag > 0.0001f)
            {
                liftDir /= liftDirMag;
            }
            else
            {
                liftDir = Vector3.up;
            }

            // Angle of attack: positive when nose pitched above velocity vector.
            float aoa = Mathf.Asin(Mathf.Clamp(Vector3.Dot(bodyUp, -vHat), -1f, 1f));
            aoa = Mathf.Clamp(aoa, -0.6f, 0.6f); // ~ +/-34 deg stall limits

            // NACA 4415 polar lookup (Cl/Cd from AoA).
            float aoaDeg = aoa * Mathf.Rad2Deg;
            InterpolatePolar(aoaDeg, out float clPolar, out float cdPolar);

            // Keep legacy config knobs useful as multipliers rather than hard-coded fudge.
            float liftScale = (p.PitchClimbLift / 28f);
            float cl = clPolar * liftScale;
            float cd = cdPolar * (p.BaseDrag / 0.15f) * p.DragMultiplier;
            cd = Mathf.Max(0.0001f, cd);

            // q proxy (0.5 * rho * v^2 * S / m folded into constants below).
            float q = speed * speed;

            // Acceleration terms.
            Vector3 gravityAcc = Physics.gravity * p.GravityMultiplier;
            Vector3 liftAcc = liftDir * (q * cl * 0.035f);
            Vector3 dragAcc = -vHat * (q * cd * 0.020f);

            // Optional dive-control authority (still physically plausible as posture-driven acceleration bias).
            float lookDown = Mathf.Clamp(-look.y, 0f, 1f);
            Vector3 lookHoriz = new Vector3(look.x, 0f, look.z);
            if (lookHoriz.sqrMagnitude > 0.001f)
            {
                lookHoriz.Normalize();
            }
            else
            {
                lookHoriz = new Vector3(vHat.x, 0f, vHat.z).normalized;
            }
            Vector3 diveControlAcc = lookHoriz * (p.PitchDiveAccel * 0.03f * lookDown);

            vel += (gravityAcc + liftAcc + dragAcc + diveControlAcc) * dt;

            // Yaw steering toward look direction (control, not free acceleration).
            Vector3 horizVel = new Vector3(vel.x, 0f, vel.z);
            float horizSpeed = horizVel.magnitude;
            if (horizSpeed > 0.01f && lookHoriz.sqrMagnitude > 0.001f)
            {
                Vector3 currentDir = horizVel / horizSpeed;
                float maxTurn = p.TurnAlignment * Mathf.Deg2Rad * dt;
                Vector3 turned = Vector3.RotateTowards(currentDir, lookHoriz, maxTurn, 0f);

                // Turning should cost energy: apply extra drag from heading-rate ("induced turn drag").
                float turnAngle = Vector3.Angle(currentDir, turned) * Mathf.Deg2Rad; // radians this tick
                float turnRate = turnAngle / Mathf.Max(dt, 1e-4f); // rad/s
                float turnLossCoeff = 0.020f * (1f + (p.BaseDrag / 0.15f));
                float turnDragMag = turnLossCoeff * turnRate * horizSpeed;
                float postTurnSpeed = Mathf.Max(0f, horizSpeed - (turnDragMag * dt));

                vel.x = turned.x * postTurnSpeed;
                vel.z = turned.z * postTurnSpeed;
            }

            // Safety clamp only (kept to prevent numeric blowups in modded stacks).
            if (vel.magnitude > p.MaxGlideSpeed * 1.5f)
            {
                vel = vel.normalized * (p.MaxGlideSpeed * 1.5f);
            }

            horizontalSpeedOut = Mathf.Clamp(new Vector3(vel.x, 0f, vel.z).magnitude, 0f, p.MaxGlideSpeed * 2f);
        }

        private static void InterpolatePolar(float aoaDeg, out float cl, out float cd)
        {
            if (aoaDeg <= PolarAoADeg[0])
            {
                cl = PolarCl[0];
                cd = PolarCd[0];
                return;
            }

            int last = PolarAoADeg.Length - 1;
            if (aoaDeg >= PolarAoADeg[last])
            {
                cl = PolarCl[last];
                cd = PolarCd[last];
                return;
            }

            for (int i = 0; i < last; i++)
            {
                float a0 = PolarAoADeg[i];
                float a1 = PolarAoADeg[i + 1];
                if (aoaDeg < a0 || aoaDeg > a1)
                {
                    continue;
                }

                float t = (aoaDeg - a0) / (a1 - a0);
                cl = Mathf.Lerp(PolarCl[i], PolarCl[i + 1], t);
                cd = Mathf.Lerp(PolarCd[i], PolarCd[i + 1], t);
                return;
            }

            cl = PolarCl[last];
            cd = PolarCd[last];
        }
    }
}

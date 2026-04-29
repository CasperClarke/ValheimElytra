using UnityEngine;

namespace ValheimElytra.Flight
{
    /// <summary>
    /// Elytra-inspired gliding model adapted to Valheim's CharacterController / Rigidbody integration.
    /// <para>
    /// This is not a perfect clone of Minecraft's equations (those depend on Minecraft's specific drag and
    /// tick order), but it captures the gameplay pattern:
    /// - camera forward sets your "nose"
    /// - pitching up trades speed for lift through AoA and the polar; diving uses gravity from Unity plus aerodynamic lift/drag on velocity (no duplicate gravity term when the rigidbody uses Unity gravity)
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
            public float DragMultiplier;
            public float TurnAlignment;
            /// <summary>Scales horizontal speed bleed when yaw-steering (rad/s × speed); not in the XFOIL polar.</summary>
            public float TurnLossCoefficient;
            /// <summary>Reference speed (m/s) for anti-glitch velocity caps; not physical terminal velocity.</summary>
            public float MaxGlideSpeed;
            /// <summary>Effective wing reference area (m²); lift and drag both ∝ S.</summary>
            public float WingReferenceAreaM2;
        }

        // --- Aerodynamic scaling (polar Cl, Cd are dimensionless; rho, S, m supply SI consistency) ---
        // L = 0.5 * rho * S * Cl * |V|^2 ,  D = 0.5 * rho * S * Cd * |V|^2  [N];  a = F / m.
        /// <summary>Air density (kg/m³). ISA sea level ~1.225; change for altitude / biomes if desired.</summary>
        public const float AirDensityKgPerM3 = 1.225f;

        /// <summary>Glider mass (kg) for F/m until config/Rigidbody wiring exists.</summary>
        public const float GliderMassKg = 80f;

        /// <summary>
        /// Rotates the aerodynamic wing plane nose-up relative to the camera forward by this angle (degrees).
        /// Polar AoA is computed in this frame so level camera implies ~this incidence vs flow when V aligns with look.
        /// </summary>
        public const float AeroPitchOffsetDegrees = 10f;

        // XFOIL polar for NACA 4415 (2D section), transcribed from Airfoil Tools.
        // Source: http://airfoiltools.com/polar/details?polar=xf-naca4415-il-500000
        // Conditions: Re = 5e5, Ncrit = 9, Mach = 0 (XFOIL 6.96). Not a literal match for a 3D cape.
        private static readonly float[] PolarAoADeg =
        {
            -15.5f, -15.25f, -15f, -14.75f, -14.5f, -14.25f, -14f, -13.75f, -13.5f, -13.25f, -13f, -12.75f, -12.5f, -12.25f, -12f,
            -11.75f, -11.5f, -11.25f, -11f, -10.75f, -10.5f, -10.25f, -10f, -9.75f, -9.5f, -9.25f, -9f, -8.75f, -8.5f, -8.25f, -8f,
            -7.75f, -7.5f, -7.25f, -7f, -6.75f, -6.5f, -6.25f, -6f, -5.75f, -5.5f, -5.25f, -5f, -4.75f, -4.5f, -4f, -3.75f, -3.5f,
            -3.25f, -3f, -2.75f, -2.5f, -2.25f, -2f, -1.75f, -1.5f, -1.25f, -1f, -0.75f, -0.5f, -0.25f, 0f, 0.25f, 0.5f, 0.75f, 1f,
            1.25f, 1.5f, 1.75f, 2f, 2.25f, 2.5f, 2.75f, 3f, 3.25f, 3.5f, 3.75f, 4f, 4.25f, 4.5f, 4.75f, 5f, 5.25f, 5.5f, 5.75f, 6f,
            6.25f, 6.5f, 6.75f, 7f, 7.25f, 7.5f, 7.75f, 8f, 8.25f, 8.5f, 8.75f, 9f, 9.25f, 9.5f, 9.75f, 10f, 10.25f, 10.5f, 10.75f,
            11.25f, 11.5f, 11.75f, 12f, 12.25f, 12.5f, 12.75f, 13f, 13.25f, 13.5f, 13.75f, 14f, 14.25f, 14.5f, 14.75f, 15f, 15.25f,
            15.5f, 15.75f, 16f, 16.25f, 16.5f, 16.75f, 17f, 17.25f, 17.5f, 17.75f, 18f, 18.25f, 18.5f, 18.75f, 19f, 19.25f
        };

        private static readonly float[] PolarCl =
        {
            -0.7223f, -0.7608f, -0.7828f, -0.808f, -0.831f, -0.857f, -0.874f, -0.8937f, -0.9159f, -0.9234f, -0.9106f, -0.8987f,
            -0.884f, -0.8601f, -0.8444f, -0.8231f, -0.7951f, -0.7739f, -0.7501f, -0.7209f, -0.6912f, -0.6705f, -0.6443f, -0.6125f,
            -0.582f, -0.5475f, -0.5273f, -0.4983f, -0.4646f, -0.4266f, -0.3899f, -0.3704f, -0.3375f, -0.3013f, -0.2765f, -0.2465f,
            -0.2099f, -0.1889f, -0.1569f, -0.1314f, -0.1035f, -0.0768f, -0.0509f, -0.0246f, 0.0009f, 0.0518f, 0.0753f, 0.102f,
            0.124f, 0.1491f, 0.1725f, 0.1962f, 0.2201f, 0.2427f, 0.2663f, 0.289f, 0.3117f, 0.3342f, 0.3555f, 0.3763f, 0.3953f,
            0.4127f, 0.4297f, 0.4468f, 0.4669f, 0.5376f, 0.6155f, 0.6568f, 0.6936f, 0.7343f, 0.7769f, 0.8247f, 0.8487f, 0.8663f,
            0.8838f, 0.9013f, 0.9182f, 0.9353f, 0.9525f, 0.9704f, 0.9883f, 1.0049f, 1.0222f, 1.0363f, 1.0525f, 1.0667f, 1.0835f,
            1.099f, 1.1161f, 1.1318f, 1.1489f, 1.164f, 1.1818f, 1.1969f, 1.2128f, 1.2288f, 1.2431f, 1.2562f, 1.2688f, 1.2808f,
            1.2927f, 1.3049f, 1.3167f, 1.3281f, 1.3381f, 1.3593f, 1.3691f, 1.3789f, 1.3888f, 1.3969f, 1.4066f, 1.4156f, 1.4218f,
            1.432f, 1.437f, 1.4457f, 1.4517f, 1.4562f, 1.4623f, 1.4646f, 1.4688f, 1.4697f, 1.4688f, 1.4685f, 1.4636f, 1.4617f,
            1.4549f, 1.4537f, 1.4474f, 1.4399f, 1.4371f, 1.4315f, 1.4234f, 1.4133f, 1.4111f, 1.4072f, 1.4011f, 1.3935f
        };

        private static readonly float[] PolarCd =
        {
            0.08142f, 0.07258f, 0.06652f, 0.06027f, 0.05454f, 0.04878f, 0.0447f, 0.04113f, 0.03834f, 0.03584f, 0.03369f, 0.03202f,
            0.03051f, 0.02905f, 0.0276f, 0.02603f, 0.02486f, 0.02387f, 0.02298f, 0.02208f, 0.02097f, 0.02013f, 0.01943f, 0.01869f,
            0.01765f, 0.01693f, 0.01632f, 0.01562f, 0.01494f, 0.01439f, 0.01375f, 0.01333f, 0.01285f, 0.0124f, 0.0121f, 0.0118f,
            0.01153f, 0.01133f, 0.01108f, 0.01095f, 0.0107f, 0.01059f, 0.01036f, 0.01026f, 0.01006f, 0.00981f, 0.00969f, 0.0096f,
            0.00946f, 0.00937f, 0.00929f, 0.0092f, 0.00913f, 0.00905f, 0.009f, 0.00894f, 0.00888f, 0.00882f, 0.00872f, 0.0086f,
            0.00843f, 0.00822f, 0.00808f, 0.00792f, 0.00778f, 0.00784f, 0.00809f, 0.00836f, 0.00855f, 0.00878f, 0.00897f, 0.00911f,
            0.00922f, 0.00932f, 0.00943f, 0.00956f, 0.00968f, 0.00982f, 0.00996f, 0.01011f, 0.01024f, 0.01042f, 0.01054f, 0.01073f,
            0.01085f, 0.01104f, 0.01119f, 0.0114f, 0.01159f, 0.01182f, 0.01204f, 0.01233f, 0.01256f, 0.01289f, 0.01321f, 0.01355f,
            0.01397f, 0.01444f, 0.01497f, 0.01555f, 0.01617f, 0.01681f, 0.0175f, 0.01823f, 0.01906f, 0.02075f, 0.02169f, 0.02266f,
            0.02366f, 0.0248f, 0.02588f, 0.02706f, 0.02845f, 0.0296f, 0.03119f, 0.03253f, 0.03413f, 0.03591f, 0.0376f, 0.03969f,
            0.04166f, 0.04399f, 0.04658f, 0.04917f, 0.05231f, 0.05519f, 0.0587f, 0.06165f, 0.06525f, 0.06904f, 0.07235f, 0.07605f,
            0.0801f, 0.08448f, 0.08788f, 0.09154f, 0.09552f, 0.09974f
        };

        /// <summary>Wing plane used for lift direction and AoA.</summary>
        private static void BuildAeroFrame(
            Vector3 cameraForward,
            out Vector3 lookNorm,
            out Vector3 bodyRight,
            out Vector3 lookAero,
            out Vector3 bodyUp)
        {
            lookNorm = cameraForward.sqrMagnitude > 0.001f ? cameraForward.normalized : Vector3.forward;
            bodyRight = Vector3.Cross(lookNorm, Vector3.up);
            if (bodyRight.sqrMagnitude < 0.001f)
            {
                bodyRight = Vector3.right;
            }

            bodyRight.Normalize();
            lookAero = Quaternion.AngleAxis(AeroPitchOffsetDegrees, bodyRight) * lookNorm;
            lookAero.Normalize();
            bodyUp = Vector3.Cross(bodyRight, lookAero).normalized;
            if (Vector3.Dot(bodyUp, Vector3.up) < 0f)
            {
                bodyUp = -bodyUp;
            }
        }

        /// <summary>
        /// Integrate one FixedUpdate-style step for elytra gliding.
        /// </summary>
        /// <param name="dt">Fixed delta time (<see cref="Time.fixedDeltaTime"/>).</param>
        /// <param name="vel">Current rigidbody velocity (modified in-place).</param>
        /// <param name="cameraForward">Camera forward (full 3D direction).</param>
        /// <param name="p">Tuning parameters from BepInEx config.</param>
        /// <param name="rigidbodyUsesUnityGravity">
        /// When true, do not add <see cref="Physics.gravity"/> here — the rigidbody already receives it in Unity's physics step.
        /// Applying it again in this postfix roughly doubles effective <i>g</i> vs vacuum √(2<i>gh</i>) expectations for steep dives.
        /// </param>
        /// <param name="horizontalSpeedOut">Horizontal speed magnitude after integration (for sync / debug).</param>
        public static void IntegrateGlide(
            float dt,
            ref Vector3 vel,
            Vector3 cameraForward,
            Params p,
            bool rigidbodyUsesUnityGravity,
            out float horizontalSpeedOut)
        {
            // Point-mass glider model:
            //   a = g + L + D, where L ⟂ v and D opposes v.
            // Camera controls body attitude, which changes angle-of-attack and thus Cl/Cd.
            BuildAeroFrame(cameraForward, out Vector3 look, out _, out _, out Vector3 bodyUp);
            float vmag = vel.magnitude;
            // Must be a unit vector; old max(|v|,0.1) trick made vHat non-unit when |v| < 0.1 and broke drag/lift direction.
            Vector3 vHat = vmag > 1e-4f ? vel / vmag : look;
            float q = vmag * vmag;

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

            // Angle of attack from geometric wing normal vs relative wind; frame uses lookAero (pitched vs camera).
            float aoa = Mathf.Asin(Mathf.Clamp(Vector3.Dot(bodyUp, -vHat), -1f, 1f));
            float aoaDeg = aoa * Mathf.Rad2Deg;
            aoaDeg = Mathf.Clamp(aoaDeg, PolarAoADeg[0], PolarAoADeg[PolarAoADeg.Length - 1]);

            InterpolatePolar(aoaDeg, out float clPolar, out float cdPolar);

            float cl = clPolar;
            float cd = cdPolar * p.DragMultiplier;
            cd = Mathf.Max(0.0001f, cd);

            float massKg = Mathf.Max(GliderMassKg, 0.1f);
            float wingArea = Mathf.Max(p.WingReferenceAreaM2, 0.01f);
            float halfRhoSOverM = (0.5f * AirDensityKgPerM3 * wingArea) / massKg;

            // --- Formula check (point mass, same S and rho for L and D) ---------------------------
            // Dynamic pressure: q_inf = 0.5 * rho * |V|^2  [Pa].  Here we fold 0.5*rho*S/m into halfRhoSOverM and use q = |V|^2:
            //   |a_L| = (0.5*rho*S/m) * Cl * |V|^2 = halfRhoSOverM * Cl * q
            //   |a_D| = (0.5*rho*S/m) * Cd * |V|^2 = halfRhoSOverM * Cd * q
            // Directions: lift along liftDir (unit, orthogonal to vHat in the camera-body plane); drag along -vHat.
            // DragMultiplier only scales Cd (effective higher parasite / trim drag for gameplay).
            Vector3 gravityAcc = rigidbodyUsesUnityGravity ? Vector3.zero : Physics.gravity;
            Vector3 liftAcc = liftDir * (halfRhoSOverM * cl * q);
            Vector3 dragAcc = -vHat * (halfRhoSOverM * cd * q);

            vel += (gravityAcc + liftAcc + dragAcc) * dt;

            // Yaw steering toward look direction (gameplay control; polar is 2D and has no yaw rate).
            Vector3 lookHoriz = new Vector3(look.x, 0f, look.z);
            if (lookHoriz.sqrMagnitude > 0.001f)
            {
                lookHoriz.Normalize();
            }
            else
            {
                lookHoriz = new Vector3(vHat.x, 0f, vHat.z).normalized;
            }

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
                float turnDragMag = p.TurnLossCoefficient * turnRate * horizSpeed;
                float postTurnSpeed = Mathf.Max(0f, horizSpeed - (turnDragMag * dt));

                vel.x = turned.x * postTurnSpeed;
                vel.z = turned.z * postTurnSpeed;
            }

            // Safety clamp only (kept to prevent numeric blowups in modded stacks).
            float cap = p.MaxGlideSpeed * 1.5f;
            if (vel.magnitude > cap)
            {
                vel = vel.normalized * cap;
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

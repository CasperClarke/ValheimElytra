using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace ValheimElytra.Flight
{
    /// <summary>
    /// Produces Minecraft-style steering vectors from the follow camera.
    /// <para>
    /// Elytra steering in Minecraft is primarily camera-forward: where you look is roughly where you accelerate
    /// and bank. Valheim's <see cref="GameCamera"/> exposes the third-person rig; we take the camera's forward
    /// vector as the glide "nose" direction.
    /// </para>
    /// </summary>
    public static class CameraSteering
    {
        private static readonly FieldInfo? GameCameraUnityCamField =
            AccessTools.Field(typeof(GameCamera), "m_camera");
        /// <summary>
        /// Returns a normalized forward vector for steering, with optional flattening to the XZ plane for banking.
        /// </summary>
        /// <param name="flattenYawOnly">
        /// When true, only yaw is respected (horizontal elytra). Minecraft still uses pitch heavily; we always
        /// compute pitch from the full forward vector separately in <see cref="FlightPhysics"/>.
        /// </param>
        public static Vector3 GetCameraForward(bool flattenYawOnly)
        {
            Camera? cam = GetMainOrGameCamera();
            if (cam == null)
            {
                return Vector3.forward;
            }

            Vector3 f = cam.transform.forward;
            if (flattenYawOnly)
            {
                f.y = 0f;
            }

            if (f.sqrMagnitude < 0.001f)
            {
                return Vector3.forward;
            }

            return f.normalized;
        }

        /// <summary>
        /// Pitch angle in degrees: negative when looking down, positive when looking up (Unity camera convention).
        /// </summary>
        public static float GetCameraPitchDegrees()
        {
            Camera? cam = GetMainOrGameCamera();
            if (cam == null)
            {
                return 0f;
            }

            // Signed angle between forward and its projection on the horizontal plane.
            Vector3 f = cam.transform.forward;
            Vector3 flat = new Vector3(f.x, 0f, f.z);
            if (flat.sqrMagnitude < 0.0001f)
            {
                return f.y > 0f ? 90f : -90f;
            }

            float pitch = Vector3.SignedAngle(flat.normalized, f.normalized, cam.transform.right);
            return pitch;
        }

        private static Camera? GetMainOrGameCamera()
        {
            if (GameCamera.instance == null)
            {
                return Camera.main;
            }

            GameCamera gc = GameCamera.instance;
            Camera? cam = GameCameraUnityCamField?.GetValue(gc) as Camera;
            return cam != null ? cam : Camera.main;
        }
    }
}

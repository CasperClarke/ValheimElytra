using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace ValheimElytra.Flight
{
    /// <summary>
    /// Visual-only flight pose controller: rotates player model toward travel direction and applies pitch.
    /// This does not alter gameplay physics transform ownership; it only adjusts the visual rig.
    /// </summary>
    public static class VisualFlightPose
    {
        private static readonly FieldInfo? VisualField = AccessTools.Field(typeof(Character), "m_visual");
        private static readonly FieldInfo? AnimatorField = AccessTools.Field(typeof(Character), "m_animator");

        private struct VisualRestore
        {
            public Quaternion Rotation;
            public bool HasRotation;
            public bool FlyingAnimSet;
        }

        private static readonly Dictionary<int, VisualRestore> RestoreByPlayerId = new Dictionary<int, VisualRestore>();

        public static void Apply(Player player, Vector3 velocity, float dt)
        {
            if (player == null || velocity.sqrMagnitude < 0.01f)
            {
                return;
            }

            GameObject? visualGo = VisualField?.GetValue(player) as GameObject;
            if (visualGo == null)
            {
                return;
            }

            Transform visual = visualGo.transform;
            int id = player.GetInstanceID();
            if (!RestoreByPlayerId.ContainsKey(id))
            {
                RestoreByPlayerId[id] = new VisualRestore
                {
                    Rotation = visual.rotation,
                    HasRotation = true,
                    FlyingAnimSet = false,
                };
            }

            // Face model toward travel direction, including pitch.
            Vector3 dir = velocity.normalized;
            Quaternion targetRot = Quaternion.LookRotation(dir, Vector3.up);
            float t = Mathf.Clamp01(dt * 8f);
            visual.rotation = Quaternion.Slerp(visual.rotation, targetRot, t);

            Animator? animator = AnimatorField?.GetValue(player) as Animator;
            if (animator != null)
            {
                TrySetAnimatorBool(animator, "flying", true);
            }
        }

        public static void Clear(Player player)
        {
            if (player == null)
            {
                return;
            }

            int id = player.GetInstanceID();
            if (!RestoreByPlayerId.TryGetValue(id, out VisualRestore restore))
            {
                return;
            }

            GameObject? visualGo = VisualField?.GetValue(player) as GameObject;
            if (visualGo != null && restore.HasRotation)
            {
                visualGo.transform.rotation = restore.Rotation;
            }

            Animator? animator = AnimatorField?.GetValue(player) as Animator;
            if (animator != null)
            {
                TrySetAnimatorBool(animator, "flying", false);
            }

            RestoreByPlayerId.Remove(id);
        }

        private static void TrySetAnimatorBool(Animator animator, string paramName, bool value)
        {
            if (animator == null)
            {
                return;
            }

            foreach (AnimatorControllerParameter p in animator.parameters)
            {
                if (p.type == AnimatorControllerParameterType.Bool && p.name == paramName)
                {
                    animator.SetBool(paramName, value);
                    return;
                }
            }
        }
    }
}

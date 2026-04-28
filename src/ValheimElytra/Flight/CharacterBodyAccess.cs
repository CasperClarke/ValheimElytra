using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace ValheimElytra.Flight
{
    /// <summary>
    /// Provides access to the Character's backing <see cref="Rigidbody"/> velocity.
    /// <para>
    /// <c>m_body</c> is not public API, but modifying <c>Rigidbody.linearVelocity</c> in <c>FixedUpdate</c> postfix is the
    /// standard way custom movement mods interact with Valheim physics.
    /// </para>
    /// </summary>
    public static class CharacterBodyAccess
    {
        private static readonly FieldInfo? BodyField = AccessTools.Field(typeof(Character), "m_body");

        public static Rigidbody? TryGetBody(Character character)
        {
            if (character == null || BodyField == null)
            {
                return null;
            }

            return BodyField.GetValue(character) as Rigidbody;
        }

        public static Vector3 GetVelocity(Character character)
        {
            Rigidbody? body = TryGetBody(character);
            return body != null ? body.linearVelocity : Vector3.zero;
        }

        public static void SetVelocity(Character character, Vector3 velocity)
        {
            Rigidbody? body = TryGetBody(character);
            if (body != null)
            {
                body.linearVelocity = velocity;
            }
        }
    }
}

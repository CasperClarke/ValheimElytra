using System.Reflection;
using HarmonyLib;

namespace ValheimElytra.Flight
{
    /// <summary>
    /// Reads <see cref="Character"/>'s networking view; <c>m_nview</c> is protected and not accessible from plugin code.
    /// </summary>
    public static class CharacterNetAccess
    {
        private static readonly FieldInfo? NviewField = AccessTools.Field(typeof(Character), "m_nview");

        public static ZNetView? GetZNetView(Character character)
        {
            if (character == null || NviewField == null)
            {
                return null;
            }

            return NviewField.GetValue(character) as ZNetView;
        }
    }
}

namespace ValheimElytra.Networking
{
    /// <summary>
    /// Deterministic string → int hashes for custom ZDO keys (same rolling-hash style Valheim uses for stable keys).
    /// Used when referencing game <c>Utils.GetStableHashCode</c> fails across assembly/API versions.
    /// </summary>
    internal static class ZdoStringHash
    {
        public static int Of(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return 0;
            }

            int hash = 5381;
            foreach (char c in text)
            {
                hash = ((hash << 5) + hash) ^ c;
            }

            return hash;
        }
    }
}

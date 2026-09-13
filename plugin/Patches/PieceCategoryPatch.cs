using HarmonyLib;
using Logger = Jotunn.Logger;

namespace OdinOnDemand.Patches
{
    /// <summary>
    ///     Gives the mod's pieces their own entry in the Valheim 1.0 build menu.
    ///     1.0 replaced the old category tab strip with <see cref="BuildUi" /> piece lists. The
    ///     "Categories" rows come from <see cref="ByUsagePieceList" />, which enumerates
    ///     <see cref="Piece.UsageTagFlags" /> and lists every flag present on an available piece,
    ///     so a piece needs a usage flag rather than a <see cref="Piece.PieceCategory" />.
    ///     Jotunn 2.30 has not updated its category support for this, which is why the pieces were
    ///     reachable only through the All row.
    /// </summary>
    public static class PieceCategoryPatch
    {
        /// <summary>
        ///     Vanilla occupies bits up to <c>Seasonal</c> (1 &lt;&lt; 19); this is the next free one.
        /// </summary>
        internal const Piece.UsageTagFlags OodUsage = (Piece.UsageTagFlags)(1 << 20);

        private const string UsageDisplayToken = "$tag_odinondemand";

        /// <summary>
        ///     Tags a registered piece so the build menu groups it under this mod's row. The legacy
        ///     category is pinned to a vanilla value because <see cref="PieceTable.UpdateAvailable" />
        ///     still buckets pieces by category into storage sized for the nine built-in values.
        /// </summary>
        internal static void Apply(Piece piece)
        {
            if (piece == null) return;

            piece.m_usage |= OodUsage;
            piece.m_category = Piece.PieceCategory.Furniture;
        }

        /// <summary>
        ///     Appends the mod's usage flag to the list the build menu enumerates. The game builds
        ///     both arrays from the enum itself, which cannot be extended, so the entry and its
        ///     display name are added afterwards; the arrays are indexed in lockstep.
        /// </summary>
        [HarmonyPatch(typeof(ByUsagePieceList), MethodType.Constructor, typeof(string))]
        private static class ByUsagePieceListPatch
        {
            private static void Postfix(ByUsagePieceList __instance)
            {
                var tagsField = AccessTools.Field(typeof(ByUsagePieceList), "m_usageTags");
                var namesField = AccessTools.Field(typeof(ByUsagePieceList), "m_usageTagDisplayNames");
                if (tagsField == null || namesField == null) return;

                var tags = (Piece.UsageTagFlags[])tagsField.GetValue(__instance);
                var names = (string[])namesField.GetValue(__instance);
                if (tags == null || names == null || tags.Length != names.Length) return;

                foreach (var tag in tags)
                {
                    if (tag == OodUsage) return;
                }

                var grownTags = new Piece.UsageTagFlags[tags.Length + 1];
                var grownNames = new string[names.Length + 1];
                tags.CopyTo(grownTags, 0);
                names.CopyTo(grownNames, 0);
                grownTags[tags.Length] = OodUsage;
                grownNames[names.Length] = UsageDisplayToken;

                tagsField.SetValue(__instance, grownTags);
                namesField.SetValue(__instance, grownNames);
                Logger.LogDebug($"Added build menu usage tag {UsageDisplayToken} at index {tags.Length}");
            }
        }
    }
}

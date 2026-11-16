using ThunderRoad;

namespace GettingOverIt
{
    public static class ModOptions
    {
        [ModOption("Getting Over it with Crystal Hunt", "A", nameLocalizationId = "GettingOverItOpt.CrystalHunt",
            tooltipLocalizationId = "GettingOverItOpt.CrystalHunt_desc", defaultValueIndex = 0, order = 0)]
        [ModOptionCategory("Advanced", 0, "GettingOverItOptCat.Advanced")]
        public static bool EnableCrystalHunt = false;
    }

    public sealed class LevelActivation
    {
        private bool crystalHuntForLevel;

        // Both enabling and disabling take effect at the next level load, never mid-session.
        public void BeginLevel(bool requestedCrystalHunt) { crystalHuntForLevel = requestedCrystalHunt; }

        public bool ShouldApply(string gameMode, string levelId, string modeName, bool dungeon)
        {
            if (!GettingIt.Eligible(levelId)) return false;
            if (gameMode == "Sandbox") return modeName == GettingIt.ModeName;
            return gameMode == "CrystalHunt" && crystalHuntForLevel && dungeon;
        }
    }
}

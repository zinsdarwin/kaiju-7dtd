using System.Collections.Generic;
using HarmonyLib;

namespace KaijuMod
{
    /// <summary>
    /// Keeps quests away from POIs that have no "Rally" block (no quest start marker), so a quest
    /// can't send you somewhere it can never start. Maps made before tools/snakemap 0.4.2 placed
    /// some (house_old_cottage_01_sleeper and _detail, house_old_gambrel_04). Works in any world;
    /// affects only quests handed out from now on.
    /// </summary>
    public static class KaijuQuestPoiFilter
    {
        // Prefab name -> has a Rally block. Prefabs don't change while the game runs.
        private static readonly Dictionary<string, bool> hasRally = new Dictionary<string, bool>();
        private static readonly System.Predicate<PrefabInstance> unquestable = p => !Questable(p);

        public static bool Questable(PrefabInstance poi)
        {
            if (poi == null || poi.prefab == null)
                return true;
            string name = poi.prefab.PrefabName ?? poi.name ?? "";
            bool ok;
            if (!hasRally.TryGetValue(name, out ok))
            {
                ok = GameApi.HasRallyBlock(poi);
                hasRally[name] = ok;
                if (!ok)
                    Log.Out("[KaijuMod] Quests skip " + name + ": it has no quest start marker (Rally block)");
            }
            return ok;
        }

        // VERIFIED (V3.3): trader quests pick their POI with DynamicPrefabDecorator.GetRandomPOINearTrader,
        // which takes the first candidate ValidPrefabForQuest accepts.
        [HarmonyPatch(typeof(DynamicPrefabDecorator), nameof(DynamicPrefabDecorator.ValidPrefabForQuest))]
        private static class TraderQuestPatch
        {
            private static void Postfix(PrefabInstance prefab, ref bool __result)
            {
                if (__result && !Questable(prefab))
                    __result = false;
            }
        }

        // VERIFIED (V3.3): quests not tied to a trader pick at random from
        // QuestEventManager.GetPrefabsByDifficultyTier, a list it builds once and caches.
        // Dropping the unquestable POIs from it is permanent and cheap after the first call.
        [HarmonyPatch(typeof(QuestEventManager), nameof(QuestEventManager.GetPrefabsByDifficultyTier))]
        private static class RandomQuestPatch
        {
            private static void Postfix(List<PrefabInstance> __result)
            {
                if (__result != null)
                    __result.RemoveAll(unquestable);
            }
        }
    }
}

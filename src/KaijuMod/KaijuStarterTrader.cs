using HarmonyLib;
using UnityEngine;

namespace KaijuMod
{
    /// <summary>
    /// The opening quest ("find a trader") goes to the trader in the town nearest the start city
    /// (Trader Jen in the generated map): the start city is gone by then, and that town is where
    /// The Run really begins. The game would instead pick the pine forest trader with the most
    /// quest POIs around it, which on the snake map can be kilometres away. Only in worlds with a
    /// kaiju.xml.
    /// </summary>
    public static class KaijuStarterTrader
    {
        /// <summary>The trader POI in the town nearest the start city, or null (no kaiju.xml, or none found).</summary>
        public static PrefabInstance Find()
        {
            var data = KaijuRun.Instance.Data;
            if (data == null || data.StartCity == null)
                return null;
            var towns = data.Settlements.FindAll(s => s.Strip == 0 && s.Kind == "town");
            if (towns.Count == 0)
                return null;
            float startX = data.StartCity.X;
            towns.Sort((a, b) => Mathf.Abs(a.X - startX).CompareTo(Mathf.Abs(b.X - startX)));
            var town = towns[0];
            foreach (var p in GameApi.Pois())
            {
                string name = p.prefab != null ? p.prefab.PrefabName : p.name;
                if (name == null || !name.StartsWith("trader_"))
                    continue;
                float cx = p.boundingBoxPosition.x + p.boundingBoxSize.x / 2f;
                float cz = p.boundingBoxPosition.z + p.boundingBoxSize.z / 2f;
                if (Mathf.Abs(cx - town.X) <= town.HalfWidth + 20f && Mathf.Abs(cz - town.Z) <= 2.2f * CityAttack.BlockSpacing + 20f)
                    return p;
            }
            return null;
        }

        // VERIFIED (V3.3): ObjectiveGoto (quest_whiteRiverCitizen1, unique_key "traderquest") finds its
        // trader with DynamicPrefabDecorator.GetClosestPOIToWorldPos(..., questKey); for "traderquest"
        // that returns chooseBestTrader: the candidate with the most tier 1 quest POIs, not the nearest.
        [HarmonyPatch(typeof(DynamicPrefabDecorator), nameof(DynamicPrefabDecorator.GetClosestPOIToWorldPos))]
        private static class StarterQuestPatch
        {
            private static void Postfix(string questKey, ref PrefabInstance __result)
            {
                if (!string.Equals(questKey, "traderquest", System.StringComparison.OrdinalIgnoreCase))
                    return;
                var trader = Find();
                if (trader == null)
                    return;
                if (trader != __result)
                    Log.Out("[KaijuMod] Starter quest: sending you to " + trader.name + ", the trader nearest the start city");
                __result = trader;
            }
        }
    }
}

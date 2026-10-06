using HarmonyLib;
using UnityEngine;

namespace KaijuMod
{
    /// <summary>
    /// Points the game's trader-to-trader quests along The Run (worlds with a kaiju.xml only):
    /// - The opening quest ("find a trader", pine forest only) goes to the trader in the town
    ///   nearest the start city (Trader Jen in the generated map): the start city is gone by then.
    /// - "Opening Trade Routes" (tierN_nexttrader) goes to the first trader in the next strip, the
    ///   town nearest the pass the run takes, never back into the radiation.
    /// The game would pick the pine forest trader with the most quest POIs around it, or the
    /// nearest trader of a given name, either of which can be kilometres back toward the radiation.
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
            return TraderIn(towns[0]);
        }

        /// <summary>
        /// The trader in the first town of the strip after the one at 'from' (the town nearest the
        /// pass beside this strip's end city), or null on the last strip or without a kaiju.xml.
        /// </summary>
        public static PrefabInstance NextBiomeTrader(Vector2 from)
        {
            var data = KaijuRun.Instance.Data;
            if (data == null)
                return null;
            int strip = data.StripOf(from.y);
            Settlement end = data.EndCity(strip);
            if (end == null || strip + 1 >= data.Strips)
                return null;
            var towns = data.Settlements.FindAll(s => s.Strip == strip + 1 && s.Kind == "town");
            if (towns.Count == 0)
                return null;
            towns.Sort((a, b) => Mathf.Abs(a.X - end.X).CompareTo(Mathf.Abs(b.X - end.X)));
            return TraderIn(towns[0]);
        }

        private static PrefabInstance TraderIn(Settlement town)
        {
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

        /// <summary>
        /// Moves an "Opening Trade Routes" quest already in the journal, still on its way, to the
        /// next strip's first trader (saves made before this fix sent it back toward the radiation).
        /// </summary>
        public static void RepairJournal(EntityPlayer player)
        {
            if (player == null || player.QuestJournal == null || KaijuRun.Instance.Data == null)
                return;
            // VERIFIED (V3.3): QuestJournal.quests; Quest.CurrentState/CurrentPhase/Objectives/
            // GetPositionData/SetPositionData; ObjectiveGoto.FinalizePoint sets POIPosition/POISize,
            // its destination and map marker.
            foreach (var q in player.QuestJournal.quests)
            {
                if (q == null || q.QuestClass == null || q.CurrentState != Quest.QuestState.InProgress || q.CurrentPhase > 1)
                    continue;
                if (q.QuestClass.ID == null || q.QuestClass.ID.ToLowerInvariant().IndexOf("nexttrader") < 0)
                    continue;
                Vector3 giver;
                if (!q.GetPositionData(out giver, Quest.PositionDataTypes.QuestGiver))
                    giver = player.position;
                var t = NextBiomeTrader(new Vector2(giver.x, giver.z));
                if (t == null)
                    continue;
                var pos = new Vector3(t.boundingBoxPosition.x, t.boundingBoxPosition.y, t.boundingBoxPosition.z);
                var size = new Vector3(t.boundingBoxSize.x, t.boundingBoxSize.y, t.boundingBoxSize.z);
                Vector3 cur;
                if (q.GetPositionData(out cur, Quest.PositionDataTypes.POIPosition) && cur == pos)
                    continue;
                q.SetPositionData(Quest.PositionDataTypes.POIPosition, pos);
                q.SetPositionData(Quest.PositionDataTypes.POISize, size);
                q.Position = pos + size * 0.5f;
                foreach (var o in q.Objectives)
                {
                    var g = o as ObjectiveGoto;
                    if (g != null)
                        g.FinalizePoint(pos, size);
                }
                Log.Out("[KaijuMod] Opening Trade Routes now leads to " + t.name + " in the next biome");
            }
        }

        // VERIFIED (V3.3): ObjectiveGoto finds its trader with DynamicPrefabDecorator.GetClosestPOIToWorldPos
        // (..., biomeFilterType, biomeFilter, questKey). Both the opening quest and tierN_nexttrader
        // use questKey "traderquest" (their unique_key); the opening quest alone filters OnlyBiome
        // (pine_forest), the next-trader quests use AnyBiome.
        [HarmonyPatch(typeof(DynamicPrefabDecorator), nameof(DynamicPrefabDecorator.GetClosestPOIToWorldPos))]
        private static class TraderQuestPatch
        {
            private static void Postfix(Vector2 worldPos, BiomeFilterTypes biomeFilterType, string questKey, ref PrefabInstance __result)
            {
                if (!string.Equals(questKey, "traderquest", System.StringComparison.OrdinalIgnoreCase))
                    return;
                try
                {
                    bool opening = biomeFilterType == BiomeFilterTypes.OnlyBiome;
                    var trader = opening ? Find() : NextBiomeTrader(worldPos);
                    if (trader == null)
                        return;
                    if (trader != __result)
                        Log.Out("[KaijuMod] " + (opening ? "Starter quest" : "Opening Trade Routes") + ": sending you to " + trader.name);
                    __result = trader;
                }
                catch (System.Exception e)
                {
                    Log.Warning("[KaijuMod] Trader quest target unchanged: " + e.Message);
                }
            }
        }
    }
}

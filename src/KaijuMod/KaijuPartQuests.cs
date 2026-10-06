using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace KaijuMod
{
    /// <summary>
    /// The trader in each part city offers a special quest for that city's Oxygen Destroyer part,
    /// at the top of their job list: go to the part's military site (map marker), get the part,
    /// return to the trader. Quests kaijuPart1-5 in Config/quests.xml, built from vanilla
    /// objectives (Goto at a preset position, FetchKeep, ReturnToNPC, InteractWithNPC). Not offered
    /// once the part is held, the device is built or armed, the crate is emptied, or the quest is
    /// active or done. Works in any world with a kaiju.xml.
    /// </summary>
    public static class KaijuPartQuests
    {
        public static string QuestId(int part) { return "kaijuPart" + (part + 1); }

        /// <summary>A part quest for this trader, set up to point at the part's site, or null.</summary>
        public static Quest Create(EntityTrader trader, int part, Vector3 sitePos, Vector3 siteSize)
        {
            // VERIFIED (V3.3): QuestClass.GetQuest (case-insensitive), CreateQuest, and the fields
            // EntityTrader.PopulateActiveQuests sets on the quests it offers.
            QuestClass qc = QuestClass.GetQuest(QuestId(part));
            if (qc == null)
                return null;
            Quest q = qc.CreateQuest();
            q.QuestGiverID = trader.entityId;
            q.QuestFaction = trader.NPCInfo != null ? trader.NPCInfo.QuestFaction : (byte)0;
            q.SetPositionData(Quest.PositionDataTypes.QuestGiver, trader.position);
            q.SetPositionData(Quest.PositionDataTypes.TraderPosition, trader.traderArea != null ? (Vector3)trader.traderArea.Position : trader.position);
            // VERIFIED (V3.3): ObjectiveGoto.GetPosition uses preset POIPosition/POISize position data
            // as its destination (and map marker) instead of searching for a POI.
            q.SetPositionData(Quest.PositionDataTypes.POIPosition, sitePos);
            q.SetPositionData(Quest.PositionDataTypes.POISize, siteSize);
            q.Position = sitePos + siteSize * 0.5f;
            q.SetupTags();
            return q;
        }

        // VERIFIED (V3.3): EntityTrader.PopulateActiveQuests builds the job list the trader shows
        // (SetupActiveQuestsForPlayer stores it); it returns null if the trader has no quests.
        [HarmonyPatch(typeof(EntityTrader), nameof(EntityTrader.PopulateActiveQuests))]
        private static class OfferPatch
        {
            private static void Postfix(EntityTrader __instance, EntityPlayer player, ref List<Quest> __result)
            {
                if (__instance == null || player == null)
                    return;
                Vector3 sitePos, siteSize;
                int part = KaijuRun.Instance.PartQuestFor(__instance, player, out sitePos, out siteSize);
                if (part < 0)
                    return;
                Quest q = Create(__instance, part, sitePos, siteSize);
                if (q == null)
                    return;
                if (__result == null)
                    __result = new List<Quest>();
                __result.Insert(0, q);
                Log.Out("[KaijuMod] " + __instance.EntityName + " offers the Oxygen Destroyer part " + (part + 1) + " quest");
            }
        }
    }
}

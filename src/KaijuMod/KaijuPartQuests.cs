using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace KaijuMod
{
    /// <summary>
    /// The trader in each town of the burnt, desert, snow and wasteland strips offers a special quest
    /// for that town's weapon part, at the top of their job list: go to the marked building in town,
    /// get the part, return to the trader. Quests kaijuPart1-12 in Config/quests.xml, built from vanilla
    /// objectives (Goto at a preset position, FetchKeep, ReturnToNPC, InteractWithNPC). Not offered
    /// once the part or its weapon is held, the crate is emptied, or the quest is
    /// active or done. Works in any world with a kaiju.xml.
    /// </summary>
    public static class KaijuPartQuests
    {
        public static string QuestId(int part) { return "kaijuPart" + (part + 1); }
        /// <summary>The part's quest at a trader tier (1-5): offered at the tier you are on with that trader.</summary>
        public static string QuestId(int part, int tier) { return QuestId(part) + "_t" + Mathf.Clamp(tier, 1, 5); }

        /// <summary>True if the player has the part's quest, at any tier (or the old tier 1 one), active or done.</summary>
        public static bool HasQuest(EntityPlayer player, int part)
        {
            var journal = player != null ? player.QuestJournal : null;
            if (journal == null)
                return false;
            if (journal.FindActiveOrCompleteQuest(QuestId(part)) != null)
                return true;
            for (int t = 1; t <= 5; t++)
                if (journal.FindActiveOrCompleteQuest(QuestId(part, t)) != null)
                    return true;
            return false;
        }

        /// <summary>A part quest for this trader, set up to point at the part's site, or null.</summary>
        public static Quest Create(EntityTrader trader, EntityPlayer player, int part, Vector3 sitePos, Vector3 siteSize)
        {
            // VERIFIED (V3.3): QuestJournal.GetCurrentFactionTier(faction), the tier the job list opens on.
            byte faction = trader.NPCInfo != null ? trader.NPCInfo.QuestFaction : (byte)0;
            int tier = player != null && player.QuestJournal != null ? player.QuestJournal.GetCurrentFactionTier(faction) : 1;
            return Create(trader, QuestId(part, tier), sitePos, siteSize);
        }

        /// <summary>A quest by id from this trader, its Goto preset to a place, or null.</summary>
        public static Quest Create(EntityTrader trader, string questId, Vector3 sitePos, Vector3 siteSize)
        {
            // VERIFIED (V3.3): QuestClass.GetQuest (case-insensitive), CreateQuest, and the fields
            // EntityTrader.PopulateActiveQuests sets on the quests it offers.
            QuestClass qc = QuestClass.GetQuest(questId);
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
            private static void Postfix(EntityTrader __instance, EntityPlayer player, List<Quest> __result)
            {
                // Never let this cost the trader its vanilla quests: only add to a list that exists,
                // and swallow any failure.
                if (__instance == null || player == null || __result == null)
                    return;
                try
                {
                    KaijuStarterTrader.OfferNextTrader(__instance, player, __result);
                    Vector3 sitePos, siteSize;
                    int part = KaijuRun.Instance.PartQuestFor(__instance, player, out sitePos, out siteSize);
                    if (part < 0)
                        return;
                    Quest q = Create(__instance, player, part, sitePos, siteSize);
                    if (q == null)
                        return;
                    __result.Insert(0, q);
                    Log.Out("[KaijuMod] " + __instance.EntityName + " offers the " + KaijuRun.PartItem(part) + " quest");
                }
                catch (System.Exception e)
                {
                    Log.Warning("[KaijuMod] Part quest not offered: " + e.Message);
                }
            }
        }
    }
}

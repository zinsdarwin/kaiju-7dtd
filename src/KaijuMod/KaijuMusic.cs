using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using DynamicMusic;
using HarmonyLib;
using MusicUtils.Enums;
using UnityEngine;

namespace KaijuMod
{
    /// <summary>
    /// The blood moon horde music plays while Godzilla is attacking a city (and a little after he
    /// leaves), using the game's own dynamic music. Outside his attacks the music works as usual.
    /// The blood moon mixer fades its layers in by the player's threat level, which would leave it
    /// near silent with no zombies about, so during an attack that one mixer is told the threat is
    /// high. The player's real threat level, used elsewhere for ambient sounds and prompts, is
    /// untouched. Respects the music settings: nothing plays if music or dynamic music is off.
    /// </summary>
    public static class KaijuMusic
    {
        /// <summary>Seconds the music keeps going after he leaves.</summary>
        public static float LingerSeconds = 20f;
        /// <summary>Intensity the blood moon music plays at during an attack; 0.95+ puts every layer at full.</summary>
        public static float Intensity = 0.97f;
        public static bool Enabled = true;

        private static float lastSeen = -999f;
        private static float nextRestart;
        // Read on the audio thread (the music is a streamed clip), so only set on the main thread.
        private static volatile bool active;

        /// <summary>Updated each frame by the music selector (main thread).</summary>
        private static void Refresh()
        {
            var d = KaijuDirector.Instance;
            if (d.Running && d.Attacking != null)
                lastSeen = Time.time;
            active = Enabled && Time.time - lastSeen < LingerSeconds;
        }

        private static bool MusicAllowed()
        {
            // Same checks as SectionSelector.Select (V3.3).
            return !SectionSelector.IsDMSTempDisabled
                && GamePrefs.GetFloat(EnumGamePrefs.OptionsMusicVolumeLevel) > 0f
                && GamePrefs.GetBool(EnumGamePrefs.OptionsDynamicMusicEnabled);
        }

        private static bool PlayerAlive()
        {
            var p = GameManager.Instance.World?.GetPrimaryPlayer();
            return p != null && p.IsAlive();
        }

        /// <summary>The intensity the blood moon mixer sees: the player's threat level, or full during an attack.</summary>
        public static float MixerThreat(float threat)
        {
            return active ? Mathf.Max(threat, Intensity) : threat;
        }

        // VERIFIED (V3.3): Conductor.Update fades to whatever SectionSelector.Select returns, every frame.
        [HarmonyPatch(typeof(SectionSelector), nameof(SectionSelector.Select))]
        private static class SelectPatch
        {
            private static void Postfix(ref SectionType __result)
            {
                Refresh();
                if (!active || !MusicAllowed() || !PlayerAlive())
                    return;
                __result = SectionType.Bloodmoon;
                RestartIfEnded(__result);
            }
        }

        // VERIFIED (V3.3): BloodmoonLayerMixer's indexer turns each layer on/hi by the local player's
        // ThreatLevel.Numeric (dmscontent.xml: Primary 0.8/0.95, Supporting 0.7/0.85, Secondary
        // 0.75/0.95, LongEffects 0.9/1.0). Pass that one read through MixerThreat.
        [HarmonyPatch(typeof(BloodmoonLayerMixer), "get_Item")]
        private static class MixerPatch
        {
            private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> code)
            {
                MethodInfo numeric = AccessTools.PropertyGetter(typeof(IThreatLevel), nameof(IThreatLevel.Numeric));
                MethodInfo boost = AccessTools.Method(typeof(KaijuMusic), nameof(MixerThreat));
                int patched = 0;
                foreach (var ins in code)
                {
                    yield return ins;
                    if ((ins.opcode == OpCodes.Callvirt || ins.opcode == OpCodes.Call) && Equals(ins.operand, numeric))
                    {
                        yield return new CodeInstruction(OpCodes.Call, boost);
                        patched++;
                    }
                }
                if (patched == 0)
                    Log.Warning("[KaijuMod] Blood moon music mixer not patched (game update?); attack music may be quiet");
                else
                    Log.Out("[KaijuMod] Blood moon music mixer patched for Godzilla attacks");
            }
        }

        /// <summary>
        /// The conductor only starts a section when the choice changes, so a piece that finishes
        /// while the choice stays the same would leave silence. Start it again.
        /// </summary>
        private static void RestartIfEnded(SectionType chosen)
        {
            if (Time.time < nextRestart)
                return;
            var conductor = GameManager.Instance.World?.dmsConductor;
            if (conductor == null || conductor.CurrentSectionType != chosen)
                return;
            var section = conductor.CurrentSection;
            if (section == null || section.IsPlaying || section.IsPaused)
                return;
            nextRestart = Time.time + 3f;
            section.FadeIn();
        }
    }
}

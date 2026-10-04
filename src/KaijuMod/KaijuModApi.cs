using System.Reflection;
using HarmonyLib;

namespace KaijuMod
{
    /// <summary>
    /// Entry point the game calls when it loads the mod (any class implementing IModApi).
    /// </summary>
    public class KaijuModApi : IModApi
    {
        public const string HarmonyId = "com.darwin.kaijumod";
        /// <summary>The mod's version, from ModInfo.xml (the one place it is set; bump it with every change).</summary>
        public static string Version = "?";

        public void InitMod(Mod modInstance)
        {
            // VERIFIED (V3.3): Mod.VersionString is ModInfo.xml's Version value.
            Version = modInstance != null && !string.IsNullOrEmpty(modInstance.VersionString) ? modInstance.VersionString : "?";
            Log.Out("[KaijuMod] Loading v" + Version);
            var harmony = new Harmony(HarmonyId);
            harmony.PatchAll(Assembly.GetExecutingAssembly());
            Log.Out("[KaijuMod] Harmony patches applied");
            KaijuRunner.Create();
            Log.Out("[KaijuMod] Director ready. Console: kaiju test, kaiju start, kaiju help");
        }
    }
}

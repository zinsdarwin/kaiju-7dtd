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

        public void InitMod(Mod modInstance)
        {
            Log.Out("[KaijuMod] Loading v0.1.0");
            var harmony = new Harmony(HarmonyId);
            harmony.PatchAll(Assembly.GetExecutingAssembly());
            Log.Out("[KaijuMod] Harmony patches applied");
            KaijuRunner.Create();
            Log.Out("[KaijuMod] Director ready. Console: kaiju test, kaiju start, kaiju help");
        }
    }
}

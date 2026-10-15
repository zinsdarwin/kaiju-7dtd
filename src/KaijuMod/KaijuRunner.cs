using UnityEngine;

namespace KaijuMod
{
    /// <summary>
    /// Plain Unity MonoBehaviour that ticks the director once per frame. Using Unity's own Update
    /// avoids depending on the game's ModEvents tick signature, which has changed between versions.
    /// </summary>
    public class KaijuRunner : MonoBehaviour
    {
        public static void Create()
        {
            var go = new GameObject("KaijuRunner");
            DontDestroyOnLoad(go);
            go.AddComponent<KaijuRunner>();
        }

        private void Update()
        {
            try
            {
                // Time.deltaTime is 0 while the single player game is paused (if the game pauses
                // via timeScale; to verify), so he stops with it.
                long t = KaijuPerf.Begin();
                if (KaijuPerf.On("director"))
                    KaijuDirector.Instance.Tick(Time.deltaTime);
                KaijuPerf.End("director", t);
                KaijuRun.Instance.Tick(Time.deltaTime); // times its own parts
                t = KaijuPerf.Begin();
                if (KaijuPerf.On("zombies") || KaijuPerf.On("skins"))
                    KaijuZombies.Tick(Time.deltaTime);
                KaijuPerf.End("zombies", t);
            }
            catch (System.Exception e)
            {
                // One bad frame must not spam the log every frame after it.
                Log.Error("[KaijuMod] Director stopped after an error: " + e);
                KaijuDirector.Instance.Stop();
            }
        }

        private void LateUpdate()
        {
            try
            {
                // After animation: the breath aims the head and puts the beam on the mouth.
                long t = KaijuPerf.Begin();
                if (KaijuPerf.On("director"))
                    KaijuDirector.Instance.LateTick();
                KaijuPerf.End("director", t);
                KaijuPerf.EndFrame();
            }
            catch (System.Exception e)
            {
                Log.Error("[KaijuMod] Director stopped after an error: " + e);
                KaijuDirector.Instance.Stop();
            }
        }
    }
}

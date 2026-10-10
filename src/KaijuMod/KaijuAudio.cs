using System.Collections.Generic;
using UnityEngine;

namespace KaijuMod
{
    /// <summary>
    /// Godzilla's sounds, from vanilla clips played on our own Unity AudioSources so they can be
    /// pitched down and heard from far away: the atomic blast (big explosion plus smaller ones going
    /// off around it) and his roar (a bear and a dire wolf, pitched way down and layered).
    /// </summary>
    public static class KaijuAudio
    {
        // Vanilla clips (Data/Config/sounds.xml), loaded by path like the game's own audio manager.
        private static readonly string[] BigBlast = { "@:Sounds/Explosions/explosion3.wav", "@:Sounds/Explosions/explosion1.wav" };
        private static readonly string[] SmallBlasts =
        {
            "@:Sounds/Explosions/explosion_charge1.wav", "@:Sounds/Explosions/explosion_charge2.wav",
            "@:Sounds/Explosions/explosion1.wav", "@:Sounds/Explosions/explosion3.wav",
        };
        /// <summary>The supply plane's drone, pitched up for the jets.</summary>
        public const string JetClip = "@:Sounds/SupplyDrops/Supply_Crate_Plane_lp.wav";
        private const string ArcClip = "@:Sounds/Electricity/ElectricFence/electric_arc_lp.wav";
        private static readonly string[] Roars = { "@:Sounds/Animals/Bear/bearalert.wav", "@:Sounds/Animals/Wolf/wolfdirealert1.wav" };

        /// <summary>Roar pitch: about an octave below the bear.</summary>
        public static float RoarPitch = 0.45f;
        public static float RoarRange = 3000f;
        public static float BlastRange = 4000f;

        private static readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();

        private static AudioClip Clip(string path)
        {
            AudioClip c;
            if (clips.TryGetValue(path, out c))
                return c;
            c = GameApi.LoadAudioClip(path);
            if (c == null)
                Log.Warning("[KaijuMod] Sound not found: " + path);
            clips[path] = c;
            return c;
        }

        /// <summary>The atomic blast: a deep double boom, then smaller explosions over the next seconds.</summary>
        public static void Blast(Vector3 worldPos, float radius)
        {
            for (int i = 0; i < BigBlast.Length; i++)
                Play(worldPos, Clip(BigBlast[i]), 0.55f + 0.1f * i, 1f, BlastRange, 0.05f * i);
            int n = Random.Range(6, 11);
            for (int i = 0; i < n; i++)
            {
                Vector2 off = Random.insideUnitCircle * radius * 2f;
                Play(worldPos + new Vector3(off.x, 0f, off.y), Clip(SmallBlasts[Random.Range(0, SmallBlasts.Length)]),
                    Random.Range(0.8f, 1.1f), 0.8f, 1500f, Random.Range(0.5f, 6f));
            }
        }

        /// <summary>A missile leaving its launcher.</summary>
        public static void Launch(Vector3 worldPos)
        {
            Play(worldPos, Clip(SmallBlasts[0]), Random.Range(1.3f, 1.6f), 0.7f, 800f, 0f);
        }

        /// <summary>A missile hitting him: a sharp explosion heard a long way off.</summary>
        public static void MissileHit(Vector3 worldPos)
        {
            Play(worldPos, Clip(SmallBlasts[Random.Range(0, SmallBlasts.Length)]), Random.Range(0.9f, 1.1f), 1f, 2000f, 0f);
        }

        /// <summary>A looping sound that follows a moving object (a jet) until it is destroyed.</summary>
        public static void Loop(GameObject go, string clipPath, float pitch, float volume, float range)
        {
            var clip = Clip(clipPath);
            if (clip == null)
                return;
            var src = go.AddComponent<AudioSource>();
            src.clip = clip;
            src.loop = true;
            src.pitch = pitch;
            src.volume = volume;
            src.spatialBlend = 1f;
            src.rolloffMode = AudioRolloffMode.Linear;
            src.minDistance = range * 0.1f;
            src.maxDistance = range;
            src.dopplerLevel = 1f;
            src.Play();
        }

        /// <summary>The maser firing: a deep crackling arc for the length of the shot, and a bang as it starts.</summary>
        public static void MaserZap(Vector3 worldPos)
        {
            Play(worldPos, Clip(SmallBlasts[1]), 1.4f, 1f, 1500f, 0f);
            var go = new GameObject("KaijuMaserSound");
            Object.DontDestroyOnLoad(go);
            go.AddComponent<SoundAnchor>().Init(worldPos);
            Loop(go, ArcClip, 0.6f, 1f, 1500f);
            Object.Destroy(go, KaijuMaser.Duration);
        }

        /// <summary>His roar from a point (his mouth): two creature calls, pitched down, layered.</summary>
        public static void Roar(Vector3 worldPos, float delay = 0f)
        {
            Play(worldPos, Clip(Roars[0]), RoarPitch, 1f, RoarRange, delay);
            Play(worldPos, Clip(Roars[1]), RoarPitch * 0.9f, 0.8f, RoarRange, delay + 0.15f);
        }

        private static void Play(Vector3 worldPos, AudioClip clip, float pitch, float volume, float range, float delay)
        {
            if (clip == null)
                return;
            var go = new GameObject("KaijuSound");
            Object.DontDestroyOnLoad(go);
            go.AddComponent<SoundAnchor>().Init(worldPos);
            var src = go.AddComponent<AudioSource>();
            src.clip = clip;
            src.pitch = pitch;
            src.volume = volume;
            src.spatialBlend = 1f;
            src.rolloffMode = AudioRolloffMode.Linear;
            src.minDistance = range * 0.1f;
            src.maxDistance = range;
            src.dopplerLevel = 0f;
            src.PlayDelayed(delay);
            Object.Destroy(go, delay + clip.length / Mathf.Max(0.1f, pitch) + 0.5f);
        }

        private class SoundAnchor : WorldAnchored
        {
            public void Init(Vector3 pos)
            {
                worldPos = pos;
                LateUpdate();
            }
        }
    }
}

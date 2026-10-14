using System.Collections.Generic;
using UnityEngine;

namespace KaijuMod
{
    /// <summary>
    /// G-cell zombies (radiated, charged, infernal) near you now and then spit a small atomic breath
    /// at you, and blow up in a small atomic blast when they die: a blue flash, a ground shockwave,
    /// a puff of smoke and damage to anyone close, bigger each tier. No blocks are destroyed, so
    /// horde bases survive. `kaiju zbreath on|off` and `kaiju zblast on|off` turn them off.
    /// </summary>
    public static class KaijuZombies
    {
        public static bool BreathOn = true, BlastOn = true;
        /// <summary>Per stage 2 (radiated), 3 (charged), 4 (infernal): breath range m, seconds between breaths, damage; blast radius m, damage.</summary>
        public static readonly float[] BreathRange = { 0, 0, 22f, 28f, 36f };
        public static readonly float[] BreathCooldown = { 0, 0, 22f, 15f, 9f };
        public static readonly int[] BreathDamage = { 0, 0, 8, 12, 18 };
        public static readonly float[] BlastRadius = { 0, 0, 3f, 4f, 5.5f };
        public static readonly int[] BlastDamage = { 0, 0, 15, 25, 40 };
        /// <summary>Only zombies this close to you are looked at, metres; at most this many blasts a second.</summary>
        public static float WatchRange = 60f;
        public static int MaxBlastsPerSecond = 4;

        private static readonly Dictionary<int, float> nextBreath = new Dictionary<int, float>();
        private static readonly HashSet<int> blasted = new HashSet<int>();
        private static float timer, blastWindow;
        private static int blastsThisWindow;

        // Zombies whose skins have been checked (the model hook does most; this catches the rest).
        private static readonly HashSet<int> skinned = new HashSet<int>();

        public static void Tick(float dt)
        {
            timer += dt;
            if (timer < 0.25f)
                return;
            timer = 0f;
            World world = GameApi.World;
            EntityPlayer player = world != null ? GameApi.LocalPlayer(world) : null;
            if (player == null)
                return;
            if (Time.time > blastWindow)
            {
                blastWindow = Time.time + 1f;
                blastsThisWindow = 0;
            }
            Vector3 pp = GameApi.Position(player);
            // VERIFIED (V3.3): World.Entities (DictionaryList, .list), Entity.EntityClass.entityClassName,
            // Entity.IsDead, Entity.getHeadPosition, EntityAlive.CanSee(EntityAlive).
            foreach (Entity e in world.Entities.list)
            {
                var z = e as EntityZombie;
                if (z == null || z.EntityClass == null)
                    continue;
                int stage = KaijuGCell.StageOf(z.EntityClass.entityClassName);
                Vector3 zp = z.position;
                float dist = Vector3.Distance(zp, pp);
                if (dist > WatchRange)
                    continue;
                // VERIFIED (V3.3): Entity.emodel is the entity's EModelBase.
                // Until all its skins are done (new skins are made a few a second).
                if (KaijuGCell.SkinsOn && z.emodel != null && !skinned.Contains(z.entityId) && !KaijuGCell.Plain.Contains(z.entityId)
                    && KaijuGCell.Ready && KaijuGCell.Apply(z.emodel.gameObject, z.EntityClass.entityClassName))
                    skinned.Add(z.entityId);
                if (stage < 2 || (!BreathOn && !BlastOn))
                    continue;
                if (z.IsDead())
                {
                    if (BlastOn && blasted.Add(z.entityId) && blastsThisWindow < MaxBlastsPerSecond)
                    {
                        blastsThisWindow++;
                        Blast(world, zp + Vector3.up, stage, z);
                    }
                    continue;
                }
                if (!BreathOn || !GameApi.IsAlive(player) || dist > BreathRange[stage] || dist < 3f)
                    continue;
                float next;
                if (!nextBreath.TryGetValue(z.entityId, out next))
                {
                    // Not all at once: each starts somewhere in its cooldown.
                    nextBreath[z.entityId] = Time.time + Random.Range(2f, BreathCooldown[stage]);
                    continue;
                }
                if (Time.time < next || !z.CanSee(player))
                    continue;
                nextBreath[z.entityId] = Time.time + BreathCooldown[stage] * Random.Range(0.8f, 1.25f);
                ZombieBolt.Fire(z.getHeadPosition(), player, BreathDamage[stage], stage);
            }
            if (nextBreath.Count > 500 || blasted.Count > 2000 || skinned.Count > 5000)
            {
                skinned.Clear();
                nextBreath.Clear();
                blasted.Clear();
            }
        }

        /// <summary>A small atomic blast where a G-cell zombie died: flash, shockwave, puff, damage nearby.</summary>
        private static void Blast(World world, Vector3 at, int stage, Entity source)
        {
            float r = BlastRadius[stage];
            var glow = new Color(0.35f, 0.65f, 1f);
            var smoke = KaijuDirector.Instance.EffectMaterial("KaijuSmoke");
            KaijuEffects.Flash(at, r * 8f, 5f + stage, 0.6f, glow);
            KaijuEffects.Burst(at, KaijuDirector.Instance.EffectMaterial("KaijuSpark"), smoke, new Color(0.85f, 0.95f, 1f), glow);
            if (smoke != null)
                Shockwave.Create(at, r * 3f, smoke);
            KaijuAudio.MissileHit(at);
            var hit = new DamageSource(EnumDamageSource.External, EnumDamageTypes.Radiation);
            foreach (Entity e in world.Entities.list)
            {
                var a = e as EntityAlive;
                if (a == null || a == source || a.IsDead() || (a.position - at).sqrMagnitude > r * r)
                    continue;
                a.DamageEntity(hit, BlastDamage[stage], false, 1f);
            }
        }

        public static void Clear()
        {
            nextBreath.Clear();
            blasted.Clear();
        }
    }

    /// <summary>A zombie's atomic breath: a fast blue bolt at where you were; it hurts if you're still there when it lands.</summary>
    public class ZombieBolt : WorldAnchored
    {
        public static float Speed = 35f;
        private static readonly Color Core = new Color(0.8f, 0.95f, 1f), Glow = new Color(0.3f, 0.6f, 1f);
        private Vector3 to, dir;
        private float left;
        private EntityPlayer target;
        private int damage;

        public static void Fire(Vector3 fromWorld, EntityPlayer target, int damage, int stage)
        {
            var go = new GameObject("KaijuZombieBolt");
            Object.DontDestroyOnLoad(go);
            var b = go.AddComponent<ZombieBolt>();
            b.worldPos = fromWorld;
            b.target = target;
            b.damage = damage;
            b.to = GameApi.Position(target) + Vector3.up * 1.3f;
            b.dir = (b.to - fromWorld).normalized;
            b.left = Vector3.Distance(fromWorld, b.to);
            b.LateUpdate();
            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = Glow;
            light.range = 6f + stage * 2f;
            light.intensity = 2.5f + stage * 0.5f;
            light.shadows = LightShadows.None;
            var spark = KaijuDirector.Instance.EffectMaterial("KaijuSpark");
            if (spark != null)
            {
                var ps = KaijuEffects.Particles(go.transform, "Trail", spark, 400, false);
                var main = ps.main;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.15f, 0.4f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(0.1f, 0.8f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.25f + 0.08f * stage, 0.5f + 0.12f * stage);
                main.startColor = new ParticleSystem.MinMaxGradient(Core, Glow);
                var emission = ps.emission;
                emission.rateOverDistance = 10f;
                ps.Play();
            }
            KaijuAudio.ZombieBreath(fromWorld);
        }

        private void Update()
        {
            float step = Speed * Time.deltaTime;
            worldPos += dir * Mathf.Min(step, left);
            left -= step;
            if (left > 0f)
                return;
            // Dodged if you moved away from where it was aimed.
            if (target != null && GameApi.IsAlive(target) && (GameApi.Position(target) + Vector3.up * 1.3f - to).sqrMagnitude < 2.2f * 2.2f)
                target.DamageEntity(new DamageSource(EnumDamageSource.External, EnumDamageTypes.Radiation), damage, false, 1f);
            KaijuEffects.Flash(worldPos, 8f, 3f, 0.3f, Glow);
            KaijuEffects.Burst(worldPos, KaijuDirector.Instance.EffectMaterial("KaijuSpark"), null, Core, Glow);
            enabled = false;
            foreach (var ps in GetComponentsInChildren<ParticleSystem>())
            {
                var em = ps.emission;
                em.enabled = false;
            }
            Destroy(GetComponent<Light>());
            Destroy(gameObject, 1f);
        }
    }
}

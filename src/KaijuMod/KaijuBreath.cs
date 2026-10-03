using System.Collections.Generic;
using UnityEngine;

namespace KaijuMod
{
    /// <summary>
    /// Atomic breath: charge (dorsal plates glow, head comes up), fire (beam from the mouth to the
    /// first thing it hits, destroying blocks along it and killing players in it), fade. He stands
    /// still and turns to face the target while it runs. Visual pieces are plain Unity objects
    /// (LineRenderers, particles, lights) using materials from the model's asset bundle.
    ///
    /// Ticked from the director: Tick in Update for timing and damage, LateTick after the walk
    /// animation has posed the skeleton, to aim the head and place the beam on the mouth.
    /// </summary>
    public class KaijuBreath
    {
        public static float ChargeTime = 3f;
        public static float FireTime = 4f;
        public static float FadeTime = 1.2f;
        /// <summary>Longest beam, in metres from the mouth.</summary>
        public static float Range = 600f;
        /// <summary>Beam damage radius as a fraction of his height (0.08 at 100 m = 8 m).</summary>
        public static float RadiusFraction = 0.08f;
        /// <summary>How fast the beam's front travels out from the mouth, m/s.</summary>
        public static float BeamSpeed = 400f;
        /// <summary>Blocks set to air per frame by the beam (on top of the footprint's budget).</summary>
        public static int ClearBudget = 300;
        /// <summary>Candidate cells examined per frame while tracing the beam or a blast crater.</summary>
        public static int ReadBudget = 8000;
        /// <summary>Blast crater radius as a multiple of the beam's damage radius (Minus One style detonation).</summary>
        public static float BlastScale = 3f; // crater ~61 m radius in a city attack at 100 m tall
        /// <summary>Minus One breath timing: plates light tail to neck, then one shot (matches the model's breath pose).</summary>
        public static float MegaChargeTime = 7f, MegaFireTime = 2.5f, MegaFadeTime = 1.5f;
        /// <summary>Called with the blast point when a mega breath detonates.</summary>
        public System.Action<Vector3> Blasted;

        private static readonly Color CoreColor = new Color(0.85f, 0.95f, 1f, 1f);
        private static readonly Color GlowColor = new Color(0.25f, 0.55f, 1f, 0.7f);
        private static readonly Color LightColor = new Color(0.45f, 0.7f, 1f);

        private enum Phase { Idle, Charge, Fire, Fade }

        private readonly KaijuVisual visual;
        private Phase phase = Phase.Idle;
        private float t;
        private Vector3 target;        // aim point, world coordinates
        private Vector3 mouthWorld;    // last known mouth position, world coordinates
        private Vector3 beamEnd;       // current beam end, world coordinates
        private float height = 100f;
        private float aimWeight;
        // This breath: timing, and whether it is the Minus One city-destroying kind.
        private float chargeTime = 3f, fireTime = 4f, fadeTime = 1.2f;
        private bool mega;
        private float killRadius;

        // Damage along the beam: cells within radius of the traced segment, nearest the mouth first.
        private Vector3 traceFrom, traceDir;
        private float traceLength, traceFront, traced;
        private readonly HashSet<long> seen = new HashSet<long>();
        private readonly Queue<Vector3i> pending = new Queue<Vector3i>();
        private readonly List<Vector3i> batch = new List<Vector3i>();
        private readonly HashSet<EntityPlayer> killed = new HashSet<EntityPlayer>();
        private bool hitSomething;
        // Blast craters being scanned, one horizontal slice at a time under ReadBudget.
        private struct CraterJob
        {
            public Vector3 Center;
            public float Radius;
            public int Dy;
        }
        private readonly List<CraterJob> craters = new List<CraterJob>();

        // Effects
        private GameObject fx;
        private LineRenderer core, glow;
        private ParticleSystem sparks, impactSparks, smoke;
        private Light mouthLight, impactLight;
        private GameObject leftovers; // smoke that keeps drifting after the beam ends
        private float leftoversUntil;

        public KaijuBreath(KaijuVisual visual)
        {
            this.visual = visual;
        }

        public bool Active { get { return phase != Phase.Idle; } }
        public long TotalCleared { get; private set; }

        /// <summary>Where he should face while breathing (world x, z), or null when idle.</summary>
        public Vector2? FacingTarget
        {
            get { return Active ? new Vector2(target.x, target.z) : (Vector2?)null; }
        }

        /// <summary>
        /// Starts a breath at a world-space target point. mega: the Minus One kind, a long charge
        /// in the breath pose and one nuclear blast at the target that kills anyone within
        /// killRadius, leaving blocks standing.
        /// </summary>
        public void Begin(Vector3 worldTarget, float kaijuHeight, float radiusScale = 1f, bool mega = false, float killRadius = 0f)
        {
            this.radiusScale = Mathf.Max(0.1f, radiusScale);
            Cancel();
            this.mega = mega;
            this.killRadius = killRadius;
            chargeTime = mega ? MegaChargeTime : ChargeTime;
            fireTime = mega ? MegaFireTime : FireTime;
            fadeTime = mega ? MegaFadeTime : FadeTime;
            if (mega)
                visual.PlayBreathPose();
            target = worldTarget;
            height = Mathf.Max(5f, kaijuHeight);
            phase = Phase.Charge;
            t = 0f;
            aimWeight = 0f;
            seen.Clear();
            killed.Clear();
            hitSomething = false;
            BuildEffects();
            Log.Out("[KaijuMod] Atomic breath charging at " + target);
        }

        /// <summary>Stops at once and also drops queued crater and beam damage (starting over).</summary>
        public void CancelAll()
        {
            Cancel();
            craters.Clear();
            pending.Clear();
        }

        /// <summary>Stops at once and removes the effects.</summary>
        public void Cancel()
        {
            phase = Phase.Idle;
            visual.SetPlateGlow(0f);
            if (fx != null)
                Object.Destroy(fx);
            fx = null;
            DropLeftovers(true);
        }

        public void Tick(World world, float dt)
        {
            if (leftovers != null && Time.time > leftoversUntil)
                DropLeftovers(true);
            // Craters and queued clears keep going after the beam has ended.
            ProcessCraters(world);
            ClearPending(world);
            if (phase == Phase.Idle)
                return;
            t += dt;
            switch (phase)
            {
                case Phase.Charge:
                    // Mega: the breath pose holds his head (thrown back, mouth gaping forward); turning
                    // the head to aim the snout would tip the open mouth down. The beam still goes
                    // from the mouth to the target.
                    aimWeight = mega ? 0f : Mathf.SmoothStep(0f, 1f, t / chargeTime);
                    if (t >= chargeTime)
                        StartFire();
                    break;
                case Phase.Fire:
                    aimWeight = mega ? 0f : 1f;
                    Trace(world);
                    KillPlayersInBeam(world);
                    if (t >= fireTime)
                    {
                        phase = Phase.Fade;
                        t = 0f;
                        if (hitSomething)
                            Detonate(world);
                        Log.Out("[KaijuMod] Atomic breath done, " + TotalCleared + " blocks destroyed by breath so far");
                    }
                    break;
                case Phase.Fade:
                    aimWeight = mega ? 0f : 1f - Mathf.SmoothStep(0f, 1f, t / fadeTime);
                    if (t >= fadeTime)
                        Finish();
                    break;
            }
        }

        /// <summary>After animation: aim the head, then put the beam, lights and particles on the mouth.</summary>
        public void LateTick()
        {
            if (phase == Phase.Idle)
                return;
            visual.AimHead(GameApi.WorldToScene(target), aimWeight);
            mouthWorld = visual.MouthWorld();
            UpdateEffects();
        }

        private void StartFire()
        {
            phase = Phase.Fire;
            t = 0f;
            traceFrom = mouthWorld;
            Vector3 toTarget = target - traceFrom;
            traceDir = toTarget.sqrMagnitude > 0.01f ? toTarget.normalized : Vector3.forward;
            // The beam stops at the first thing it hits (terrain, buildings, you) or at its range.
            // Physics colliders only exist near the player, so a far target can be missed: fall back
            // to marching through block data, which is loaded much further out.
            // A mega breath goes all the way to its target (the city centre), through anything.
            Vector3? hit = mega ? target : GameApi.Raycast(traceFrom, traceDir, Range);
            if (!hit.HasValue && GameApi.World != null)
                hit = GameApi.BlockRaycast(GameApi.World, traceFrom, traceDir, Range);
            beamEnd = hit ?? traceFrom + traceDir * Range;
            hitSomething = hit.HasValue;
            traceLength = Vector3.Distance(traceFrom, beamEnd);
            traceFront = 0f;
            traced = 0f;
            if (sparks != null) sparks.Play();
            if (impactSparks != null) impactSparks.Play();
            if (smoke != null) smoke.Play();
            Log.Out("[KaijuMod] Atomic breath firing " + Mathf.Round(traceLength) + " m" + (hit.HasValue ? "" : " (no hit; full range)"));
        }

        private void Finish()
        {
            phase = Phase.Idle;
            visual.SetPlateGlow(0f);
            if (mega)
                visual.EndBreathPose();
            visual.AimHead(Vector3.zero, 0f);
            if (fx == null)
                return;
            // Let the smoke drift for a while instead of vanishing with the beam.
            if (smoke != null)
            {
                var em = smoke.emission;
                em.enabled = false;
                smoke.transform.SetParent(null, true);
                DropLeftovers(true);
                leftovers = smoke.gameObject;
                leftoversUntil = Time.time + 8f;
            }
            Object.Destroy(fx);
            fx = null;
        }

        private void DropLeftovers(bool destroy)
        {
            if (leftovers != null && destroy)
                Object.Destroy(leftovers);
            leftovers = null;
        }

        // ---- Damage ----

        // Damage radius multiplier for this breath (city attacks use a bigger one).
        private float radiusScale = 1f;

        private float DamageRadius { get { return Mathf.Max(2f, height * RadiusFraction * radiusScale); } }

        /// <summary>
        /// Advances the beam front and queues destructible blocks within the damage radius of the
        /// segment up to it, in 1 m slices from the mouth. The end gets a crater twice as wide.
        /// </summary>
        private void Trace(World world)
        {
            traceFront = Mathf.Min(traceLength, traceFront + BeamSpeed * Time.deltaTime);
            if (mega)
                return; // no blocks destroyed: the blast does the work
            float r = DamageRadius;
            int reads = 0;
            while (traced <= traceFront && reads < ReadBudget)
            {
                bool atEnd = traced + 1f > traceLength;
                float sliceR = atEnd ? r * 2f : r;
                Vector3 c = traceFrom + traceDir * traced;
                reads += QueueSphere(world, c, sliceR, atEnd ? c : (Vector3?)null);
                traced += 1f;
            }
        }

        /// <summary>
        /// Queues destructible cells near c: within radius of the beam segment, or of the crater
        /// centre when one is given. Returns the number of cells examined.
        /// </summary>
        private int QueueSphere(World world, Vector3 c, float radius, Vector3? crater)
        {
            int ri = Mathf.CeilToInt(radius);
            int reads = 0;
            for (int dy = ri; dy >= -ri; dy--) // top down, like the footprint
                reads += QueueSlice(world, c, radius, crater, dy);
            return reads;
        }

        /// <summary>One horizontal slice (c.y + dy) of QueueSphere.</summary>
        private int QueueSlice(World world, Vector3 c, float radius, Vector3? crater, int dy)
        {
            int ri = Mathf.CeilToInt(radius);
            int cx = Mathf.FloorToInt(c.x), cy = Mathf.FloorToInt(c.y), cz = Mathf.FloorToInt(c.z);
            float r2 = radius * radius;
            int reads = 0;
            int y = cy + dy;
            if (y < 1 || y > 253)
                return 1;
            for (int dx = -ri; dx <= ri; dx++)
            {
                for (int dz = -ri; dz <= ri; dz++)
                {
                    int x = cx + dx, z = cz + dz;
                    var p = new Vector3(x + 0.5f, y + 0.5f, z + 0.5f);
                    float d2 = crater.HasValue ? (p - crater.Value).sqrMagnitude : DistanceToBeamSq(p);
                    if (d2 > r2)
                        continue;
                    long key = Key(x, y, z);
                    if (!seen.Add(key))
                        continue;
                    reads++;
                    if (!GameApi.IsChunkLoaded(world, x, z))
                        continue;
                    if (GameApi.IsDestructible(world, x, y, z))
                        pending.Enqueue(new Vector3i(x, y, z));
                }
            }
            return Mathf.Max(1, reads);
        }

        /// <summary>
        /// The beam's end detonates like an atomic bomb: flash, shockwave, mushroom cloud, a crater
        /// BlastScale times the beam's radius (blocks only; terrain stays), and anyone inside it dies.
        /// </summary>
        private void Detonate(World world)
        {
            if (mega)
            {
                MegaDetonate(world);
                return;
            }
            float r = DamageRadius * BlastScale;
            KaijuEffects.Explosion(beamEnd, height, visual.EffectMaterial("KaijuSmoke"), visual.EffectMaterial("KaijuSpark"));
            KaijuAudio.Blast(beamEnd, r);
            craters.Add(new CraterJob { Center = beamEnd, Radius = r, Dy = Mathf.CeilToInt(r) });
            foreach (EntityPlayer player in GameApi.Players(world))
            {
                if (!GameApi.IsAlive(player))
                    continue;
                if ((GameApi.Position(player) - beamEnd).sqrMagnitude <= r * r)
                {
                    Log.Out("[KaijuMod] Atomic blast hit player at " + GameApi.Position(player));
                    GameApi.Kill(player);
                }
            }
            Log.Out("[KaijuMod] Atomic blast at " + beamEnd + ", crater radius " + Mathf.Round(r) + " m");
        }

        /// <summary>
        /// The Minus One blast: a fireball, a mushroom cloud far wider than his normal ones, a
        /// shockwave racing out past the city's edge, and death for anyone within killRadius. No
        /// blocks are destroyed; the city is left standing but irradiated (Blasted tells The Run).
        /// </summary>
        private void MegaDetonate(World world)
        {
            float r = Mathf.Max(killRadius, height);
            KaijuEffects.MegaExplosion(beamEnd, height, r, visual.EffectMaterial("KaijuSmoke"), visual.EffectMaterial("KaijuSpark"));
            KaijuAudio.Blast(beamEnd, r * 0.4f);
            foreach (EntityPlayer player in GameApi.Players(world))
            {
                if (!GameApi.IsAlive(player))
                    continue;
                Vector3 p = GameApi.Position(player);
                if (new Vector2(p.x - beamEnd.x, p.z - beamEnd.z).sqrMagnitude <= r * r)
                {
                    Log.Out("[KaijuMod] Mega blast hit player at " + p);
                    GameApi.Kill(player);
                }
            }
            Log.Out("[KaijuMod] Mega blast at " + beamEnd + ", kill radius " + Mathf.Round(r) + " m");
            if (Blasted != null)
                Blasted(beamEnd);
        }

        /// <summary>Scans queued craters one horizontal slice at a time, top down, within the read budget.</summary>
        private void ProcessCraters(World world)
        {
            int reads = 0;
            while (craters.Count > 0 && reads < ReadBudget)
            {
                CraterJob job = craters[0];
                int ri = Mathf.CeilToInt(job.Radius);
                if (job.Dy < -ri)
                {
                    craters.RemoveAt(0);
                    continue;
                }
                reads += QueueSlice(world, job.Center, job.Radius, job.Center, job.Dy);
                job.Dy--;
                craters[0] = job;
            }
        }

        private float DistanceToBeamSq(Vector3 p)
        {
            float along = Mathf.Clamp(Vector3.Dot(p - traceFrom, traceDir), 0f, traceLength);
            return (traceFrom + traceDir * along - p).sqrMagnitude;
        }

        private void ClearPending(World world)
        {
            batch.Clear();
            while (pending.Count > 0 && batch.Count < ClearBudget)
                batch.Add(pending.Dequeue());
            if (batch.Count == 0)
                return;
            GameApi.ClearBlocks(world, batch);
            TotalCleared += batch.Count;
        }

        private void KillPlayersInBeam(World world)
        {
            float r = DamageRadius + 1f;
            foreach (EntityPlayer player in GameApi.Players(world))
            {
                if (!GameApi.IsAlive(player))
                {
                    killed.Remove(player);
                    continue;
                }
                Vector3 p = GameApi.Position(player) + new Vector3(0f, 0.9f, 0f);
                float along = Vector3.Dot(p - traceFrom, traceDir);
                if (along < 0f || along > traceFront + r)
                    continue;
                if (DistanceToBeamSq(p) > r * r)
                    continue;
                if (killed.Add(player))
                    Log.Out("[KaijuMod] Atomic breath hit player at " + p);
                GameApi.Kill(player);
            }
        }

        private static long Key(int x, int y, int z)
        {
            return ((long)(x & 0x3ffffff) << 34) | ((long)(z & 0x3ffffff) << 8) | (long)(y & 0xff);
        }

        // ---- Effects ----

        private void BuildEffects()
        {
            fx = new GameObject("KaijuBreath");
            Object.DontDestroyOnLoad(fx);
            Material beam = visual.EffectMaterial("KaijuBeam");
            Material spark = visual.EffectMaterial("KaijuSpark");
            Material smokeMat = visual.EffectMaterial("KaijuSmoke");
            float h = height;

            if (beam != null)
            {
                glow = MakeLine("Glow", beam, GlowColor);
                core = MakeLine("Core", beam, CoreColor);
            }
            mouthLight = MakeLight("MouthLight", h * 0.35f);
            impactLight = MakeLight("ImpactLight", h * 0.6f);

            if (spark != null)
            {
                sparks = MakeParticles("Sparks", spark, 3000);
                var main = sparks.main;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.3f, 0.7f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(h * 0.01f, h * 0.05f);
                main.startSize = new ParticleSystem.MinMaxCurve(h * 0.004f, h * 0.012f);
                main.startColor = new ParticleSystem.MinMaxGradient(CoreColor, GlowColor);
                var shape = sparks.shape;
                shape.enabled = true;
                shape.shapeType = ParticleSystemShapeType.Box;

                impactSparks = MakeParticles("ImpactSparks", spark, 2000);
                main = impactSparks.main;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 1.2f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(h * 0.05f, h * 0.18f);
                main.startSize = new ParticleSystem.MinMaxCurve(h * 0.006f, h * 0.02f);
                main.startColor = new ParticleSystem.MinMaxGradient(CoreColor, GlowColor);
                main.gravityModifier = 0.6f;
                var emission = impactSparks.emission;
                emission.rateOverTime = 250f;
                shape = impactSparks.shape;
                shape.enabled = true;
                shape.shapeType = ParticleSystemShapeType.Sphere;
                shape.radius = h * 0.03f;
            }
            if (smokeMat != null)
            {
                smoke = MakeParticles("Smoke", smokeMat, 400);
                var main = smoke.main;
                main.startLifetime = new ParticleSystem.MinMaxCurve(4f, 7f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(h * 0.01f, h * 0.03f);
                main.startSize = new ParticleSystem.MinMaxCurve(h * 0.08f, h * 0.18f);
                main.startColor = new Color(0.32f, 0.33f, 0.36f, 0.65f);
                main.gravityModifier = -0.02f;
                var emission = smoke.emission;
                emission.rateOverTime = 18f;
                var shape = smoke.shape;
                shape.enabled = true;
                shape.shapeType = ParticleSystemShapeType.Hemisphere;
                shape.radius = h * 0.04f;
                var col = smoke.colorOverLifetime;
                col.enabled = true;
                var g = new Gradient();
                g.SetKeys(
                    new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                    new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0f, 1f) });
                col.color = g;
                var size = smoke.sizeOverLifetime;
                size.enabled = true;
                size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 1.8f));
            }
        }

        private void UpdateEffects()
        {
            if (fx == null)
                return;
            float h = height;
            float flicker = 1f + 0.18f * (Mathf.PerlinNoise(Time.time * 18f, 0.3f) - 0.5f) * 2f;
            Vector3 mouth = GameApi.WorldToScene(mouthWorld);
            if (mega && phase == Phase.Charge)
            {
                // Minus One: the plates light one band at a time from the tail tip to the neck,
                // then all flare together just before he fires.
                float lit = chargeTime * 0.85f;
                visual.SetPlateCharge(Mathf.Clamp01(t / lit), (t > lit ? 5f : 3.2f) * flicker);
            }
            else
                visual.SetPlateGlow(PlateLevel(flicker));

            float mouthIntensity = phase == Phase.Charge ? Mathf.Lerp(0f, 3f, t / chargeTime)
                : phase == Phase.Fire ? 6f * flicker
                : Mathf.Lerp(6f, 0f, t / fadeTime);
            if (mouthLight != null)
            {
                mouthLight.transform.position = mouth;
                mouthLight.intensity = mouthIntensity;
            }

            bool beamOn = phase == Phase.Fire || phase == Phase.Fade;
            float widthScale = phase == Phase.Fire ? Mathf.Clamp01(t / 0.25f) : phase == Phase.Fade ? 1f - Mathf.Clamp01(t / (fadeTime * 0.6f)) : 0f;
            // The beam's start follows the mouth; its end grows out at BeamSpeed to the traced hit point.
            Vector3 end = !beamOn ? mouth
                : traceFront < traceLength ? mouth + traceDir * traceFront
                : GameApi.WorldToScene(beamEnd);
            SetLine(core, mouth, end, h * 0.03f * widthScale * flicker, beamOn);
            SetLine(glow, mouth, end, h * 0.09f * widthScale * (2f - flicker), beamOn);

            bool hitting = phase == Phase.Fire && traceFront >= traceLength - 0.5f;
            Vector3 impact = GameApi.WorldToScene(beamEnd);
            if (impactLight != null)
            {
                impactLight.transform.position = impact - traceDir * (h * 0.05f);
                impactLight.intensity = hitting ? 8f * flicker : Mathf.MoveTowards(impactLight.intensity, 0f, Time.deltaTime * 10f);
            }
            if (sparks != null)
            {
                float len = Vector3.Distance(mouth, end);
                sparks.transform.position = (mouth + end) * 0.5f;
                if (len > 0.1f)
                    sparks.transform.rotation = Quaternion.LookRotation(end - mouth);
                var shape = sparks.shape;
                shape.scale = new Vector3(h * 0.02f, h * 0.02f, Mathf.Max(0.1f, len));
                var emission = sparks.emission;
                emission.rateOverTime = phase == Phase.Fire ? Mathf.Min(1500f, len * 2.5f) : 0f;
            }
            if (impactSparks != null)
            {
                impactSparks.transform.position = impact;
                var emission = impactSparks.emission;
                emission.enabled = hitting;
            }
            if (smoke != null)
            {
                smoke.transform.position = impact;
                var emission = smoke.emission;
                emission.enabled = phase == Phase.Fire && traceFront >= traceLength - 0.5f || phase == Phase.Fade;
            }
        }

        private float PlateLevel(float flicker)
        {
            switch (phase)
            {
                case Phase.Charge:
                    // Brightens in pulses that come faster as the charge builds.
                    float k = t / chargeTime;
                    float pulse = 0.65f + 0.35f * Mathf.Sin(t * Mathf.Lerp(6f, 22f, k));
                    return Mathf.Lerp(0f, 4f, k * k) * pulse;
                case Phase.Fire:
                    return 5f * flicker;
                case Phase.Fade:
                    return Mathf.Lerp(5f, 0f, t / fadeTime);
            }
            return 0f;
        }

        private static void SetLine(LineRenderer lr, Vector3 a, Vector3 b, float width, bool on)
        {
            if (lr == null)
                return;
            lr.enabled = on && width > 0.01f;
            lr.SetPosition(0, a);
            lr.SetPosition(1, b);
            lr.startWidth = width;
            lr.endWidth = width * 1.15f;
        }

        private LineRenderer MakeLine(string name, Material mat, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(fx.transform, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.positionCount = 2;
            lr.sharedMaterial = mat;
            lr.startColor = color;
            lr.endColor = color;
            lr.textureMode = LineTextureMode.Stretch;
            lr.alignment = LineAlignment.View;
            lr.numCapVertices = 6;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.enabled = false;
            return lr;
        }

        private Light MakeLight(string name, float range)
        {
            var go = new GameObject(name);
            go.transform.SetParent(fx.transform, false);
            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = LightColor;
            light.range = range;
            light.intensity = 0f;
            light.shadows = LightShadows.None;
            return light;
        }

        private ParticleSystem MakeParticles(string name, Material mat, int max)
        {
            var go = new GameObject(name);
            go.transform.SetParent(fx.transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = max;
            main.scalingMode = ParticleSystemScalingMode.Shape;
            var emission = ps.emission;
            emission.rateOverTime = 0f;
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = mat;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return ps;
        }
    }
}

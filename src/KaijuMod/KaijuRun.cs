using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Xml;
using UnityEngine;

namespace KaijuMod
{
    /// <summary>
    /// The Run (docs/design.md): on a Kaiju world (one with kaiju.xml), Godzilla destroys the start
    /// city shortly after the first spawn, then the end city of the current strip each time a
    /// blood moon horde ends. Between attacks a radiation front sweeps along the current strip from
    /// the ruined city toward the next end city, timed to stop short of it as the blood moon starts.
    /// After an attack the front sweeps over that city to the coast during a few hours of grace,
    /// while you escape north through the pass; then the next strip's front starts.
    ///
    /// Radiation: anyone in an earlier strip, or behind the front in the current one, takes steady
    /// radiation damage and sees a warning. State is saved to kaiju_run.xml in the save folder.
    /// </summary>
    public class KaijuRun
    {
        public static readonly KaijuRun Instance = new KaijuRun();

        /// <summary>Real seconds after the first spawn before he attacks the start city. None: his walk in from the sea is the lead-in.</summary>
        public static float StartAttackDelay = 0f;
        /// <summary>In-game hours after an end-city attack before the next strip's front starts.</summary>
        public static int GraceHours = 4;
        /// <summary>The front stops this far short of the end city's inland edge, metres.</summary>
        public static float FrontStopMargin = 200f;
        /// <summary>Radiation damage per second to players behind the front.</summary>
        public static int RadiationDamage = 4;
        /// <summary>Warn players who are this close ahead of the front, metres.</summary>
        public static float WarnDistance = 150f;
        /// <summary>The Oxygen Destroyer kills him when he comes this close to it, metres.</summary>
        public static float DestroyerRadius = 100f;
        /// <summary>Oxygen Destroyer parts: one crate in a military site by the end city of each strip.</summary>
        public const int Parts = 5;
        public static string PartItem(int i) { return "kaijuOxygenDestroyerPart" + (i + 1); }
        public static string CrateBlock(int i) { return "kaijuPartCrate" + (i + 1); }

        private const string StateFile = "kaiju_run.xml";

        private class State
        {
            public bool StartAttacked;
            public int Strip = -1;          // strip whose front is (or will be) moving; -1 before the start attack
            public ulong FrontStart;        // world time the current strip's front starts moving
            public ulong SweepStart;        // world time of the last end-city attack (previous strip's sweep)
            public int BloodMoonActive;     // blood moon day seen in progress, 0 if none
            public int LastAttackedBloodMoon;
            public bool RadiationOn = true;
            public bool Complete;
            // Oxygen Destroyer
            public readonly bool[] CratePlaced = new bool[Parts];
            public readonly Vector3i[] CratePos = new Vector3i[Parts];
            // Part taken from its crate (held once, or the crate opened and emptied of it, or the
            // crate gone): its marker, light and trader quest never come back.
            public readonly bool[] PartTaken = new bool[Parts];
            public bool DeviceArmed;
            public Vector3i DevicePos;
            public bool FinaleActive;
            public string Outcome = "";     // "", "won" or "lost"
            // Cities he has attacked: fallout hangs over them for the rest of the run.
            public readonly List<string> Ruined = new List<string>();
            // Cities wiped out by a Minus One blast: the whole city is irradiated for good.
            public readonly List<string> Blasted = new List<string>();
        }

        /// <summary>Fallout haze colour and fog density while you are in the radiation.</summary>
        public static Color FalloutFog = new Color(0.47f, 0.52f, 0.4f);
        public static float FalloutFogDensity = 0.55f;
        private readonly Dictionary<string, FalloutCloud> fallout = new Dictionary<string, FalloutCloud>();
        private Ashfall ash;
        private bool fogOn;
        private FrontCloud frontCloud;
        private KaijuHorizonWall horizonWall;
        private bool prewarmFront;   // the first cloud after a load starts fully formed
        /// <summary>Size of the dark cloud bank over the radiation front, metres.</summary>
        public static float FrontCloudWidth = 900f, FrontCloudDepth = 700f;
        private float nextGeiger;

        // Not saved: markers and lights are rebuilt as the player moves.
        private readonly NavObject[] markers = new NavObject[Parts];
        private readonly Beacon[] crateLights = new Beacon[Parts];
        private Beacon deviceLight;
        private float partsTimer;

        private World boundWorld;
        private KaijuWorldData data;
        private State st = new State();
        private float startTimer, radTimer;
        private float nextWarn;

        public bool Active { get { return data != null; } }
        public KaijuWorldData Data { get { return data; } }

        // ---- ticking

        public void Tick(float dt)
        {
            World world = GameApi.World;
            if (world == null)
            {
                boundWorld = null;
                data = null;
                return;
            }
            if (world != boundWorld)
                Bind(world);
            if (data == null || dt <= 0f)
                return;

            ulong now = GameApi.WorldTime(world);
            EntityPlayer player = GameApi.LocalPlayer(world);

            if (!st.StartAttacked && player != null && GameApi.IsAlive(player))
            {
                startTimer += dt;
                if (startTimer >= StartAttackDelay)
                    AttackStartCity(now);
            }

            if (GameApi.IsBloodMoonNow(world))
            {
                int bm = GameApi.BloodMoonDay();
                if (st.BloodMoonActive != bm)
                {
                    st.BloodMoonActive = bm;
                    Save();
                }
            }
            else if (st.BloodMoonActive > 0 && st.LastAttackedBloodMoon != st.BloodMoonActive)
            {
                OnHordeEnded(now);
            }

            radTimer += dt;
            if (radTimer >= 1f)
            {
                radTimer = 0f;
                ApplyRadiation(world, now);
            }

            TickFinale();
            TickFallout(world, player, now);
            partsTimer += dt;
            if (partsTimer >= 1f)
            {
                partsTimer = 0f;
                TickParts(world, player);
            }
        }

        private void Bind(World world)
        {
            ClearMarkers();
            ClearFallout(world);
            boundWorld = world;
            prewarmFront = true;
            startTimer = radTimer = partsTimer = 0f;
            st = new State();
            string folder = GameApi.WorldFolder();
            try
            {
                data = KaijuWorldData.Load(folder);
            }
            catch (System.Exception e)
            {
                data = null;
                Log.Warning("[KaijuMod] Could not read " + KaijuWorldData.FileName + " in " + folder + ": " + e.Message);
            }
            if (data == null)
            {
                Log.Out("[KaijuMod] No " + KaijuWorldData.FileName + " in this world (" + folder + "); The Run is off");
                return;
            }
            if (!GameApi.IsSinglePlayer())
            {
                Log.Warning("[KaijuMod] The Run is single player only; disabled in hosted and dedicated games");
                data = null;
                return;
            }
            Load();
            foreach (string name in st.Ruined)
                AddFallout(data.Find(name));
            Log.Out("[KaijuMod] The Run: " + data.World + ", " + data.Settlements.Count + " settlements, "
                + data.Route.Count + " route points; strip " + st.Strip + (st.StartAttacked ? "" : ", start attack pending"));
        }

        // ---- attacks

        private void AttackStartCity(ulong now)
        {
            st.StartAttacked = true;
            st.Strip = 0;
            st.FrontStart = now + 1000; // the front leaves the ruins an hour after he comes ashore
            Save();
            Attack(data.StartCity);
        }

        private void OnHordeEnded(ulong now)
        {
            st.LastAttackedBloodMoon = st.BloodMoonActive;
            st.BloodMoonActive = 0;
            if (st.Strip < 0 || st.Complete)
            {
                Save();
                return;
            }
            Settlement city = data.EndCity(st.Strip);
            if (st.Strip == data.Strips - 1)
                st.FinaleActive = true; // the last city: the Oxygen Destroyer's chance
            st.SweepStart = now;
            st.FrontStart = now + (ulong)(GraceHours * 1000);
            st.Strip++;
            if (st.Strip >= data.Strips)
                st.Complete = true;
            Save();
            Attack(city);
        }

        /// <summary>Sends him at a settlement now (also used by `kaiju attack`).</summary>
        public bool Attack(Settlement city)
        {
            if (city == null)
                return false;
            var plan = CityAttack.Plan(data, city);
            string error;
            if (!KaijuDirector.Instance.StartAttack(plan, data.SeaLevel, out error))
            {
                Log.Warning("[KaijuMod] Attack on " + city.Name + " failed: " + error);
                return false;
            }
            Log.Out("[KaijuMod] Godzilla is attacking " + city.Name);
            if (!st.Ruined.Contains(city.Name))
            {
                st.Ruined.Add(city.Name);
                Save();
            }
            AddFallout(city);
            var player = GameApi.LocalPlayer(boundWorld);
            if (player != null)
                GameApi.Tooltip(player, "Godzilla is rising off " + city.Name + "!");
            return true;
        }

        public bool Attack(string which)
        {
            if (data == null)
                return false;
            Settlement city;
            if (which == "finale")
            {
                // Test the ending now: the last city's attack, with the Oxygen Destroyer check on.
                st.FinaleActive = true;
                st.Outcome = "";
                Save();
                city = data.EndCity(data.Strips - 1);
            }
            else if (which == "start")
                city = data.StartCity;
            else if (which == "end")
                city = data.EndCity(Mathf.Clamp(st.Strip, 0, data.Strips - 1));
            else
                city = data.Find(which);
            return Attack(city);
        }

        // ---- radiation

        /// <summary>Front x in a strip at a time: from the ruined city toward a stop short of the end city.</summary>
        private float FrontX(int strip, ulong now)
        {
            Settlement from = strip == 0 ? data.StartCity : data.EndCity(strip - 1);
            Settlement to = data.EndCity(strip);
            if (from == null || to == null)
                return 0f;
            int d = data.Direction(strip);
            float stop = to.X - d * (to.HalfWidth + FrontStopMargin);
            var duskDawn = GameApi.DuskDawn();
            ulong end = GameApi.DayTimeToWorldTime(GameApi.BloodMoonDay(), duskDawn.Item1, 0);
            return Mathf.Lerp(from.X, stop, Progress(now, st.FrontStart, end));
        }

        /// <summary>While the grace period runs, the previous strip's front sweeps over its ruined city to the coast.</summary>
        private float SweepX(int strip, ulong now)
        {
            Settlement to = data.EndCity(strip);
            int d = data.Direction(strip);
            float stop = to.X - d * (to.HalfWidth + FrontStopMargin);
            float coast = d * data.Size / 2f;
            return Mathf.Lerp(stop, coast, Progress(now, st.SweepStart, st.FrontStart));
        }

        /// <summary>0 before start, 1 after end, linear between (world times).</summary>
        private static float Progress(ulong now, ulong start, ulong end)
        {
            if (now <= start)
                return 0f;
            if (end <= start || now >= end)
                return 1f;
            return (float)(now - start) / (end - start);
        }

        /// <summary>
        /// The moving edge of the radiation now: the previous strip's sweep during a grace period,
        /// otherwise the current strip's front. False when there is none (before the run, after the end).
        /// </summary>
        private bool CurrentFront(ulong now, out float x, out int strip)
        {
            x = 0f;
            strip = -1;
            if (data == null || !st.RadiationOn || st.Strip < 0)
                return false;
            if (now < st.FrontStart && st.Strip >= 1)
            {
                strip = st.Strip - 1;
                x = SweepX(strip, now);
                return true;
            }
            if (st.Complete || st.Strip >= data.Strips)
                return false;
            strip = st.Strip;
            x = FrontX(strip, now);
            return true;
        }

        /// <summary>True if a world position is irradiated now; frontX is the front it is measured against.</summary>
        public bool IsIrradiated(Vector3 p, ulong now, out float frontX, out int strip)
        {
            frontX = 0f;
            strip = -1;
            if (data == null || !st.RadiationOn || st.Strip < 0)
                return false;
            if (BlastedCityAt(p) != null)
                return true;
            int ps = data.StripOf(p.z);
            if (now < st.FrontStart)
            {
                int sweeping = st.Strip - 1;
                if (sweeping < 0)
                    return false;
                if (ps < sweeping)
                    return true;
                if (ps != sweeping)
                    return false;
                strip = sweeping;
                frontX = SweepX(sweeping, now);
                return (p.x - frontX) * data.Direction(sweeping) < 0f;
            }
            if (st.Complete)
                return false; // run over: no new front after the last city
            if (ps < st.Strip)
                return true;
            if (ps != st.Strip)
                return false;
            strip = st.Strip;
            frontX = FrontX(st.Strip, now);
            return (p.x - frontX) * data.Direction(st.Strip) < 0f;
        }

        private void ApplyRadiation(World world, ulong now)
        {
            foreach (EntityPlayer player in GameApi.Players(world))
            {
                if (!GameApi.IsAlive(player))
                    continue;
                Vector3 p = GameApi.Position(player);
                float fx;
                int strip;
                bool hot = IsIrradiated(p, now, out fx, out strip);
                if (hot)
                {
                    GameApi.RadiationDamage(player, RadiationDamage);
                    if (Time.time >= nextWarn)
                    {
                        nextWarn = Time.time + 6f;
                        var city = BlastedCityAt(p);
                        string way = strip >= 0 ? (data.Direction(strip) > 0 ? "east" : "west") : "north";
                        GameApi.Tooltip(player, city != null ? "RADIATION! Get out of " + city.Name + "!"
                            : "RADIATION! Get " + way + ", ahead of the front.");
                    }
                }
                else if (strip >= 0 && Mathf.Abs(p.x - fx) < WarnDistance && Time.time >= nextWarn)
                {
                    nextWarn = Time.time + 20f;
                    GameApi.Tooltip(player, "The radiation front is " + Mathf.RoundToInt(Mathf.Abs(p.x - fx)) + " m behind you.");
                }
            }
        }

        public void SetRadiation(bool on)
        {
            st.RadiationOn = on;
            Save();
        }

        /// <summary>
        /// Starts The Run over: he is removed, every cloud, shockwave and haze goes, the radiation
        /// and fog clear, and the start city attack comes again at once.
        /// </summary>
        public void Reset()
        {
            KaijuDirector.Instance.StopAll();
            KaijuEffects.ClearAll();
            ClearMarkers();
            ClearFallout(GameApi.World);
            st = new State();
            startTimer = 0f;
            Save();
        }

        // ---- Minus One blasts

        /// <summary>A Minus One breath has wiped out a city: all of it is irradiated from now on.</summary>
        public void OnCityBlasted(string cityName, Vector3 pos)
        {
            if (data == null || cityName == null)
                return;
            if (!st.Blasted.Contains(cityName))
            {
                st.Blasted.Add(cityName);
                Save();
            }
            Log.Out("[KaijuMod] " + cityName + " is irradiated");
            if (horizonWall == null)
                horizonWall = KaijuHorizonWall.Create(KaijuDirector.Instance.EffectMaterial("KaijuSmoke"));
            if (horizonWall != null)
                horizonWall.Flare();
            var player = GameApi.LocalPlayer(boundWorld);
            if (player != null && GameApi.IsAlive(player))
                GameApi.Tooltip(player, cityName + " is gone. The whole city is irradiated.");
        }

        /// <summary>The blasted city a position is in (with a margin), or null.</summary>
        private Settlement BlastedCityAt(Vector3 p)
        {
            foreach (string name in st.Blasted)
            {
                var c = data.Find(name);
                if (c != null && Mathf.Abs(p.x - c.X) <= c.HalfWidth + 40f
                    && Mathf.Abs(p.z - c.Z) <= 2.2f * CityAttack.BlockSpacing + 40f)
                    return c;
            }
            return null;
        }

        // ---- Fallout

        /// <summary>
        /// The smoke wall on the horizon toward the radiation: shown once a city has been hit (the
        /// Minus One blast, or a classic attack once he has gone) while the front is active, at the
        /// player's distance from the front.
        /// </summary>
        private void TickHorizonWall(EntityPlayer player, ulong now)
        {
            float fx;
            int strip;
            bool front = CurrentFront(now, out fx, out strip);
            bool hit = st.Blasted.Count > 0 || (st.Ruined.Count > 0 && !KaijuDirector.Instance.Running);
            bool on = front && hit;
            if (!on && horizonWall == null)
                return;
            if (horizonWall == null)
                horizonWall = KaijuHorizonWall.Create(KaijuDirector.Instance.EffectMaterial("KaijuSmoke"));
            if (horizonWall == null)
                return;
            Vector3 p = GameApi.Position(player);
            int d = front ? data.Direction(strip) : 1;
            // The radiation is behind the front: the side the front comes from.
            horizonWall.Set(on, GameApi.WorldToScene(p), new Vector2(-d, 0f), front ? (p.x - fx) * d : KaijuHorizonWall.MaxDistance,
                GameApi.WorldToScene(new Vector3(p.x, p.y - 40f, p.z)).y);
        }

        /// <summary>Keeps the dark cloud bank hanging over the irradiated side of the front.</summary>
        private void TickFrontCloud(ulong now)
        {
            float fx;
            int strip;
            bool on = CurrentFront(now, out fx, out strip);
            if (on && frontCloud == null)
            {
                float h = KaijuDirector.Instance.Footprint.Height * KaijuEffects.ExplosionScale;
                frontCloud = FrontCloud.Create(KaijuDirector.Instance.EffectMaterial("KaijuSmoke"), h,
                    FrontCloudWidth, FrontCloudDepth, prewarmFront);
            }
            prewarmFront = false;
            if (frontCloud == null)
                return;
            frontCloud.SetEmitting(on);
            if (!on)
                return;
            float stripZ = -data.Size / 2f + (strip + 0.5f) * data.StripHeight;
            float capY = data.SeaLevel + KaijuDirector.Instance.Footprint.Height * KaijuEffects.ExplosionScale * 4.6f;
            frontCloud.MoveTo(new Vector3(fx - data.Direction(strip) * FrontCloudDepth * 0.5f, capY, stripZ));
        }

        private void AddFallout(Settlement city)
        {
            if (city == null || fallout.ContainsKey(city.Name))
                return;
            var f = FalloutCloud.Create(new Vector3(city.X, city.Y, city.Z), KaijuDirector.Instance.Footprint.Height,
                city.HalfWidth, KaijuDirector.Instance.EffectMaterial("KaijuSmoke"));
            if (f != null)
                fallout[city.Name] = f;
        }

        private void ClearFallout(World world)
        {
            foreach (var f in fallout.Values)
                if (f != null)
                    Object.Destroy(f.gameObject);
            fallout.Clear();
            if (ash != null)
                Object.Destroy(ash.gameObject);
            ash = null;
            if (frontCloud != null)
                Object.Destroy(frontCloud.gameObject);
            frontCloud = null;
            if (horizonWall != null)
                Object.Destroy(horizonWall.gameObject);
            horizonWall = null;
            if (fogOn)
                GameApi.ClearFog(world);
            fogOn = false;
        }

        /// <summary>
        /// In the radiation: ash falls around you, the air turns a sickly green-grey and a geiger
        /// counter clicks. All of it fades back out when you leave.
        /// </summary>
        private void TickFallout(World world, EntityPlayer player, ulong now)
        {
            TickFrontCloud(now);
            if (player == null)
                return;
            TickHorizonWall(player, now);
            Vector3 p = GameApi.Position(player);
            float fx;
            int strip;
            bool hot = GameApi.IsAlive(player) && IsIrradiated(p, now, out fx, out strip);
            if (ash == null && hot)
                ash = Ashfall.Create(KaijuDirector.Instance.EffectMaterial("KaijuSmoke"));
            if (ash != null)
            {
                ash.Target = hot ? 1f : 0f;
                ash.Follow(GameApi.WorldToScene(p));
            }
            if (hot && !fogOn)
            {
                GameApi.SetFog(world, FalloutFog, FalloutFogDensity);
                fogOn = true;
            }
            else if (!hot && fogOn)
            {
                GameApi.ClearFog(world);
                fogOn = false;
            }
            if (hot && Time.time >= nextGeiger)
            {
                GameApi.PlaySound(player, "buff_geiger_counter");
                nextGeiger = Time.time + Random.Range(0.15f, 0.7f);
            }
        }

        // ---- Oxygen Destroyer: part crates

        /// <summary>
        /// Places each part crate once its city's ground is loaded (in the POI nearest the city
        /// centre, on a free ground-floor cell), then keeps its light and compass marker up to date.
        /// </summary>
        private void TickParts(World world, EntityPlayer player)
        {
            Vector3 p = player != null ? GameApi.Position(player) : Vector3.zero;
            for (int i = 0; i < Parts; i++)
            {
                Settlement city = data.EndCity(i);
                if (city == null)
                    continue;
                if (!st.CratePlaced[i])
                {
                    Vector3i spot;
                    if (FindCrateSpot(world, city, data.SiteFor(i + 1), out spot) && GameApi.PlaceBlock(world, spot, CrateBlock(i)))
                    {
                        st.CratePlaced[i] = true;
                        st.CratePos[i] = spot;
                        Save();
                        Log.Out("[KaijuMod] Oxygen Destroyer part " + (i + 1) + " crate placed in " + city.Name + " at " + spot);
                    }
                    continue;
                }
                if (player == null)
                    continue;
                Vector3 crate = new Vector3(st.CratePos[i].x + 0.5f, st.CratePos[i].y + 0.5f, st.CratePos[i].z + 0.5f);
                bool near = (crate - p).sqrMagnitude < 350f * 350f;
                bool hasPart = GameApi.ItemCount(player, PartItem(i)) > 0;
                bool loaded = GameApi.IsChunkLoaded(world, st.CratePos[i].x, st.CratePos[i].z);
                bool crateThere = !loaded || GameApi.BlockName(world, st.CratePos[i]) == CrateBlock(i);
                if (!st.PartTaken[i] && (hasPart || !crateThere || (loaded && GameApi.LootTakenFrom(world, st.CratePos[i], PartItem(i)))))
                {
                    st.PartTaken[i] = true;
                    Save();
                    Log.Out("[KaijuMod] Oxygen Destroyer part " + (i + 1) + " taken from its crate");
                }
                // Once taken it stays taken: stashing or crafting the part must not bring the marker back.
                hasPart = hasPart || st.PartTaken[i];
                // Light: while nearby and the part is still out there.
                if (near && !hasPart && crateThere && crateLights[i] == null)
                    crateLights[i] = Beacon.Create(crate + Vector3.up, new Color(0.5f, 0.85f, 1f), 14f, 4f);
                else if ((!near || hasPart || !crateThere) && crateLights[i] != null)
                {
                    Object.Destroy(crateLights[i].gameObject);
                    crateLights[i] = null;
                }
                // Compass marker: once at the city or its military site, until you carry the part.
                var site = data.SiteFor(i + 1);
                bool inCity = InCity(city, p, 40f)
                    || (site != null && (new Vector2(p.x, p.z) - site.Centre).magnitude < Mathf.Max(site.W, site.D) + 100f);
                if (inCity && !hasPart && markers[i] == null)
                    markers[i] = GameApi.AddMarker("kaiju_part", crate);
                else if ((!inCity || hasPart) && markers[i] != null)
                {
                    GameApi.RemoveMarker(markers[i]);
                    markers[i] = null;
                }
            }
            // The armed device glows red.
            if (st.DeviceArmed && deviceLight == null)
                deviceLight = Beacon.Create(new Vector3(st.DevicePos.x + 0.5f, st.DevicePos.y + 2f, st.DevicePos.z + 0.5f), new Color(1f, 0.25f, 0.2f), 10f, 3f);
            else if (!st.DeviceArmed && deviceLight != null)
            {
                Object.Destroy(deviceLight.gameObject);
                deviceLight = null;
            }
        }

        private bool FindCrateSpot(World world, Settlement city, PartSite site, out Vector3i spot)
        {
            spot = default(Vector3i);
            if (site != null)
                return FindSiteSpot(world, site, out spot);
            if (!GameApi.IsChunkLoaded(world, Mathf.RoundToInt(city.X), Mathf.RoundToInt(city.Z)))
                return false;
            // Older worlds: the POI nearest the city centre (not a trader), else the centre itself.
            PrefabInstance best = null;
            float bestD = float.MaxValue;
            foreach (var poi in GameApi.Pois())
            {
                if (poi == null || poi.name == null || poi.name.StartsWith("trader_"))
                    continue;
                float cx = poi.boundingBoxPosition.x + poi.boundingBoxSize.x * 0.5f;
                float cz = poi.boundingBoxPosition.z + poi.boundingBoxSize.z * 0.5f;
                if (Mathf.Abs(cx - city.X) > city.HalfWidth || Mathf.Abs(cz - city.Z) > 2.2f * CityAttack.BlockSpacing)
                    continue;
                float d = (cx - city.X) * (cx - city.X) + (cz - city.Z) * (cz - city.Z);
                if (d < bestD)
                {
                    bestD = d;
                    best = poi;
                }
            }
            int x0, z0, rx, rz;
            if (best != null)
            {
                x0 = best.boundingBoxPosition.x + best.boundingBoxSize.x / 2;
                z0 = best.boundingBoxPosition.z + best.boundingBoxSize.z / 2;
                rx = Mathf.Max(1, best.boundingBoxSize.x / 2 - 2);
                rz = Mathf.Max(1, best.boundingBoxSize.z / 2 - 2);
            }
            else
            {
                x0 = Mathf.RoundToInt(city.X);
                z0 = Mathf.RoundToInt(city.Z);
                rx = rz = 10;
            }
            if (!GameApi.IsChunkLoaded(world, x0, z0))
                return false;
            int ground = Mathf.RoundToInt(city.Y);
            // Spiral out from the POI centre; first free ground-floor cell with a floor under it.
            for (int r = 0; r <= Mathf.Max(rx, rz); r++)
            {
                for (int dx = -r; dx <= r; dx++)
                {
                    for (int dz = -r; dz <= r; dz++)
                    {
                        if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dz)) != r || Mathf.Abs(dx) > rx || Mathf.Abs(dz) > rz)
                            continue;
                        int x = x0 + dx, z = z0 + dz;
                        if (!GameApi.IsChunkLoaded(world, x, z))
                            continue;
                        for (int y = ground - 1; y <= ground + 6; y++)
                        {
                            var c = new Vector3i(x, y, z);
                            if (GameApi.IsAir(world, c) && GameApi.IsAir(world, new Vector3i(x, y + 1, z)) && !GameApi.IsAir(world, new Vector3i(x, y - 1, z)))
                            {
                                spot = c;
                                return true;
                            }
                        }
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// A free cell inside the part's military POI, as near its middle as possible, at or just
        /// above its ground level (camps and checkpoints are mostly one storey; the bunker's
        /// surface buildings sit on the ground too).
        /// </summary>
        private bool FindSiteSpot(World world, PartSite site, out Vector3i spot)
        {
            spot = default(Vector3i);
            int x0 = Mathf.RoundToInt(site.Centre.x), z0 = Mathf.RoundToInt(site.Centre.y);
            if (!GameApi.IsChunkLoaded(world, x0, z0))
                return false;
            int rx = Mathf.Max(2, Mathf.RoundToInt(site.W / 2f) - 3), rz = Mathf.Max(2, Mathf.RoundToInt(site.D / 2f) - 3);
            int ground = Mathf.RoundToInt(site.Y);
            for (int r = 0; r <= Mathf.Max(rx, rz); r++)
            {
                for (int dx = -r; dx <= r; dx++)
                {
                    for (int dz = -r; dz <= r; dz++)
                    {
                        if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dz)) != r || Mathf.Abs(dx) > rx || Mathf.Abs(dz) > rz)
                            continue;
                        int x = x0 + dx, z = z0 + dz;
                        if (!GameApi.IsChunkLoaded(world, x, z))
                            continue;
                        for (int y = ground - 1; y <= ground + 4; y++)
                        {
                            var c = new Vector3i(x, y, z);
                            if (GameApi.IsAir(world, c) && GameApi.IsAir(world, new Vector3i(x, y + 1, z)) && !GameApi.IsAir(world, new Vector3i(x, y - 1, z)))
                            {
                                spot = c;
                                return true;
                            }
                        }
                    }
                }
            }
            return false;
        }

        private static bool InCity(Settlement city, Vector3 p, float margin)
        {
            return Mathf.Abs(p.x - city.X) <= city.HalfWidth + margin
                && Mathf.Abs(p.z - city.Z) <= 2f * CityAttack.BlockSpacing + margin;
        }

        private void ClearMarkers()
        {
            for (int i = 0; i < Parts; i++)
            {
                GameApi.RemoveMarker(markers[i]);
                markers[i] = null;
                if (crateLights[i] != null)
                    Object.Destroy(crateLights[i].gameObject);
                crateLights[i] = null;
            }
            if (deviceLight != null)
                Object.Destroy(deviceLight.gameObject);
            deviceLight = null;
        }

        /// <summary>Gives the local player a part (1-4) for testing.</summary>
        public bool GivePart(int i)
        {
            var player = boundWorld != null ? GameApi.LocalPlayer(boundWorld) : null;
            return player != null && i >= 0 && i < Parts && GameApi.GiveItem(player, PartItem(i));
        }

        public string PartsStatus()
        {
            if (data == null)
                return "No Oxygen Destroyer parts: this world has no " + KaijuWorldData.FileName + ".";
            var player = GameApi.LocalPlayer(boundWorld);
            var s = "Oxygen Destroyer parts:";
            for (int i = 0; i < Parts; i++)
            {
                Settlement city = data.EndCity(i);
                bool has = player != null && GameApi.ItemCount(player, PartItem(i)) > 0;
                var site = data.SiteFor(i + 1);
                s += "\n  " + (i + 1) + ". " + (city != null ? city.Name : "?")
                    + (site != null ? " (" + site.Name + ", tier " + site.Tier + ", at " + Mathf.RoundToInt(site.Centre.x) + " " + Mathf.RoundToInt(site.Centre.y) + ")" : "") + ": "
                    + (st.PartTaken[i] ? "taken" : st.CratePlaced[i] ? "crate at " + st.CratePos[i] : "crate not placed yet (placed when you get near)")
                    + (has ? ", YOU HAVE IT" : "");
            }
            s += "\nDevice: " + (st.DeviceArmed ? "armed at " + st.DevicePos + (InAshmouth(st.DevicePos) ? " (in Ashmouth or on its shore)" : " (NOT in Ashmouth or on its shore)") : "not armed")
                + ". Finale: " + (st.Outcome == "won" ? "won" : st.Outcome == "lost" ? "lost" : st.FinaleActive ? "in progress" : "after the last blood moon") + ".";
            return s;
        }

        // ---- Oxygen Destroyer: part quests from the part cities' traders

        /// <summary>
        /// The part (0-based) whose quest this trader should offer, or -1: the trader must be in a
        /// part's city, and the player must not have the part, the device, the quest (active or
        /// done), and the part must still be in its crate. sitePos/siteSize: where the quest leads.
        /// </summary>
        public int PartQuestFor(EntityTrader trader, EntityPlayer player, out Vector3 sitePos, out Vector3 siteSize)
        {
            sitePos = siteSize = Vector3.zero;
            if (data == null || trader == null || player == null)
                return -1;
            Vector3 tp = trader.traderArea != null ? (Vector3)trader.traderArea.Position : trader.position;
            for (int i = 0; i < Parts; i++)
            {
                Settlement city = data.EndCity(i);
                if (city == null || !InCity(city, tp, 30f))
                    continue;
                if (st.PartTaken[i] || GameApi.ItemCount(player, PartItem(i)) > 0 || GameApi.ItemCount(player, "kaijuOxygenDestroyer") > 0 || st.DeviceArmed)
                    return -1;
                // VERIFIED (V3.3): QuestJournal.FindActiveOrCompleteQuest(name, faction = -1).
                if (player.QuestJournal != null && player.QuestJournal.FindActiveOrCompleteQuest(KaijuPartQuests.QuestId(i)) != null)
                    return -1;
                World world = GameApi.World;
                if (st.CratePlaced[i] && world != null && GameApi.IsChunkLoaded(world, st.CratePos[i].x, st.CratePos[i].z)
                    && GameApi.BlockName(world, st.CratePos[i]) != CrateBlock(i))
                    return -1; // crate already emptied and gone
                var site = data.SiteFor(i + 1);
                if (site != null)
                {
                    sitePos = new Vector3(site.X, site.Y, site.Z);
                    siteSize = new Vector3(site.W, 20f, site.D);
                }
                else if (st.CratePlaced[i])
                {
                    sitePos = new Vector3(st.CratePos[i].x - 15, st.CratePos[i].y, st.CratePos[i].z - 15);
                    siteSize = new Vector3(30f, 20f, 30f);
                }
                else
                {
                    sitePos = new Vector3(city.X - 50f, city.Y, city.Z - 50f);
                    siteSize = new Vector3(100f, 20f, 100f);
                }
                return i;
            }
            return -1;
        }

        // ---- Oxygen Destroyer: the device and the finale

        public bool IsArmed(Vector3i pos)
        {
            return st.DeviceArmed && st.DevicePos == pos;
        }

        public void SetArmed(Vector3i pos, bool armed, EntityPlayer player)
        {
            st.DeviceArmed = armed;
            st.DevicePos = pos;
            Save();
            if (!armed)
            {
                GameApi.Tooltip(player, "Oxygen Destroyer disarmed.");
                return;
            }
            Log.Out("[KaijuMod] Oxygen Destroyer armed at " + pos);
            GameApi.Tooltip(player, InAshmouth(pos)
                ? "Oxygen Destroyer armed. It goes off when Godzilla comes within " + Mathf.RoundToInt(DestroyerRadius) + " m."
                : "Oxygen Destroyer armed, but not in " + (FinalCity() != null ? FinalCity().Name : "the last city") + " or on its shore. He attacks there.");
        }

        public void DeviceRemoved(Vector3i pos)
        {
            if (st.DevicePos != pos || !st.DeviceArmed)
                return;
            st.DeviceArmed = false;
            Save();
        }

        private Settlement FinalCity()
        {
            return data != null ? data.EndCity(data.Strips - 1) : null;
        }

        /// <summary>
        /// Where the device counts: the last city, or the beach and shallows in front of it out to
        /// the map edge (in the Minus One attack he comes ashore there and fires from just outside
        /// the city, so he never walks into it).
        /// </summary>
        private bool InAshmouth(Vector3i pos)
        {
            Settlement city = FinalCity();
            if (city == null)
                return false;
            var p = new Vector3(pos.x, pos.y, pos.z);
            if (InCity(city, p, 0f))
                return true;
            float sea = city.X < 0 ? -1f : 1f;
            return (p.x - city.X) * sea >= city.HalfWidth
                && Mathf.Abs(p.z - city.Z) <= 2f * CityAttack.BlockSpacing + 60f;
        }

        /// <summary>During the last city's attack: armed device in the city or on its shore and he is within range: he dies.</summary>
        private void TickFinale()
        {
            if (!st.FinaleActive)
                return;
            var director = KaijuDirector.Instance;
            Settlement city = FinalCity();
            if (director.Running)
            {
                if (director.Dying || city == null || director.Attacking != city.Name)
                    return;
                if (st.DeviceArmed && InAshmouth(st.DevicePos)
                    && (director.Position - new Vector2(st.DevicePos.x, st.DevicePos.z)).sqrMagnitude <= DestroyerRadius * DestroyerRadius)
                {
                    director.Die(new Vector3(st.DevicePos.x + 0.5f, st.DevicePos.y, st.DevicePos.z + 0.5f));
                    st.Outcome = "won";
                    st.DeviceArmed = false;
                    Save();
                    TellPlayer("Godzilla is dead. You won the run.");
                }
                return;
            }
            // The attack is over.
            st.FinaleActive = false;
            if (st.Outcome != "won")
            {
                st.Outcome = "lost";
                TellPlayer((city != null ? city.Name : "The last city") + " has fallen. Godzilla survived: the run is lost.");
            }
            Save();
        }

        private void TellPlayer(string text)
        {
            Log.Out("[KaijuMod] " + text);
            var player = boundWorld != null ? GameApi.LocalPlayer(boundWorld) : null;
            if (player != null)
                GameApi.Tooltip(player, text);
        }

        // ---- status

        public string Status()
        {
            World world = GameApi.World;
            if (world == null)
                return "No world loaded.";
            if (data == null)
                return "The Run is off: this world has no " + KaijuWorldData.FileName + ".";
            ulong now = GameApi.WorldTime(world);
            var dd = GameApi.DuskDawn();
            string s = "The Run on " + data.World + ": day " + GameApi.Day(now) + " " + GameApi.Hour(now).ToString("00") + ":00, next blood moon day "
                + GameApi.BloodMoonDay() + " (dusk " + dd.Item1 + ":00, dawn " + dd.Item2 + ":00)";
            if (!st.StartAttacked)
                return s + "\nStart city " + data.StartCity?.Name + " is attacked " + Mathf.Max(0, Mathf.RoundToInt(StartAttackDelay - startTimer)) + " s after you are in the world.";
            if (st.Complete)
                s += "\nAll " + data.Strips + " strips done; the run is complete.";
            else
            {
                var end = data.EndCity(st.Strip);
                s += "\nStrip " + st.Strip + " (" + end?.Biome + "), next attack: " + end?.Name + " when the blood moon horde ends.";
                if (now < st.FrontStart)
                    s += "\nGrace: the front starts in " + ((st.FrontStart - now) / 1000f).ToString("0.0", CultureInfo.InvariantCulture) + " h"
                        + (st.Strip > 0 ? "; strip " + (st.Strip - 1) + " sweep at x " + Mathf.RoundToInt(SweepX(st.Strip - 1, now)) : "") + ".";
                else
                    s += "\nRadiation front at x " + Mathf.RoundToInt(FrontX(st.Strip, now)) + ", moving " + (data.Direction(st.Strip) > 0 ? "east" : "west") + ".";
            }
            s += "\nRadiation " + (st.RadiationOn ? "on" : "off") + ".";
            int have = 0;
            var me = GameApi.LocalPlayer(world);
            for (int i = 0; i < Parts; i++)
                if (me != null && GameApi.ItemCount(me, PartItem(i)) > 0)
                    have++;
            s += " Oxygen Destroyer: " + have + "/" + Parts + " parts carried, device " + (st.DeviceArmed ? "ARMED" : "not armed")
                + (st.Outcome != "" ? ", run " + st.Outcome.ToUpperInvariant() : st.FinaleActive ? ", finale in progress" : "") + ".";
            var player = GameApi.LocalPlayer(world);
            if (player != null)
            {
                float fx;
                int strip;
                Vector3 p = GameApi.Position(player);
                bool hot = IsIrradiated(p, now, out fx, out strip);
                s += " You are in strip " + data.StripOf(p.z) + " at x " + Mathf.RoundToInt(p.x) + (hot ? ", IN the radiation." : ", clear of it.");
            }
            return s;
        }

        // ---- persistence

        private string StatePath()
        {
            string dir = GameApi.SaveFolder();
            return string.IsNullOrEmpty(dir) ? null : Path.Combine(dir, StateFile);
        }

        private void Save()
        {
            string path = StatePath();
            if (path == null)
                return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                var doc = new XmlDocument();
                var e = doc.CreateElement("kaijurun");
                doc.AppendChild(e);
                e.SetAttribute("startAttacked", st.StartAttacked ? "1" : "0");
                e.SetAttribute("strip", st.Strip.ToString(CultureInfo.InvariantCulture));
                e.SetAttribute("frontStart", st.FrontStart.ToString(CultureInfo.InvariantCulture));
                e.SetAttribute("sweepStart", st.SweepStart.ToString(CultureInfo.InvariantCulture));
                e.SetAttribute("bloodMoonActive", st.BloodMoonActive.ToString(CultureInfo.InvariantCulture));
                e.SetAttribute("lastAttackedBloodMoon", st.LastAttackedBloodMoon.ToString(CultureInfo.InvariantCulture));
                e.SetAttribute("radiation", st.RadiationOn ? "1" : "0");
                e.SetAttribute("complete", st.Complete ? "1" : "0");
                e.SetAttribute("deviceArmed", st.DeviceArmed ? "1" : "0");
                e.SetAttribute("device", st.DevicePos.x + "," + st.DevicePos.y + "," + st.DevicePos.z);
                e.SetAttribute("finale", st.FinaleActive ? "1" : "0");
                e.SetAttribute("outcome", st.Outcome);
                foreach (string name in st.Ruined)
                {
                    var r = doc.CreateElement("ruined");
                    r.SetAttribute("name", name);
                    e.AppendChild(r);
                }
                foreach (string name in st.Blasted)
                {
                    var r = doc.CreateElement("blasted");
                    r.SetAttribute("name", name);
                    e.AppendChild(r);
                }
                for (int i = 0; i < Parts; i++)
                {
                    var c = doc.CreateElement("crate");
                    c.SetAttribute("part", (i + 1).ToString(CultureInfo.InvariantCulture));
                    c.SetAttribute("placed", st.CratePlaced[i] ? "1" : "0");
                    c.SetAttribute("taken", st.PartTaken[i] ? "1" : "0");
                    c.SetAttribute("pos", st.CratePos[i].x + "," + st.CratePos[i].y + "," + st.CratePos[i].z);
                    e.AppendChild(c);
                }
                doc.Save(path);
            }
            catch (System.Exception ex)
            {
                Log.Warning("[KaijuMod] Could not save " + path + ": " + ex.Message);
            }
        }

        private static Vector3i ParseV3i(string s)
        {
            var p = (s ?? "").Split(',');
            int x, y, z;
            if (p.Length == 3 && int.TryParse(p[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out x)
                && int.TryParse(p[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out y)
                && int.TryParse(p[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out z))
                return new Vector3i(x, y, z);
            return default(Vector3i);
        }

        private void Load()
        {
            string path = StatePath();
            if (path == null || !File.Exists(path))
                return;
            try
            {
                var doc = new XmlDocument();
                doc.Load(path);
                var e = doc.DocumentElement;
                st.StartAttacked = e.GetAttribute("startAttacked") == "1";
                st.Strip = int.Parse(e.GetAttribute("strip"), CultureInfo.InvariantCulture);
                st.FrontStart = ulong.Parse(e.GetAttribute("frontStart"), CultureInfo.InvariantCulture);
                st.SweepStart = ulong.Parse(e.GetAttribute("sweepStart"), CultureInfo.InvariantCulture);
                st.BloodMoonActive = int.Parse(e.GetAttribute("bloodMoonActive"), CultureInfo.InvariantCulture);
                st.LastAttackedBloodMoon = int.Parse(e.GetAttribute("lastAttackedBloodMoon"), CultureInfo.InvariantCulture);
                st.RadiationOn = e.GetAttribute("radiation") != "0";
                st.Complete = e.GetAttribute("complete") == "1";
                st.DeviceArmed = e.GetAttribute("deviceArmed") == "1";
                st.DevicePos = ParseV3i(e.GetAttribute("device"));
                st.FinaleActive = e.GetAttribute("finale") == "1";
                st.Outcome = e.GetAttribute("outcome") ?? "";
                foreach (XmlElement r in e.SelectNodes("ruined"))
                    st.Ruined.Add(r.GetAttribute("name"));
                foreach (XmlElement r in e.SelectNodes("blasted"))
                    st.Blasted.Add(r.GetAttribute("name"));
                foreach (XmlElement c in e.SelectNodes("crate"))
                {
                    int i;
                    if (!int.TryParse(c.GetAttribute("part"), NumberStyles.Integer, CultureInfo.InvariantCulture, out i) || i < 1 || i > Parts)
                        continue;
                    st.CratePlaced[i - 1] = c.GetAttribute("placed") == "1";
                    st.PartTaken[i - 1] = c.GetAttribute("taken") == "1";
                    st.CratePos[i - 1] = ParseV3i(c.GetAttribute("pos"));
                }
            }
            catch (System.Exception ex)
            {
                Log.Warning("[KaijuMod] Could not read " + path + ", starting a new run: " + ex.Message);
                st = new State();
            }
        }
    }
}

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
        /// <summary>In-game minutes after a blood moon horde ends (dawn) before he attacks the end city.</summary>
        public static int AttackDelayMinutes = 60;
        /// <summary>In-game hours after an end-city attack before the next strip's front starts.</summary>
        public static int GraceHours = 4;
        /// <summary>The front stops this far short of the end city's inland edge, metres.</summary>
        public static float FrontStopMargin = 200f;
        /// <summary>Radiation damage per second to players behind the front.</summary>
        public static int RadiationDamage = 4;
        /// <summary>Warn players who are this close ahead of the front, metres.</summary>
        public static float WarnDistance = 150f;
        /// <summary>The Oxygen Destroyer, crafted from the parts and dropped on him from a gyrocopter.</summary>
        public const string DeviceItem = "kaijuOxygenDestroyer";
        /// <summary>You can drop it when he is within this distance across the ground, metres (its fins steer it onto him).</summary>
        public static float DropRange = 150f;
        // The weapon for each strip's end-city attack, crafted from parts found in that strip's
        // towns (one part per town, offered as a quest by its trader): the missile battery (burnt
        // forest, Cinder Bay), the airstrike radio (desert, Dune Point), the maser cannon (snow,
        // Frostport) and the Oxygen Destroyer (wasteland, Ashmouth). The pine forest has none.
        public const string MissileBlock = "kaijuMissileControl", AirstrikeBlock = "kaijuAirstrikeRadio", MaserBlock = "kaijuMaserCannon";
        public static readonly string[] WeaponItems = { MissileBlock, AirstrikeBlock, MaserBlock, DeviceItem };
        private static readonly string[] PartPrefix = { "kaijuMissilePart", "kaijuAirstrikePart", "kaijuMaserPart", "kaijuOxygenDestroyerPart" };
        public const int PartsPerWeapon = 3;
        // One per WeaponItems entry times PartsPerWeapon; a const because Instance is built before the static fields below it.
        public const int Parts = 4 * PartsPerWeapon;
        public static int WeaponOf(int part) { return part / PartsPerWeapon; }
        public static int WeaponStrip(int weapon) { return weapon + 1; }
        public static string PartItem(int i) { return PartPrefix[WeaponOf(i)] + (i % PartsPerWeapon + 1); }
        public static string CrateBlock(int i) { return "kaijuPartCrate" + (i + 1); }
        public static int MissileSalvo = 8;
        /// <summary>Seconds between salvos, and how far the missiles reach, metres.</summary>
        public static float MissileReload = 30f, MissileRange = 2500f;
        /// <summary>How far the airstrike and the maser reach, metres.</summary>
        public static float AirstrikeRange = 3000f, MaserRange = 1500f;
        /// <summary>Gas for each third of the maser's charge.</summary>
        public const string Fuel = "ammoGasCan";
        public static int FuelPerCharge = 25;

        private const string StateFile = "kaiju_run.xml";

        private class State
        {
            public bool StartAttacked;
            public int Strip = -1;          // strip whose front is (or will be) moving; -1 before the start attack
            public ulong FrontStart;        // world time the current strip's front starts moving
            public ulong SweepStart;        // world time of the last end-city attack (previous strip's sweep)
            public int BloodMoonActive;     // blood moon day seen in progress, 0 if none
            public int LastAttackedBloodMoon;
            public ulong AttackAt;          // world time the end city's attack is due (after the horde), 0 if none
            public bool RadiationOn = true;
            public bool Complete;
            // Weapon parts
            public readonly bool[] CratePlaced = new bool[Parts];
            public readonly Vector3i[] CratePos = new Vector3i[Parts];
            // Part taken from its crate (held once, or the crate opened and emptied of it, or the
            // crate gone): its marker, light and trader quest never come back.
            public readonly bool[] PartTaken = new bool[Parts];
            // The maser's charge (thirds) and whether it has fired its one shot.
            public int MaserCharge;
            public bool MaserUsed;
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
        private bool prewarmFront;   // the first cloud after a load starts fully formed
        private bool journalChecked; // trader quests in the journal pointed along the run, once per load
        /// <summary>Size of the dark cloud bank over the radiation front, metres.</summary>
        public static float FrontCloudWidth = 2600f, FrontCloudDepth = 1800f;
        private float nextGeiger;

        // Not saved: markers and lights are rebuilt as the player moves.
        private readonly NavObject[] markers = new NavObject[Parts];
        private readonly Beacon[] crateLights = new Beacon[Parts];
        private float partsTimer;
        private float missilesReady;   // Time.time the battery can fire again
        private string airstrikeFor;   // the attack the airstrike was called on (one per attack)
        private bool gyroHinted;       // told you how to drop it since you got in

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
            long t0 = KaijuPerf.Begin();
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

            if (!journalChecked && player != null && player.QuestJournal != null)
            {
                journalChecked = true;
                try { KaijuStarterTrader.RepairJournal(player); }
                catch (System.Exception e) { Log.Warning("[KaijuMod] Quest journal check failed: " + e.Message); }
            }

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
            // Saved, so it still comes after a reload, or at once if you slept past it.
            if (st.AttackAt > 0 && now >= st.AttackAt)
                AttackEndCity(now);

            TickFinale();
            TickGyro(player);
            KaijuPerf.End("run", t0);

            long t = KaijuPerf.Begin();
            radTimer += dt;
            if (radTimer >= 1f && KaijuPerf.On("radiation"))
            {
                radTimer = 0f;
                ApplyRadiation(world, now);
            }
            KaijuPerf.End("radiation", t);
            t = KaijuPerf.Begin();
            if (KaijuPerf.On("fallout"))
                TickFallout(world, player, now);
            KaijuPerf.End("fallout", t);
            t = KaijuPerf.Begin();
            partsTimer += dt;
            if (partsTimer >= 1f && KaijuPerf.On("parts"))
            {
                partsTimer = 0f;
                TickParts(world, player);
            }
            KaijuPerf.End("parts", t);
        }

        private void Bind(World world)
        {
            ClearMarkers();
            ClearFallout(world);
            boundWorld = world;
            prewarmFront = true;
            journalChecked = false;
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
            if (st.Strip >= 0 && !st.Complete)
            {
                st.AttackAt = now + (ulong)(AttackDelayMinutes * 1000 / 60); // 1000 world time per in-game hour
                Log.Out("[KaijuMod] Horde over; " + data.EndCity(st.Strip)?.Name + " is attacked in " + AttackDelayMinutes + " in-game minutes");
            }
            Save();
        }

        private void AttackEndCity(ulong now)
        {
            st.AttackAt = 0;
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
            if (st.AttackAt > 0)
                return stop; // waiting for him after the horde: the front stays where the blood moon left it
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
            var player = GameApi.LocalPlayer(boundWorld);
            if (player != null && GameApi.IsAlive(player))
                GameApi.Tooltip(player, cityName + " is gone. The whole city is irradiated.");
        }

        /// <summary>True if a position is at one of the part sites (the mountain posts above the cities).</summary>
        public bool AtPartSite(Vector3 p)
        {
            if (data == null)
                return false;
            foreach (var s in data.PartSites)
                if (p.x >= s.X - 20f && p.x <= s.X + s.W + 20f && p.z >= s.Z - 20f && p.z <= s.Z + s.D + 20f)
                    return true;
            return false;
        }

        /// <summary>The blasted city a position is in (with a margin), or null.</summary>
        private Settlement BlastedCityAt(Vector3 p)
        {
            foreach (string name in st.Blasted)
            {
                var c = data.Find(name);
                if (c != null && Mathf.Abs(p.x - c.X) <= c.HalfWidth + 40f
                    && Mathf.Abs(p.z - c.Z) <= c.HalfDepth + 45f)
                    return c;
            }
            return null;
        }

        // ---- Fallout

        /// <summary>Keeps the dark cloud bank hanging over the irradiated side of the front.</summary>
        private void TickFrontCloud(ulong now)
        {
            float fx;
            int strip;
            bool on = CurrentFront(now, out fx, out strip);
            if (on && frontCloud == null)
            {
                float h = KaijuDirector.Instance.Footprint.Height * KaijuEffects.ExplosionScale;
                // Smoke that ignores fog, so it stays dark through storms (older bundles: the normal smoke).
                var smoke = KaijuDirector.Instance.EffectMaterial("KaijuSmokeNoFog") ?? KaijuDirector.Instance.EffectMaterial("KaijuSmoke");
                frontCloud = FrontCloud.Create(smoke, h,
                    FrontCloudWidth, FrontCloudDepth, prewarmFront);
            }
            prewarmFront = false;
            if (frontCloud == null)
                return;
            frontCloud.SetEmitting(on);
            if (!on)
                return;
            float stripZ = -data.Size / 2f + (strip + 0.5f) * data.StripHeight;
            // From the ground up to the old cap height: a wall of smoke, not just a cloud in the sky.
            float capY = data.SeaLevel + KaijuDirector.Instance.Footprint.Height * KaijuEffects.ExplosionScale * 1.9f;
            frontCloud.MoveTo(new Vector3(fx - data.Direction(strip) * FrontCloudDepth * 0.5f, capY, stripZ));
        }

        /// <summary>Shows or hides the smoke: fallout over ruined cities, the cloud bank over the front, ash (`kaiju perf off clouds`).</summary>
        public void ShowClouds(bool show)
        {
            foreach (var f in fallout.Values)
                if (f != null)
                    f.gameObject.SetActive(show);
            if (frontCloud != null)
                frontCloud.gameObject.SetActive(show);
            if (ash != null)
                ash.gameObject.SetActive(show);
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

        // ---- weapon parts: one crate in each town of the strip, offered by its trader

        /// <summary>Town (0-2 in its strip, in kaiju.xml order) and strip of a part.</summary>
        private Settlement TownFor(int part)
        {
            var towns = data.Towns(WeaponStrip(WeaponOf(part)));
            int k = part % PartsPerWeapon;
            return k < towns.Count ? towns[k] : null;
        }

        /// <summary>
        /// Places each part crate once its town's ground is loaded (in the POI nearest the town
        /// centre, on a free ground-floor cell), then keeps its light and compass marker up to date.
        /// </summary>
        private void TickParts(World world, EntityPlayer player)
        {
            Vector3 p = player != null ? GameApi.Position(player) : Vector3.zero;
            for (int i = 0; i < Parts; i++)
            {
                Settlement town = TownFor(i);
                if (town == null)
                    continue;
                if (!st.CratePlaced[i])
                {
                    Vector3i spot;
                    byte rot;
                    if (FindQuestChest(world, town, out spot, out rot) && GameApi.PlaceBlock(world, spot, CrateBlock(i), rot))
                    {
                        st.CratePlaced[i] = true;
                        st.CratePos[i] = spot;
                        Save();
                        Log.Out("[KaijuMod] " + PartItem(i) + " crate placed in " + town.Name + " at " + spot);
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
                    Log.Out("[KaijuMod] " + PartItem(i) + " taken from its crate");
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
                // No compass marker of its own any more: the quest's marker leads there (Darwin found
                // the extra one noisy); one left from an older version is taken down.
                if (markers[i] != null)
                {
                    GameApi.RemoveMarker(markers[i]);
                    markers[i] = null;
                }
            }
        }

        // POIs already scanned and found to have no main chest (scanning one is costly).
        private readonly HashSet<PrefabInstance> noChest = new HashSet<PrefabInstance>();
        // A POI's main loot chest: the reinforced (hero) chest or hardened chest at the end of it.
        private static readonly string[] MainChests = { "cntLootChestHero", "cntHardenedChest" };

        /// <summary>
        /// Where a town's part goes: the main loot chest of its best quest POI (highest tier, then
        /// nearest the centre; with a Rally block so its quests work), which the part crate (a hero
        /// chest with the part on top of that chest's usual loot) replaces. Else a free floor cell
        /// in the POI nearest the centre. False until that ground is loaded.
        /// </summary>
        private bool FindQuestChest(World world, Settlement town, out Vector3i spot, out byte rotation)
        {
            spot = default(Vector3i);
            rotation = 0;
            // Nothing to do until you are there: the town's middle must be loaded.
            if (!GameApi.IsChunkLoaded(world, Mathf.RoundToInt(town.X), Mathf.RoundToInt(town.Z)))
                return false;
            var pois = new List<PrefabInstance>();
            foreach (var poi in GameApi.Pois())
            {
                if (poi == null || poi.name == null || poi.name.StartsWith("trader_") || poi.name.StartsWith("sign_"))
                    continue;
                float cx = poi.boundingBoxPosition.x + poi.boundingBoxSize.x * 0.5f;
                float cz = poi.boundingBoxPosition.z + poi.boundingBoxSize.z * 0.5f;
                if (Mathf.Abs(cx - town.X) <= town.HalfWidth && Mathf.Abs(cz - town.Z) <= town.HalfDepth && GameApi.HasRallyBlock(poi))
                    pois.Add(poi);
            }
            pois.Sort((a, b) =>
            {
                int t = GameApi.PoiTier(b).CompareTo(GameApi.PoiTier(a));
                if (t != 0)
                    return t;
                Vector2 ca = new Vector2(a.boundingBoxPosition.x + a.boundingBoxSize.x * 0.5f - town.X, a.boundingBoxPosition.z + a.boundingBoxSize.z * 0.5f - town.Z);
                Vector2 cb = new Vector2(b.boundingBoxPosition.x + b.boundingBoxSize.x * 0.5f - town.X, b.boundingBoxPosition.z + b.boundingBoxSize.z * 0.5f - town.Z);
                return ca.sqrMagnitude.CompareTo(cb.sqrMagnitude);
            });
            foreach (var poi in pois)
            {
                if (noChest.Contains(poi))
                    continue;
                bool ready;
                if (GameApi.FindPoiBlock(world, poi, MainChests, out spot, out rotation, out ready))
                {
                    Log.Out("[KaijuMod] " + town.Name + ": the part goes in the main loot chest of " + poi.name + " (tier " + GameApi.PoiTier(poi) + ")");
                    return true;
                }
                if (!ready)
                    return false; // that POI is not all loaded yet: try again later
                noChest.Add(poi); // scanned all of it: never again
            }
            rotation = 0;
            return FindCrateSpot(world, town, out spot);
        }

        /// <summary>A free ground-floor cell in the POI nearest a town's centre (not its trader).</summary>
        private bool FindCrateSpot(World world, Settlement town, out Vector3i spot)
        {
            spot = default(Vector3i);
            if (!GameApi.IsChunkLoaded(world, Mathf.RoundToInt(town.X), Mathf.RoundToInt(town.Z)))
                return false;
            PrefabInstance best = null;
            float bestD = float.MaxValue;
            foreach (var poi in GameApi.Pois())
            {
                if (poi == null || poi.name == null || poi.name.StartsWith("trader_"))
                    continue;
                float cx = poi.boundingBoxPosition.x + poi.boundingBoxSize.x * 0.5f;
                float cz = poi.boundingBoxPosition.z + poi.boundingBoxSize.z * 0.5f;
                if (Mathf.Abs(cx - town.X) > town.HalfWidth || Mathf.Abs(cz - town.Z) > town.HalfDepth)
                    continue;
                float d = (cx - town.X) * (cx - town.X) + (cz - town.Z) * (cz - town.Z);
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
                x0 = Mathf.RoundToInt(town.X);
                z0 = Mathf.RoundToInt(town.Z);
                rx = rz = 10;
            }
            if (!GameApi.IsChunkLoaded(world, x0, z0))
                return false;
            int ground = Mathf.RoundToInt(town.Y);
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

        private static bool InCity(Settlement city, Vector3 p, float margin)
        {
            return Mathf.Abs(p.x - city.X) <= city.HalfWidth + margin
                && Mathf.Abs(p.z - city.Z) <= city.HalfDepth - 10f + margin;
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
        }

        /// <summary>Gives the local player a part (0-based) for testing.</summary>
        public bool GivePart(int i)
        {
            var player = boundWorld != null ? GameApi.LocalPlayer(boundWorld) : null;
            return player != null && i >= 0 && i < Parts && GameApi.GiveItem(player, PartItem(i));
        }

        public string PartsStatus()
        {
            if (data == null)
                return "No weapon parts: this world has no " + KaijuWorldData.FileName + ".";
            var player = GameApi.LocalPlayer(boundWorld);
            var s = "Weapon parts (one in each town, from its trader):";
            for (int i = 0; i < Parts; i++)
            {
                Settlement town = TownFor(i);
                bool has = player != null && GameApi.ItemCount(player, PartItem(i)) > 0;
                s += "\n  " + (i + 1) + ". " + PartItem(i) + " in " + (town != null ? town.Name : "?") + ": "
                    + (st.PartTaken[i] ? "taken" : st.CratePlaced[i] ? "crate at " + st.CratePos[i] : "crate not placed yet (placed when you get near)")
                    + (has ? ", YOU HAVE IT" : "");
            }
            s += "\nMaser: " + (st.MaserUsed ? "used" : "charge " + (st.MaserCharge * 100 / 3) + "%")
                + ". Device: " + (player != null && GameApi.ItemCount(player, DeviceItem) > 0 ? "YOU HAVE IT" : "not carried")
                + ". Finale: " + (st.Outcome == "won" ? "won" : st.Outcome == "lost" ? "lost" : st.FinaleActive ? "in progress" : "after the last blood moon") + ".";
            return s;
        }

        /// <summary>
        /// The part (0-based) whose quest this trader should offer, or -1: the trader must be in a
        /// part's town, and the player must not have the part, its weapon, the quest (active or
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
                Settlement town = TownFor(i);
                if (town == null || !InCity(town, tp, 30f))
                    continue;
                if (st.PartTaken[i] || GameApi.ItemCount(player, PartItem(i)) > 0 || GameApi.ItemCount(player, WeaponItems[WeaponOf(i)]) > 0)
                    return -1;
                // VERIFIED (V3.3): QuestJournal.FindActiveOrCompleteQuest(name, faction = -1).
                if (KaijuPartQuests.HasQuest(player, i))
                    return -1;
                World world = GameApi.World;
                if (st.CratePlaced[i] && world != null && GameApi.IsChunkLoaded(world, st.CratePos[i].x, st.CratePos[i].z)
                    && GameApi.BlockName(world, st.CratePos[i]) != CrateBlock(i))
                    return -1; // crate already emptied and gone
                if (st.CratePlaced[i])
                {
                    sitePos = new Vector3(st.CratePos[i].x - 15, st.CratePos[i].y, st.CratePos[i].z - 15);
                    siteSize = new Vector3(30f, 20f, 30f);
                }
                else
                {
                    sitePos = new Vector3(town.X - 50f, town.Y, town.Z - 50f);
                    siteSize = new Vector3(100f, 20f, 100f);
                }
                return i;
            }
            return -1;
        }

        // ---- the weapons, crafted from the parts and placed by you: missiles, airstrike, maser

        /// <summary>He is out, not dying, and within range of a world position.</summary>
        private static bool Targetable(Vector3i pos, float range)
        {
            var d = KaijuDirector.Instance;
            return d.Running && !d.Dying && (d.Position - new Vector2(pos.x, pos.z)).sqrMagnitude <= range * range;
        }

        /// <summary>The radial menu's commands for a weapon block (Localization blockcommand_*).</summary>
        public string[] WeaponCommands(string block)
        {
            return block == AirstrikeBlock ? new[] { "call", "take" } : block == MaserBlock ? new[] { "fuel", "fire", "take" } : new[] { "fire", "take" };
        }

        /// <summary>What a weapon block says when you look at it.</summary>
        public string WeaponText(string block, Vector3i pos)
        {
            switch (block)
            {
                case MissileBlock:
                    return "Missile battery: " + (Time.time < missilesReady ? "reloading, " + Mathf.CeilToInt(missilesReady - Time.time) + " s"
                        : Targetable(pos, MissileRange) ? "ready, target in range" : "ready, no target");
                case AirstrikeBlock:
                    return "Airstrike radio: " + (Targetable(pos, AirstrikeRange)
                        ? (airstrikeFor == KaijuDirector.Instance.Attacking ? "jets on their way" : "target in range, call it in")
                        : "no target");
                case MaserBlock:
                    return "Maser cannon: " + (st.MaserUsed ? "burnt out" : st.MaserCharge < 3 ? "charge " + (st.MaserCharge * 100 / 3) + "%, fuel it (" + FuelPerCharge + " gas each)"
                        : Targetable(pos, MaserRange) ? "charged, target in range" : "charged, no target");
            }
            return "";
        }

        public void UseWeapon(string block, string command, Vector3i pos, EntityPlayer player)
        {
            if (command == "fuel")
                FuelMaser(player);
            else if (block == MissileBlock)
                FireMissiles(pos, player);
            else if (block == AirstrikeBlock)
                CallAirstrike(pos, player);
            else if (block == MaserBlock)
                FireMaser(pos, player);
        }

        /// <summary>The launch button: a salvo at him if he is out and in range. It won't stop him.</summary>
        private void FireMissiles(Vector3i pos, EntityPlayer player)
        {
            if (Time.time < missilesReady)
            {
                GameApi.Tooltip(player, "Reloading: " + Mathf.CeilToInt(missilesReady - Time.time) + " s.");
                return;
            }
            if (!Targetable(pos, MissileRange))
            {
                GameApi.Tooltip(player, "No target.");
                return;
            }
            missilesReady = Time.time + MissileReload;
            Missile.Salvo(new Vector3(pos.x + 0.5f, pos.y + 2f, pos.z + 0.5f), MissileSalvo);
            GameApi.Tooltip(player, "Missiles away!");
            Log.Out("[KaijuMod] Missile salvo from " + pos);
        }

        /// <summary>The radio: jets carpet-bomb him, two passes, once per attack.</summary>
        private void CallAirstrike(Vector3i pos, EntityPlayer player)
        {
            var d = KaijuDirector.Instance;
            if (!Targetable(pos, AirstrikeRange))
            {
                GameApi.Tooltip(player, "No target.");
                return;
            }
            if (airstrikeFor == d.Attacking)
            {
                GameApi.Tooltip(player, "The jets are already on their way.");
                return;
            }
            airstrikeFor = d.Attacking;
            KaijuJets.Strike(d.Position.x >= 0f ? 1f : -1f);
            GameApi.Tooltip(player, "Airstrike inbound!");
            Log.Out("[KaijuMod] Airstrike called from " + pos);
        }

        private void FuelMaser(EntityPlayer player)
        {
            if (st.MaserUsed || st.MaserCharge >= 3)
                return;
            if (!GameApi.TakeItem(player, Fuel, FuelPerCharge))
            {
                GameApi.Tooltip(player, "It takes " + FuelPerCharge + " gas.");
                return;
            }
            st.MaserCharge++;
            Save();
            GameApi.Tooltip(player, "Maser charge " + (st.MaserCharge * 100 / 3) + "%.");
        }

        /// <summary>
        /// The maser's one shot: a beam for a few seconds while he staggers, then he turns his breath
        /// on the cannon and destroys it.
        /// </summary>
        private void FireMaser(Vector3i pos, EntityPlayer player)
        {
            if (st.MaserUsed)
            {
                GameApi.Tooltip(player, "Burnt out.");
                return;
            }
            if (st.MaserCharge < 3)
            {
                GameApi.Tooltip(player, "Charge " + (st.MaserCharge * 100 / 3) + "%: fuel it first.");
                return;
            }
            if (!Targetable(pos, MaserRange))
            {
                GameApi.Tooltip(player, "No target.");
                return;
            }
            st.MaserUsed = true;
            Save();
            var dish = new Vector3(pos.x + 0.5f, pos.y + 3f, pos.z + 0.5f);
            KaijuMaser.Fire(dish, () => KaijuDirector.Instance.Retaliate(dish, MaserRange + 300f, () => DestroyMaser(pos)));
            GameApi.Tooltip(player, "Maser firing! Then get clear of it.");
            Log.Out("[KaijuMod] Maser fired from " + pos);
        }

        /// <summary>His breath has reached the cannon: it goes up.</summary>
        private void DestroyMaser(Vector3i pos)
        {
            World world = GameApi.World;
            if (world == null)
                return;
            var at = new Vector3(pos.x + 0.5f, pos.y + 1f, pos.z + 0.5f);
            KaijuEffects.Flash(at, 60f, 6f, 0.8f, new Color(0.6f, 0.8f, 1f));
            KaijuEffects.Burst(at, KaijuDirector.Instance.EffectMaterial("KaijuSpark"), KaijuDirector.Instance.EffectMaterial("KaijuSmoke"));
            KaijuAudio.MissileHit(at);
            if (GameApi.BlockName(world, pos) == MaserBlock)
                GameApi.ClearBlocks(world, new List<Vector3i> { pos });
            Log.Out("[KaijuMod] The maser cannon is destroyed");
        }

        // ---- Oxygen Destroyer: the drop and the finale

        /// <summary>In a gyrocopter with the device: right-click drops it on him.</summary>
        private void TickGyro(EntityPlayer player)
        {
            var gyro = player != null ? player.AttachedToEntity as EntityVehicle : null;
            if (!(gyro is EntityVGyroCopter || gyro is EntityVHelicopter) || GameApi.ItemCount(player, DeviceItem) <= 0)
            {
                gyroHinted = false;
                return;
            }
            if (!gyroHinted)
            {
                gyroHinted = true;
                GameApi.Tooltip(player, "The Oxygen Destroyer is aboard. Fly over Godzilla and right-click to drop it on him.");
            }
            // VERIFIED (V3.3): the game reads input through InControl on Unity's legacy Input (no
            // Input System package); vehicles leave the right mouse button unbound
            // (PlayerActionsVehicle). GameManager.isAnyCursorWindowOpen: a menu is open.
            if (!Input.GetMouseButtonDown(1) || GameManager.Instance.isAnyCursorWindowOpen(null) || KaijuBomb.Falling != null)
                return;
            var d = KaijuDirector.Instance;
            if (!d.Running || d.Dying)
            {
                GameApi.Tooltip(player, "Hold on to it until he comes.");
                return;
            }
            Vector3 p = GameApi.Position(player);
            float dist = (d.Position - new Vector2(p.x, p.z)).magnitude;
            if (dist > DropRange)
            {
                GameApi.Tooltip(player, "Too far: get over him (" + Mathf.RoundToInt(dist) + " m).");
                return;
            }
            if (!GameApi.TakeItem(player, DeviceItem))
                return;
            KaijuBomb.Drop(p + Vector3.down * 4f, gyro.GetVelocityPerSecond());
            GameApi.Tooltip(player, "Oxygen Destroyer away!");
            Log.Out("[KaijuMod] Oxygen Destroyer dropped at " + p + ", " + Mathf.RoundToInt(dist) + " m from him");
        }

        /// <summary>It fell into him: he dies, the run is won.</summary>
        public void OnBombHit(Vector3 pos)
        {
            KaijuDirector.Instance.Die(pos);
            st.Outcome = "won";
            st.FinaleActive = false;
            st.Complete = true;
            Save();
            TellPlayer("Godzilla is dead. You won the run.");
        }

        /// <summary>It missed him: it only goes off against him, so you get it back to try again.</summary>
        public void OnBombMissed(Vector3 pos)
        {
            KaijuEffects.Burst(pos, KaijuDirector.Instance.EffectMaterial("KaijuSpark"), KaijuDirector.Instance.EffectMaterial("KaijuSmoke"));
            var player = boundWorld != null ? GameApi.LocalPlayer(boundWorld) : null;
            if (player != null && GameApi.GiveItem(player, DeviceItem))
                TellPlayer("Missed him. It only goes off against him, and it's back in your pack: come around again.");
            else
                TellPlayer("Missed him. It only goes off against him.");
        }

        private Settlement FinalCity()
        {
            return data != null ? data.EndCity(data.Strips - 1) : null;
        }

        /// <summary>The last city's attack is over and the Oxygen Destroyer never hit him: the run is lost.</summary>
        private void TickFinale()
        {
            if (!st.FinaleActive || KaijuDirector.Instance.Running)
                return;
            st.FinaleActive = false;
            if (st.Outcome != "won")
            {
                st.Outcome = "lost";
                Settlement city = FinalCity();
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
                s += "\nStrip " + st.Strip + " (" + end?.Biome + "), next attack: " + end?.Name
                    + (st.AttackAt > 0 ? " in " + ((st.AttackAt - System.Math.Min(st.AttackAt, now)) * 60 / 1000) + " in-game minutes."
                        : ", " + AttackDelayMinutes + " in-game minutes after the blood moon horde ends.");
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
            s += " Oxygen Destroyer: " + have + "/" + Parts + " parts carried, device " + (me != null && GameApi.ItemCount(me, DeviceItem) > 0 ? "carried" : "not carried")
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
                e.SetAttribute("attackAt", st.AttackAt.ToString(CultureInfo.InvariantCulture));
                e.SetAttribute("radiation", st.RadiationOn ? "1" : "0");
                e.SetAttribute("complete", st.Complete ? "1" : "0");
                e.SetAttribute("maserUsed", st.MaserUsed ? "1" : "0");
                e.SetAttribute("maserCharge", st.MaserCharge.ToString(CultureInfo.InvariantCulture));
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
                ulong attackAt;
                st.AttackAt = ulong.TryParse(e.GetAttribute("attackAt"), NumberStyles.Integer, CultureInfo.InvariantCulture, out attackAt) ? attackAt : 0;
                st.RadiationOn = e.GetAttribute("radiation") != "0";
                st.Complete = e.GetAttribute("complete") == "1";
                st.MaserUsed = e.GetAttribute("maserUsed") == "1";
                int charge;
                st.MaserCharge = int.TryParse(e.GetAttribute("maserCharge"), NumberStyles.Integer, CultureInfo.InvariantCulture, out charge) ? charge : 0;
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

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

        /// <summary>Real seconds after the first spawn before he attacks the start city.</summary>
        public static float StartAttackDelay = 90f;
        /// <summary>In-game hours after an end-city attack before the next strip's front starts.</summary>
        public static int GraceHours = 4;
        /// <summary>The front stops this far short of the end city's inland edge, metres.</summary>
        public static float FrontStopMargin = 200f;
        /// <summary>Radiation damage per second to players behind the front.</summary>
        public static int RadiationDamage = 4;
        /// <summary>Warn players who are this close ahead of the front, metres.</summary>
        public static float WarnDistance = 150f;
        /// <summary>The Oxygen Destroyer kills him when he comes this close to it, metres.</summary>
        public static float DestroyerRadius = 40f;
        /// <summary>Oxygen Destroyer parts: one crate in the end city of each of these strips.</summary>
        public const int Parts = 4;
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
            public bool DeviceArmed;
            public Vector3i DevicePos;
            public bool FinaleActive;
            public string Outcome = "";     // "", "won" or "lost"
        }

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
            boundWorld = world;
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

        /// <summary>True if a world position is irradiated now; frontX is the front it is measured against.</summary>
        public bool IsIrradiated(Vector3 p, ulong now, out float frontX, out int strip)
        {
            frontX = 0f;
            strip = -1;
            if (data == null || !st.RadiationOn || st.Strip < 0)
                return false;
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
                        string way = strip >= 0 ? (data.Direction(strip) > 0 ? "east" : "west") : "north";
                        GameApi.Tooltip(player, "RADIATION! Get " + way + ", ahead of the front.");
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

        public void Reset()
        {
            ClearMarkers();
            st = new State();
            startTimer = 0f;
            Save();
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
                    if (FindCrateSpot(world, city, out spot) && GameApi.PlaceBlock(world, spot, CrateBlock(i)))
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
                bool crateThere = GameApi.BlockName(world, st.CratePos[i]) == CrateBlock(i) || !GameApi.IsChunkLoaded(world, st.CratePos[i].x, st.CratePos[i].z);
                // Light: while nearby and the part is still out there.
                if (near && !hasPart && crateThere && crateLights[i] == null)
                    crateLights[i] = Beacon.Create(crate + Vector3.up, new Color(0.5f, 0.85f, 1f), 14f, 4f);
                else if ((!near || hasPart || !crateThere) && crateLights[i] != null)
                {
                    Object.Destroy(crateLights[i].gameObject);
                    crateLights[i] = null;
                }
                // Compass marker: once inside the city, until you carry the part.
                bool inCity = InCity(city, p, 40f);
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

        private bool FindCrateSpot(World world, Settlement city, out Vector3i spot)
        {
            spot = default(Vector3i);
            if (!GameApi.IsChunkLoaded(world, Mathf.RoundToInt(city.X), Mathf.RoundToInt(city.Z)))
                return false;
            // The POI nearest the city centre (not a trader), else the centre itself.
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
                s += "\n  " + (i + 1) + ". " + (city != null ? city.Name : "?") + ": "
                    + (st.CratePlaced[i] ? "crate at " + st.CratePos[i] : "crate not placed yet (placed when you get near)")
                    + (has ? ", YOU HAVE IT" : "");
            }
            s += "\nDevice: " + (st.DeviceArmed ? "armed at " + st.DevicePos + (InAshmouth(st.DevicePos) ? " (inside Ashmouth)" : " (NOT inside Ashmouth)") : "not armed")
                + ". Finale: " + (st.Outcome == "won" ? "won" : st.Outcome == "lost" ? "lost" : st.FinaleActive ? "in progress" : "after the last blood moon") + ".";
            return s;
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
                : "Oxygen Destroyer armed, but it is not inside " + (FinalCity() != null ? FinalCity().Name : "the last city") + ". He attacks there.");
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

        private bool InAshmouth(Vector3i pos)
        {
            Settlement city = FinalCity();
            return city != null && InCity(city, new Vector3(pos.x, pos.y, pos.z), 0f);
        }

        /// <summary>During the last city's attack: armed device inside the city and he is within range: he dies.</summary>
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
                for (int i = 0; i < Parts; i++)
                {
                    var c = doc.CreateElement("crate");
                    c.SetAttribute("part", (i + 1).ToString(CultureInfo.InvariantCulture));
                    c.SetAttribute("placed", st.CratePlaced[i] ? "1" : "0");
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
                foreach (XmlElement c in e.SelectNodes("crate"))
                {
                    int i;
                    if (!int.TryParse(c.GetAttribute("part"), NumberStyles.Integer, CultureInfo.InvariantCulture, out i) || i < 1 || i > Parts)
                        continue;
                    st.CratePlaced[i - 1] = c.GetAttribute("placed") == "1";
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

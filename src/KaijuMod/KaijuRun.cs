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
        }

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
        }

        private void Bind(World world)
        {
            boundWorld = world;
            startTimer = radTimer = 0f;
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
            if (which == "start")
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
            st = new State();
            startTimer = 0f;
            Save();
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
                doc.Save(path);
            }
            catch (System.Exception ex)
            {
                Log.Warning("[KaijuMod] Could not save " + path + ": " + ex.Message);
            }
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
            }
            catch (System.Exception ex)
            {
                Log.Warning("[KaijuMod] Could not read " + path + ", starting a new run: " + ex.Message);
                st = new State();
            }
        }
    }
}

using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

namespace KaijuMod
{
    /// <summary>
    /// A small profiler for the mod's own work, per subsystem: `kaiju perf` prints average and worst
    /// milliseconds per frame over the last seconds, frames over SpikeMs are logged with the culprit,
    /// and `kaiju perf off &lt;subsystem|all&gt;` switches a subsystem off live to compare (on again with
    /// `kaiju perf on ...`). The clouds are GPU work (smoke particles), so they are switched, not timed.
    /// </summary>
    public static class KaijuPerf
    {
        public static readonly string[] Subsystems = { "director", "run", "radiation", "fallout", "horizon", "clouds", "parts", "zombies", "skins" };
        public static float SpikeMs = 5f;

        private class Stat
        {
            public double Frame, Sum, Max;
            public int Frames;
        }

        private static readonly Dictionary<string, Stat> stats = new Dictionary<string, Stat>();
        private static readonly HashSet<string> off = new HashSet<string>();
        private static readonly Stopwatch watch = new Stopwatch();
        private static float windowStart;
        private static long gcStart;
        private static float nextSpikeLog;

        public static bool On(string subsystem)
        {
            return !off.Contains(subsystem);
        }

        /// <summary>Starts timing; pass the result to End.</summary>
        public static long Begin()
        {
            if (!watch.IsRunning)
                watch.Start();
            return watch.ElapsedTicks;
        }

        public static void End(string subsystem, long start)
        {
            Stat s;
            if (!stats.TryGetValue(subsystem, out s))
                stats[subsystem] = s = new Stat();
            s.Frame += (watch.ElapsedTicks - start) * 1000.0 / Stopwatch.Frequency;
        }

        /// <summary>Called once a frame after the mod's work: folds this frame in and logs a spike.</summary>
        public static void EndFrame()
        {
            double total = 0;
            string worst = null;
            double worstMs = 0;
            foreach (var kv in stats)
            {
                var s = kv.Value;
                s.Sum += s.Frame;
                s.Frames++;
                if (s.Frame > s.Max)
                    s.Max = s.Frame;
                total += s.Frame;
                if (s.Frame > worstMs)
                {
                    worstMs = s.Frame;
                    worst = kv.Key;
                }
                s.Frame = 0;
            }
            if (total > SpikeMs && Time.realtimeSinceStartup >= nextSpikeLog)
            {
                nextSpikeLog = Time.realtimeSinceStartup + 1f; // at most one a second
                Log.Out("[KaijuMod] perf: frame spike " + total.ToString("0.0") + " ms, mostly " + worst + " (" + worstMs.ToString("0.0") + " ms)");
            }
        }

        public static string Report()
        {
            float seconds = Mathf.Max(0.001f, Time.realtimeSinceStartup - windowStart);
            var sb = new System.Text.StringBuilder();
            sb.Append("Kaiju mod cost over the last " + seconds.ToString("0") + " s (ms per frame, average / worst):");
            foreach (string name in Subsystems)
            {
                Stat s;
                stats.TryGetValue(name, out s);
                sb.Append("\n  " + name.PadRight(10) + (off.Contains(name) ? " OFF" : s == null || s.Frames == 0 ? " -" :
                    " " + (s.Sum / s.Frames).ToString("0.00") + " / " + s.Max.ToString("0.00")));
            }
            long gc = System.GC.GetTotalMemory(false) - gcStart;
            sb.Append("\n  managed memory change: " + (gc / 1024) + " KB (whole game, not just the mod)");
            sb.Append("\nFrame spikes over " + SpikeMs + " ms are logged. Use kaiju perf off <name|all> to compare.");
            return sb.ToString();
        }

        public static void Reset()
        {
            stats.Clear();
            windowStart = Time.realtimeSinceStartup;
            gcStart = System.GC.GetTotalMemory(false);
        }

        public static string Switch(string subsystem, bool on)
        {
            var names = subsystem == "all" ? Subsystems : new[] { subsystem };
            foreach (string n in names)
            {
                if (System.Array.IndexOf(Subsystems, n) < 0)
                    return "Unknown subsystem " + subsystem + ". One of: " + string.Join(", ", Subsystems) + ", all.";
                if (on)
                    off.Remove(n);
                else
                    off.Add(n);
            }
            if (subsystem == "all" || subsystem == "clouds")
                KaijuRun.Instance.ShowClouds(On("clouds"));
            return "Kaiju " + subsystem + " " + (on ? "on" : "off") + ".";
        }
    }
}

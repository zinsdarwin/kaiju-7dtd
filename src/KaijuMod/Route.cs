using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace KaijuMod
{
    /// <summary>
    /// Godzilla's route: an ordered list of (x, z) waypoints in world coordinates. Height is not
    /// stored; he follows the terrain. On the snake map this will be the main road.
    /// </summary>
    public static class Route
    {
        /// <summary>
        /// Milestone 1 route between two towns on a vanilla map.
        ///
        /// PLACEHOLDER: these numbers are not real town coordinates. Record the real ones in game
        /// with `kaiju addpoint` while walking the road from one town to the next, then
        /// `kaiju saveroute` (writes route.txt, which overrides this list) or paste the C# lines
        /// that `kaiju route` prints in here.
        /// </summary>
        public static readonly Vector2[] Default =
        {
            new Vector2(-600f, -400f),
            new Vector2(-450f, -400f),
            new Vector2(-300f, -350f),
            new Vector2(-150f, -350f),
            new Vector2(0f, -300f),
        };

        public const string FileName = "route.txt";

        public static string FilePath
        {
            get
            {
                // The DLL lives in the mod folder, so route.txt sits next to it.
                string dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                return Path.Combine(dir ?? ".", FileName);
            }
        }

        /// <summary>route.txt if it exists and has at least two points, otherwise the default list.</summary>
        public static List<Vector2> Load(out string source)
        {
            var fromFile = TryLoadFile();
            if (fromFile != null && fromFile.Count >= 2)
            {
                source = FilePath;
                return fromFile;
            }
            source = "built-in default (placeholder coordinates)";
            return new List<Vector2>(Default);
        }

        /// <summary>One "x z" pair per line; blank lines and lines starting with # are ignored.</summary>
        public static List<Vector2> TryLoadFile()
        {
            string path = FilePath;
            if (!File.Exists(path))
                return null;
            var points = new List<Vector2>();
            foreach (string raw in File.ReadAllLines(path))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#"))
                    continue;
                string[] parts = line.Split(new[] { ' ', '\t', ',' }, StringSplitOptions.RemoveEmptyEntries);
                float x, z;
                if (parts.Length >= 2
                    && float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out x)
                    && float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out z))
                {
                    points.Add(new Vector2(x, z));
                }
                else
                {
                    Log.Warning("[KaijuMod] Ignoring bad line in " + path + ": " + raw);
                }
            }
            return points;
        }

        public static void Save(List<Vector2> points)
        {
            var lines = new List<string> { "# Kaiju route: one \"x z\" waypoint per line, world coordinates." };
            foreach (var p in points)
                lines.Add(Format(p.x) + " " + Format(p.y));
            File.WriteAllLines(FilePath, lines.ToArray());
        }

        public static string ToCSharp(Vector2 p)
        {
            return "new Vector2(" + Format(p.x) + "f, " + Format(p.y) + "f),";
        }

        private static string Format(float v)
        {
            return Mathf.Round(v).ToString(CultureInfo.InvariantCulture);
        }
    }
}

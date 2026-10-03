using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Xml;
using UnityEngine;

namespace KaijuMod
{
    /// <summary>A city or town from kaiju.xml. Coordinates are world x/z; Y is its ground height.</summary>
    public class Settlement
    {
        public string Name, Kind, Role, Biome;
        public int Strip;
        public float X, Y, Z, HalfWidth;

        public bool IsCity { get { return Kind == "city"; } }
    }

    /// <summary>
    /// The map data tools/snakemap writes next to a Kaiju world (kaiju.xml): its settlements and
    /// Godzilla's road route. A world without the file is not a Kaiju world and The Run stays off.
    /// </summary>
    public class KaijuWorldData
    {
        public const string FileName = "kaiju.xml";

        public string World;
        public int Size = 6144;
        public int SeaLevel = 30;
        public int Strips = 1;
        public readonly List<Settlement> Settlements = new List<Settlement>();
        public readonly List<Vector2> Route = new List<Vector2>();

        public float StripHeight { get { return Size / (float)Strips; } }

        /// <summary>Strip index (0 = south) of a world z, clamped to the map.</summary>
        public int StripOf(float z)
        {
            return Mathf.Clamp(Mathf.FloorToInt((z + Size / 2f) / StripHeight), 0, Strips - 1);
        }

        public Settlement StartCity
        {
            get { return Settlements.Find(s => s.Role == "start"); }
        }

        public Settlement EndCity(int strip)
        {
            return Settlements.Find(s => s.Role == "end" && s.Strip == strip);
        }

        /// <summary>A settlement by name, case-insensitive, or null.</summary>
        public Settlement Find(string name)
        {
            return Settlements.Find(s => string.Equals(s.Name, name, System.StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Direction the chase runs along a strip: +1 east, -1 west (toward its end city).</summary>
        public int Direction(int strip)
        {
            var end = EndCity(strip);
            return end != null && end.X < 0 ? -1 : 1;
        }

        /// <summary>Loads kaiju.xml from a world folder, or returns null if there is none.</summary>
        public static KaijuWorldData Load(string worldFolder)
        {
            if (string.IsNullOrEmpty(worldFolder))
                return null;
            string path = Path.Combine(worldFolder, FileName);
            if (!File.Exists(path))
                return null;
            var doc = new XmlDocument();
            doc.Load(path);
            var root = doc.DocumentElement;
            var data = new KaijuWorldData
            {
                World = root.GetAttribute("world"),
                Size = Int(root, "size", 6144),
                SeaLevel = Int(root, "sealevel", 30),
            };
            foreach (XmlElement e in root.SelectNodes("settlements/settlement"))
            {
                data.Settlements.Add(new Settlement
                {
                    Name = e.GetAttribute("name"),
                    Kind = e.GetAttribute("kind"),
                    Role = e.GetAttribute("role"),
                    Biome = e.GetAttribute("biome"),
                    Strip = Int(e, "strip", 0),
                    X = Float(e, "x"),
                    Y = Float(e, "y"),
                    Z = Float(e, "z"),
                    HalfWidth = Float(e, "halfwidth"),
                });
            }
            foreach (XmlElement e in root.SelectNodes("route/p"))
                data.Route.Add(new Vector2(Float(e, "x"), Float(e, "z")));
            foreach (var s in data.Settlements)
                data.Strips = Mathf.Max(data.Strips, s.Strip + 1);
            return data;
        }

        private static int Int(XmlElement e, string name, int fallback)
        {
            int v;
            return int.TryParse(e.GetAttribute(name), NumberStyles.Integer, CultureInfo.InvariantCulture, out v) ? v : fallback;
        }

        private static float Float(XmlElement e, string name)
        {
            float v;
            return float.TryParse(e.GetAttribute(name), NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : 0f;
        }
    }
}

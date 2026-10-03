using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace KaijuMod
{
    /// <summary>
    /// F1 console command for testing the kaiju. The game finds console commands by scanning
    /// mod assemblies for ConsoleCmdAbstract subclasses.
    ///
    /// VERIFIED (V3.3): override the lowercase getCommands, getDescription and getHelp (protected
    /// originally, public in the shipped publicized assembly); the capitalized ones cache them.
    /// </summary>
    public class ConsoleCmdKaiju : ConsoleCmdAbstract
    {
        private static readonly List<Vector2> recorded = new List<Vector2>();

        public override string[] getCommands()
        {
            return new[] { "kaiju" };
        }

        public override string getDescription()
        {
            return "Kaiju world event: The Run, city attacks, radiation, and test commands.";
        }

        public override string getHelp()
        {
            return "Usage:\n"
                + "  kaiju run [reset]    The Run status: day, strip, radiation front, next attack (reset starts it over)\n"
                + "  kaiju attack <city|start|end|finale>  send him at a city now (a name from kaiju.xml, the start city, this strip's end city, or the final attack with the Oxygen Destroyer check)\n"
                + "  kaiju parts          Oxygen Destroyer parts: where each crate is, which you carry, device and finale state\n"
                + "  kaiju give <1-4|all> put Oxygen Destroyer parts in your backpack (testing)\n"
                + "  kaiju radiation <on|off>  turn the radiation chase on or off\n"
                + "  kaiju start          walk this session's recorded waypoints, else route.txt, else the built-in list\n"
                + "  kaiju test [dist]    walk a straight line from dist m in front of you, through you (default 120)\n"
                + "  kaiju stop           stop and remove him\n"
                + "  kaiju breath [me]    atomic breath at what you're looking at (or at you); he must be out\n"
                + "  kaiju status         position, segment, blocks cleared\n"
                + "  kaiju speed <m/s>    set walking speed\n"
                + "  kaiju radius <m>     set footprint radius\n"
                + "  kaiju stride <h>     ground per walk loop in body heights; higher slows his legs (default 0.6)\n"
                + "  kaiju height <m>     set height (box size and how high blocks are cleared)\n"
                + "  kaiju addpoint       record your position as the next waypoint\n"
                + "  kaiju route          list recorded waypoints (as C# for Route.cs)\n"
                + "  kaiju saveroute      write recorded waypoints to route.txt\n"
                + "  kaiju clearroute     forget recorded waypoints";
        }

        public override void Execute(List<string> _params, CommandSenderInfo _senderInfo)
        {
            string sub = _params.Count > 0 ? _params[0].ToLowerInvariant() : "status";
            var director = KaijuDirector.Instance;
            switch (sub)
            {
                case "start":
                {
                    // Waypoints recorded this session win, so record-then-start needs no save.
                    string source;
                    List<Vector2> points;
                    if (recorded.Count >= 2)
                    {
                        points = new List<Vector2>(recorded);
                        source = "the waypoints recorded this session";
                    }
                    else
                    {
                        points = Route.Load(out source);
                    }
                    string error;
                    if (director.Start(points, out error))
                        GameApi.ConsoleOut("Kaiju walking " + points.Count + " waypoints from " + source);
                    else
                        GameApi.ConsoleOut("Kaiju not started: " + error);
                    break;
                }
                case "test":
                    StartTest(_params);
                    break;
                case "breath":
                    Breathe(_params);
                    break;
                case "run":
                    if (_params.Count > 1 && _params[1].ToLowerInvariant() == "reset")
                    {
                        KaijuRun.Instance.Reset();
                        GameApi.ConsoleOut("The Run reset: the start city attack is pending again.");
                    }
                    GameApi.ConsoleOut(KaijuRun.Instance.Status());
                    break;
                case "attack":
                {
                    if (!KaijuRun.Instance.Active)
                    {
                        GameApi.ConsoleOut("No attack: this world has no kaiju.xml (load the Kaiju Snake world).");
                        break;
                    }
                    string which = _params.Count > 1 ? string.Join(" ", _params.GetRange(1, _params.Count - 1)) : "end";
                    string key = which.ToLowerInvariant();
                    if (KaijuRun.Instance.Attack(key == "start" || key == "end" || key == "finale" ? key : which))
                        GameApi.ConsoleOut("Godzilla is attacking " + (director.Attacking ?? which) + ".");
                    else
                        GameApi.ConsoleOut("No such city: " + which + ". Use a settlement name from kaiju.xml, start, or end.");
                    break;
                }
                case "parts":
                    GameApi.ConsoleOut(KaijuRun.Instance.PartsStatus());
                    break;
                case "give":
                {
                    string arg = _params.Count > 1 ? _params[1].ToLowerInvariant() : "";
                    int n;
                    if (arg == "all")
                    {
                        for (int i = 0; i < KaijuRun.Parts; i++)
                            KaijuRun.Instance.GivePart(i);
                        GameApi.ConsoleOut("Gave all " + KaijuRun.Parts + " Oxygen Destroyer parts.");
                    }
                    else if (int.TryParse(arg, out n) && n >= 1 && n <= KaijuRun.Parts)
                        GameApi.ConsoleOut(KaijuRun.Instance.GivePart(n - 1) ? "Gave Oxygen Destroyer part " + n + "." : "Could not give it (backpack full?).");
                    else
                        GameApi.ConsoleOut("Usage: kaiju give <1-" + KaijuRun.Parts + "|all>");
                    break;
                }
                case "radiation":
                {
                    if (_params.Count > 1 && (_params[1] == "on" || _params[1] == "off"))
                        KaijuRun.Instance.SetRadiation(_params[1] == "on");
                    GameApi.ConsoleOut(KaijuRun.Instance.Status());
                    break;
                }
                case "stop":
                    director.Stop();
                    GameApi.ConsoleOut("Kaiju stopped.");
                    break;
                case "status":
                    PrintStatus();
                    break;
                case "speed":
                {
                    float v;
                    if (_params.Count > 1 && TryFloat(_params[1], out v) && v > 0f)
                        director.Speed = v;
                    GameApi.ConsoleOut("Kaiju speed " + director.Speed + " m/s");
                    break;
                }
                case "radius":
                {
                    float v;
                    if (_params.Count > 1 && TryFloat(_params[1], out v) && v >= 1f)
                    {
                        director.Footprint.Radius = v;
                    }
                    GameApi.ConsoleOut("Kaiju footprint radius " + director.Footprint.Radius + " m");
                    break;
                }
                case "height":
                {
                    float v;
                    if (_params.Count > 1 && TryFloat(_params[1], out v) && v >= 5f && v <= 250f)
                    {
                        director.Footprint.Height = Mathf.RoundToInt(v);
                    }
                    GameApi.ConsoleOut("Kaiju height " + director.Footprint.Height + " m (5 to 250; blocks are cleared up to the world's build limit)");
                    break;
                }
                case "stride":
                {
                    float v;
                    if (_params.Count > 1 && TryFloat(_params[1], out v) && v >= 0.05f && v <= 10f)
                        KaijuVisual.StrideHeights = v;
                    GameApi.ConsoleOut("Kaiju stride " + KaijuVisual.StrideHeights + " body heights per walk loop (higher = slower legs)");
                    break;
                }
                case "addpoint":
                {
                    var player = LocalPlayer();
                    if (player == null)
                        break;
                    Vector3 p = GameApi.Position(player);
                    recorded.Add(new Vector2(p.x, p.z));
                    GameApi.ConsoleOut("Waypoint " + recorded.Count + ": " + Route.ToCSharp(recorded[recorded.Count - 1]));
                    break;
                }
                case "route":
                    if (recorded.Count == 0)
                        GameApi.ConsoleOut("No waypoints recorded. Use kaiju addpoint.");
                    foreach (var p in recorded)
                        GameApi.ConsoleOut(Route.ToCSharp(p));
                    break;
                case "saveroute":
                    if (recorded.Count < 2)
                    {
                        GameApi.ConsoleOut("Record at least two waypoints first.");
                        break;
                    }
                    Route.Save(recorded);
                    GameApi.ConsoleOut("Saved " + recorded.Count + " waypoints to " + Route.FilePath);
                    break;
                case "clearroute":
                    recorded.Clear();
                    GameApi.ConsoleOut("Recorded waypoints cleared.");
                    break;
                default:
                    GameApi.ConsoleOut(getHelp());
                    break;
            }
        }

        /// <summary>
        /// Straight line toward the player, starting dist metres ahead of where they face and
        /// ending dist metres behind them: the quickest way to see him coming and get crushed.
        /// </summary>
        private static void StartTest(List<string> args)
        {
            var player = LocalPlayer();
            if (player == null)
                return;
            float dist = 120f;
            float parsed;
            if (args.Count > 1 && TryFloat(args[1], out parsed) && parsed > 0f)
                dist = parsed;
            Vector3 pos = GameApi.Position(player);
            float yaw = GameApi.YawDegrees(player) * Mathf.Deg2Rad;
            var forward = new Vector2(Mathf.Sin(yaw), Mathf.Cos(yaw));
            var here = new Vector2(pos.x, pos.z);
            var points = new List<Vector2> { here + forward * dist, here, here - forward * dist };
            string error;
            if (KaijuDirector.Instance.Start(points, out error))
                GameApi.ConsoleOut("Kaiju coming at you from " + dist + " m ahead at " + KaijuDirector.Instance.Speed + " m/s");
            else
                GameApi.ConsoleOut("Kaiju not started: " + error);
        }

        /// <summary>Fires at the point under the crosshair (up to 1 km), or at the player with "me".</summary>
        private static void Breathe(List<string> args)
        {
            var player = LocalPlayer();
            if (player == null)
                return;
            Vector3 target;
            if (args.Count > 1 && args[1].ToLowerInvariant() == "me")
            {
                target = GameApi.Position(player) + new Vector3(0f, 1f, 0f);
            }
            else
            {
                Ray look = GameApi.LookRay(player);
                Vector3? hit = GameApi.Raycast(look.origin, look.direction, 1000f);
                target = hit ?? look.origin + look.direction * 400f;
            }
            string error;
            if (KaijuDirector.Instance.Breathe(target, out error))
                GameApi.ConsoleOut("Atomic breath charging at (" + Mathf.Round(target.x) + ", " + Mathf.Round(target.y) + ", " + Mathf.Round(target.z) + ")");
            else
                GameApi.ConsoleOut("No breath: " + error);
        }

        private static void PrintStatus()
        {
            var d = KaijuDirector.Instance;
            if (!d.Running)
            {
                GameApi.ConsoleOut("Kaiju idle. Speed " + d.Speed + " m/s, radius " + d.Footprint.Radius + " m.");
                return;
            }
            GameApi.ConsoleOut("Kaiju at (" + Mathf.Round(d.Position.x) + ", " + Mathf.Round(d.BaseY) + ", " + Mathf.Round(d.Position.y)
                + "), heading to waypoint " + (d.Segment + 2) + "/" + d.WaypointCount
                + ", " + d.Footprint.TotalCleared + " blocks cleared, "
                + d.Footprint.PendingBlocks + " blocks and " + d.Footprint.PendingColumns + " columns queued");
        }

        private static EntityPlayer LocalPlayer()
        {
            World world = GameApi.World;
            EntityPlayer player = world == null ? null : GameApi.LocalPlayer(world);
            if (player == null)
                GameApi.ConsoleOut("No local player.");
            return player;
        }

        private static bool TryFloat(string s, out float v)
        {
            return float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v);
        }
    }
}

using System;
using System.IO;

namespace Shipwright
{
    /// <summary>
    /// Command line entry point. Compile Pal invokes this as a compile step, the same way it shells
    /// out to vbsp and bspzip, but it is deliberately usable on its own: the inspection commands are
    /// the ones to reach for when a publish did something unexpected, and neither of them touches
    /// the Workshop.
    /// </summary>
    public static class Program
    {
        public static int Main(string[] args)
        {
            if (args.Length == 0)
            {
                Usage();
                return 1;
            }

            try
            {
                switch (args[0].ToLowerInvariant())
                {
                    case "publish":
                        return Publisher.Run(Options.Parse(args, 1));

                    case "inspect":
                        return Inspect(args);

                    case "check-icon":
                        return CheckIcon(args);

                    case "status":
                        return Status(args);

                    case "list":
                        return ListItems(args);

                    case "-h":
                    case "--help":
                    case "help":
                        Usage();
                        return 0;

                    default:
                        Log.Error($"unknown command: {Sanitize.PlainText(args[0], 32)}");
                        Usage();
                        return 1;
                }
            }
            catch (ArgumentException e)
            {
                Log.Error(e.Message);
                return 1;
            }
            catch (Exception e)
            {
                Log.Error(e.Message);
                return 1;
            }
        }

        /// <summary>
        /// Says what a publish would ship and what it is bound to, and touches nothing.
        ///
        /// No network, no Steam, no gmad: this reads the BSP, the lump file beside it and the state
        /// file, and prints them. It is the command to run when the question is "what would this
        /// upload" and the answer needs to cost nothing to get.
        /// </summary>
        private static int Inspect(string[] args)
        {
            var options = Options.Parse(args, 1);

            if (!File.Exists(options.BspPath))
            {
                Log.Error($"no compiled map at {options.BspPath}");
                return 1;
            }

            var facts = BspReader.Read(options.BspPath);

            if (!facts.Readable)
            {
                Log.Error($"{Path.GetFileName(options.BspPath)}: {facts.Message}");
                return 1;
            }

            Log.Bsp($"{Path.GetFileName(options.BspPath)}  version {facts.Version}, revision {facts.MapRevision}, " +
                    $"{new FileInfo(options.BspPath).Length:N0} bytes");
            Log.Bsp($"entity lump: {facts.EntityLumpLength:N0} bytes, state {facts.LumpState}");

            var lump = LumpFiles.Inspect(options.BspPath);
            Log.Out($"lump file: {lump.State}" +
                    (lump.Path != null ? $" ({Path.GetFileName(lump.Path)}, revision {lump.LumpRevision})" : ""));

            var state = WorkshopState.Load(options.StatePath!);
            Log.Out($"state file: {options.StatePath}");
            Log.Out(state.WorkshopId is null
                ? "  no Workshop item bound to this map"
                : $"  item {state.WorkshopId}, last published {state.LastPublished?.ToLocalTime():yyyy-MM-dd HH:mm}, " +
                  $"map revision {state.BspRevision}, entities stripped: {state.EntitiesStripped}");

            return 0;
        }

        /// <summary>
        /// Prints what a queue row should say about this map, as one line of JSON.
        ///
        /// Compile Pal runs this for every queued map whenever the queue or the preset changes, so
        /// it touches nothing but the state file beside the map: no network, no Steam, no gmad. The
        /// answer has to be instant and the same every time it is asked.
        ///
        /// Nothing else is printed. The host reads stdout as the result, so a warning about a
        /// malformed state file would arrive where a JSON object was expected.
        /// </summary>
        private static int Status(string[] args)
        {
            var options = Options.Parse(args, 1);

            Log.Sink = _ => { };        // swallow anything the loaders want to say

            string? stepArgs = Environment.GetEnvironmentVariable(MapStatusReporter.StepArgsVariable);

            bool stepEnabled = !string.Equals(
                Environment.GetEnvironmentVariable(MapStatusReporter.StepEnabledVariable), "false",
                StringComparison.OrdinalIgnoreCase);

            var status = MapStatusReporter.Describe(options.StatePath!, stepArgs, stepEnabled);

            Log.Sink = null;
            Console.Out.WriteLine(status.ToJson());

            return 0;
        }

        /// <summary>
        /// Lists what the signed-in account has published, the way the settings window sees it.
        ///
        /// Here as well as in the window because this is the half that talks to Steam, and when it
        /// comes back empty the question is always whether the account has no items or whether
        /// something between here and Steam is wrong. From a terminal that is one command; through a
        /// window it is a bug report.
        /// </summary>
        private static int ListItems(string[] args)
        {
            string binFolder = "";

            for (int i = 1; i < args.Length - 1; i++)
                if (string.Equals(args[i], "-bin", StringComparison.OrdinalIgnoreCase))
                    binFolder = args[i + 1];

            var steam = SteamState.Check();

            if (!steam.CanPublish)
            {
                Log.Error(steam.Message);
                return 1;
            }

            if (GmodTools.Find("gmpublish.exe", binFolder) is not { } gmpublish)
            {
                Log.Error($"gmpublish.exe was not found near {binFolder}. Pass -bin <Garry's Mod bin folder>.");
                return 1;
            }

            var (result, items) = GmodTools.List(gmpublish);

            if (!result.Ok)
            {
                Log.Error($"gmpublish list failed with exit code {result.ExitCode}:");
                foreach (string line in result.Output.Split('\n'))
                    if (line.Trim().Length > 0)
                        Log.FromChild("gmpub", line);
                return 1;
            }

            if (items.Count == 0)
            {
                Log.Warn("gmpublish listed no items. Its output was:");
                foreach (string line in result.Output.Split('\n'))
                    if (line.Trim().Length > 0)
                        Log.FromChild("gmpub", line);
                return 0;
            }

            foreach (var item in items)
                Log.Out($"{item.Id,-12} {item.Title}");

            Log.Check($"{items.Count} item(s).");
            return 0;
        }

        private static int CheckIcon(string[] args)
        {
            if (args.Length < 2)
            {
                Log.Error("check-icon needs the path to a .jpg");
                return 1;
            }

            var verdict = IconCheck.Inspect(args[1]);

            if (verdict.Acceptable)
            {
                Log.Check($"{Path.GetFileName(args[1])}: {verdict.Message}");
                return 0;
            }

            Log.Error($"{Path.GetFileName(args[1])} {verdict.Message}");
            return 1;
        }

        private static void Usage()
        {
            Log.Line("shipwright - publishes a compiled Source map to the Garry's Mod Workshop.");
            Log.Line();
            Log.Line("  shipwright publish <map.bsp> [options]");
            Log.Line("  shipwright inspect <map.bsp> [-vmf <path>] [-state <path>]");
            Log.Line("  shipwright status <map.bsp> [-vmf <path>]          one line of JSON, for a queue row");
            Log.Line("  shipwright list -bin <Garry's Mod bin folder>   what this account has published");
            Log.Line("  shipwright check-icon <icon.jpg>");
            Log.Line();
            Log.Line("Publishing is off by default. Without -publish the run builds the addon, says exactly");
            Log.Line("what it would upload and to which item, and stops.");
            Log.Line();
            Log.Line("  -publish              actually upload");
            Log.Line("  -allowcreate          create a new Workshop item when none is bound to this map");
            Log.Line("  -id <n>               publish to this item, overriding the state file");
            Log.Line("  -icon <path>          512x512 baseline JPEG, required to create a new item");
            Log.Line("  -includelump          ship the entity lump too (off: the point of the tool)");
            Log.Line("  -includenav           ship the nav mesh beside the map");
            Log.Line("  -includeain           ship the AI node graph beside the map");
            Log.Line("  -title <text>         title for a new item; defaults to the map's name");
            Log.Line("  -tags <a,b>           up to two of: fun roleplay scenic movie realism cartoon water comic build");
            Log.Line("  -changes <text>       change note for an update");
            Log.Line("  -changesfile <path>   change note read from a file");
            Log.Line("  -force                publish even if nothing changed or it is too soon");
            Log.Line("  -mininterval <mins>   minimum minutes between publishes of one item (default 5)");
            Log.Line("  -offline              skip looking the item up before overwriting it");
            Log.Line("  -bin <folder>         where gmad.exe and gmpublish.exe are");
            Log.Line("  -vmf <path>           the map source, which is where the state file lives");
            Log.Line("  -state <path>         the state file, if it is somewhere else");
            Log.Line("  -keepstaging          leave the staging directory behind for inspection");
        }
    }
}

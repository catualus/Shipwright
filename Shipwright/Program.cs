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

using System;
using System.IO;

namespace Shipwright.Ui
{
    /// <summary>
    /// What the window was told to work on.
    ///
    /// Deliberately small. The window edits one map's binding, so it needs the map and nothing else;
    /// the bin folder is accepted because the step's Configure line passes it and a later version
    /// will list the account's own items through Steam, which needs it.
    /// </summary>
    public sealed class UiOptions
    {
        public const string Usage =
            "shipwright-ui -vmf <map.vmf> [-state <path>] [-bin <folder>]";

        /// <summary>The map this binding belongs to. Its name is what the window says at the top.</summary>
        public string MapSource = "";

        public string StatePath = "";

        /// <summary>Where Garry's Mod's tools are. Unused today; kept because the step passes it.</summary>
        public string BinFolder = "";

        public string MapName => Path.GetFileNameWithoutExtension(MapSource);

        public static UiOptions Parse(string[] args)
        {
            var options = new UiOptions();
            string? state = null;

            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i].ToLowerInvariant())
                {
                    case "-vmf":
                    case "-map":
                        options.MapSource = Next(args, ref i);
                        break;

                    case "-state":
                        state = Next(args, ref i);
                        break;

                    case "-bin":
                        options.BinFolder = Next(args, ref i);
                        break;

                    default:
                        // A bare path, so that dragging a map onto the executable works.
                        if (!args[i].StartsWith('-') && options.MapSource.Length == 0)
                            options.MapSource = args[i];
                        break;
                }
            }

            if (options.MapSource.Length == 0)
                throw new ArgumentException("No map given.");

            options.MapSource = Path.GetFullPath(options.MapSource);
            options.StatePath = state ?? WorkshopState.PathFor(options.MapSource);

            return options;
        }

        private static string Next(string[] args, ref int index)
        {
            if (index + 1 >= args.Length)
                throw new ArgumentException($"{args[index]} needs a value.");

            return args[++index];
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Shipwright
{
    /// <summary>
    /// The parsed command line.
    ///
    /// Every switch that causes something irreversible is off unless it is present. There is no
    /// configuration file that can turn one on, and no environment variable: the only way this
    /// uploads anything is for the word to be on the command line Compile Pal shows in the step's
    /// argument summary, where the user can see it.
    /// </summary>
    public sealed class Options
    {
        public string BspPath = "";

        /// <summary>Where gmad.exe and gmpublish.exe live. Compile Pal passes $binFolder$.</summary>
        public string BinFolder = "";

        /// <summary>
        /// The map source file, which is where the state file goes. Compile Pal passes $vmfFile$;
        /// when a BSP was queued directly, that is the BSP.
        /// </summary>
        public string MapSource = "";

        public string GameName = "";

        /// <summary>Actually upload. Without it the run stops after building and inspecting the addon.</summary>
        public bool Publish;

        /// <summary>Allow creating a new Workshop item when there is no ID to update.</summary>
        public bool AllowCreate;

        /// <summary>An explicit item ID, overriding the state file.</summary>
        public string? ItemId;

        /// <summary>Ship the entity lump beside the map. Off, which is the point of the tool.</summary>
        public bool IncludeLump;

        public bool IncludeNav;
        public bool IncludeAin;

        public string? IconPath;
        public string? Title;
        public string ChangeNote = "";
        public string? ChangeNoteFile;
        public List<string> Tags = new();

        /// <summary>Publish even when the packed addon is byte for byte what was published last time.</summary>
        public bool Force;

        /// <summary>Refuse to publish the same item again within this many minutes.</summary>
        public int MinIntervalMinutes = 5;

        public string? StatePath;
        public bool KeepStaging;

        /// <summary>Skip the public lookup of the item being updated. For a machine with no network.</summary>
        public bool Offline;

        public static Options Parse(string[] args, int firstIndex)
        {
            var options = new Options();
            var positional = new List<string>();

            for (int i = firstIndex; i < args.Length; i++)
            {
                string arg = args[i];

                if (!arg.StartsWith("-", StringComparison.Ordinal))
                {
                    positional.Add(arg);
                    continue;
                }

                switch (arg.ToLowerInvariant())
                {
                    case "-publish": options.Publish = true; break;
                    case "-allowcreate": options.AllowCreate = true; break;
                    case "-includelump": options.IncludeLump = true; break;
                    case "-includenav": options.IncludeNav = true; break;
                    case "-includeain": options.IncludeAin = true; break;
                    case "-force": options.Force = true; break;
                    case "-keepstaging": options.KeepStaging = true; break;
                    case "-offline": options.Offline = true; break;

                    case "-bin": options.BinFolder = Next(args, ref i, arg); break;
                    case "-vmf": options.MapSource = Next(args, ref i, arg); break;
                    case "-gamename": options.GameName = Next(args, ref i, arg); break;
                    case "-id": options.ItemId = Next(args, ref i, arg); break;
                    case "-icon": options.IconPath = Next(args, ref i, arg); break;
                    case "-title": options.Title = Next(args, ref i, arg); break;
                    case "-changes": options.ChangeNote = Next(args, ref i, arg); break;
                    case "-changesfile": options.ChangeNoteFile = Next(args, ref i, arg); break;
                    case "-state": options.StatePath = Next(args, ref i, arg); break;

                    case "-tags":
                        options.Tags.AddRange(Next(args, ref i, arg)
                            .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries));
                        break;

                    case "-mininterval":
                        options.MinIntervalMinutes = int.TryParse(Next(args, ref i, arg), out int minutes) && minutes >= 0
                            ? minutes
                            : throw new ArgumentException("-mininterval takes a number of minutes.");
                        break;

                    default:
                        /*
                         * Unknown switches are refused rather than ignored. Compile Pal concatenates
                         * a preset's parameters into one command line, and a preset built for one
                         * game hands its arguments to whatever step runs next - so an unrecognised
                         * switch here means this step is being passed somebody else's parameters,
                         * and carrying on would mean publishing under assumptions nobody made.
                         */
                        throw new ArgumentException($"Unknown option: {Sanitize.PlainText(arg, 64)}");
                }
            }

            if (positional.Count == 0)
                throw new ArgumentException("No map given. Pass the path to the compiled .bsp.");

            if (positional.Count > 1)
                throw new ArgumentException(
                    $"Expected one map, got {positional.Count}. A path with spaces has to be quoted.");

            options.BspPath = Path.GetFullPath(positional[0]);

            if (options.MapSource.Length == 0)
                options.MapSource = options.BspPath;

            options.StatePath ??= WorkshopState.PathFor(options.MapSource);

            return options;
        }

        private static string Next(string[] args, ref int index, string flag)
        {
            if (index + 1 >= args.Length)
                throw new ArgumentException($"{flag} needs a value.");

            return args[++index];
        }

        /// <summary>
        /// The change note, from -changes or -changesfile, sanitised.
        ///
        /// A file is offered because a note is the one piece of free text long enough to want
        /// newlines in, and Compile Pal parameter values cannot carry them.
        /// </summary>
        public string ResolveChangeNote()
        {
            string raw = ChangeNote;

            if (ChangeNoteFile != null)
            {
                try
                {
                    raw = File.ReadAllText(ChangeNoteFile);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    Log.Warn($"could not read the change note file: {e.Message}");
                    raw = "";
                }
            }

            return Sanitize.ChangeNote(raw);
        }

        /// <summary>
        /// The title for a new item: the parameter, then the map's own state file, then its name.
        ///
        /// That order, and not the other one. The parameter is the same string for every map in the
        /// queue, so it is only ever set deliberately - by someone publishing one map, or driving
        /// this from a script - and when it is set it should win. The state file is where the
        /// settings window puts a title that belongs to one map.
        /// </summary>
        public string ResolveTitle(WorkshopState? state = null)
        {
            string asked = Sanitize.Title(Title);
            if (asked.Length > 0)
                return asked;

            string stored = Sanitize.Title(state?.Title);
            if (stored.Length > 0)
                return stored;

            return Path.GetFileNameWithoutExtension(BspPath);
        }

        /// <summary>Tags from the parameter if any were given, otherwise the map's own.</summary>
        public IEnumerable<string> ResolveTags(WorkshopState? state = null) =>
            Tags.Count > 0 ? Tags : (state?.Tags ?? Array.Empty<string>());
    }
}

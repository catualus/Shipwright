using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Shipwright
{
    public sealed record ToolResult(int ExitCode, string Output)
    {
        public bool Ok => ExitCode == 0;
    }

    /// <summary>
    /// Runs Garry's Mod's own publishing tools.
    ///
    /// WHY THESE AND NOT SOMETHING ELSE
    ///
    /// gmad and gmpublish ship with the game, and gmpublish uploads through the Steam client session
    /// that is already signed in. That is the entire security design of this tool: it never sees a
    /// password, never stores a token, and cannot upload to an item the signed-in account does not
    /// own, because Steam - not this program - decides that.
    ///
    /// The alternative, SteamCMD with +login, would mean a username and password on a command line
    /// that Compile Pal writes verbatim into debug.log and the compile log, both of which people
    /// paste into Discord when asking why a compile failed. There is no version of that which is
    /// acceptable, so there is no support for it here and no parameter that could turn it on.
    /// </summary>
    public static class GmodTools
    {
        /// <summary>
        /// Finds gmad.exe and gmpublish.exe.
        ///
        /// Compile Pal's bin folder for Garry's Mod points at bin/win64 on the 64 bit branch and bin
        /// on the 32 bit one, and both tools sit beside the engine binaries in either. The parent is
        /// searched as a fallback for a configuration pointing one level in.
        /// </summary>
        public static string? Find(string toolName, string binFolder)
        {
            var candidates = new List<string>();

            if (!string.IsNullOrWhiteSpace(binFolder))
            {
                candidates.Add(Path.Combine(binFolder, toolName));

                string? parent = Path.GetDirectoryName(binFolder.TrimEnd(Path.DirectorySeparatorChar));
                if (parent != null)
                {
                    candidates.Add(Path.Combine(parent, toolName));
                    candidates.Add(Path.Combine(parent, "win64", toolName));
                }
            }

            foreach (string candidate in candidates)
                if (File.Exists(candidate))
                    return Path.GetFullPath(candidate);

            return null;
        }

        /// <summary>
        /// Packs a staging directory into a .gma.
        /// </summary>
        public static ToolResult Pack(string gmadPath, string stagingRoot, string outputGma)
        {
            return Run(gmadPath, "gmad", new[]
            {
                "create",
                "-folder", stagingRoot,
                "-out", outputGma,
            });
        }

        /// <summary>Updates an existing item. The ID is the item this map has been bound to.</summary>
        public static ToolResult Update(string gmpublishPath, string gmaPath, ulong id, string changeNote)
        {
            var args = new List<string> { "update", "-addon", gmaPath, "-id", id.ToString() };

            if (changeNote.Length > 0)
            {
                args.Add("-changes");
                args.Add(changeNote);
            }

            return Run(gmpublishPath, "gmpub", args);
        }

        /// <summary>One Workshop item the signed-in account has published.</summary>
        public sealed record PublishedItem(ulong Id, string Title);

        /// <summary>
        /// Everything the signed-in account has published, as gmpublish reports it.
        ///
        /// WHY THIS AND NOT STEAMWORKS
        ///
        /// The obvious way to list an account's items is to bind Steamworks, initialise as app 4000
        /// and run a UGC query - a native dependency, an interface version to match against whatever
        /// Steam ships, and a callback pump, all to ask a question the tool sitting in the game's bin
        /// folder already answers. gmpublish has a "list" command, it runs through the same signed-in
        /// client the upload does, and it needs nothing that is not already installed.
        /// </summary>
        public static (ToolResult Result, List<PublishedItem> Items) List(string gmpublishPath)
        {
            var result = Run(gmpublishPath, "gmpub", new[] { "list" }, quiet: true);

            return (result, ParseList(result.Output));
        }

        /// <summary>
        /// Reads the item list out of gmpublish's output.
        ///
        /// The real thing looks like this:
        ///
        /// <code>
        /// Setting breakpad minidump AppID = 4000
        /// SteamInternal_SetMinidumpSteamID:  Caching Steam ID:  76561198115990249 [API loaded no]
        ///
        /// Getting published files..
        ///         3790485925      50.4 KB "[Meowy Roleplay] Gang Flag"
        ///         3706313781      74.8 MB "[Meowy Roleplay] Map"
        /// Done
        /// </code>
        ///
        /// Two things in that are traps. The minidump line carries a SteamID - seventeen digits, a
        /// perfectly good looking Workshop id - so an id is only accepted when it is the first thing
        /// on the line. And the size sits between the id and the title, so taking "the rest of the
        /// line" as a title produces "50.4 KB [Meowy Roleplay] Gang Flag".
        ///
        /// Still forgiving about the shape: the title is read from quotes when they are there, and
        /// from the line with a leading size removed when they are not, because this is a console
        /// tool's output rather than an interface and it has changed before.
        /// </summary>
        internal static List<PublishedItem> ParseList(string output)
        {
            var items = new List<PublishedItem>();
            var seen = new HashSet<ulong>();

            foreach (string raw in output.Split('\n'))
            {
                var line = Regex.Match(raw.TrimEnd(), @"^\s*(?<id>\d{6,20})(\s+(?<rest>.*))?$");

                if (!line.Success || !Sanitize.IsWorkshopId(line.Groups["id"].Value, out ulong id))
                    continue;

                if (seen.Add(id))
                    items.Add(new PublishedItem(id, TitleFrom(line.Groups["rest"].Value)));
            }

            return items;
        }

        /// <summary>The title out of what follows the id: the quoted part, or the part after the size.</summary>
        private static string TitleFrom(string rest)
        {
            var quoted = Regex.Match(rest, "\"(?<title>[^\"]*)\"");

            if (quoted.Success)
                return Sanitize.DisplayText(quoted.Groups["title"].Value, Sanitize.MaxTitle);

            // "74.8 MB Some Addon" - the size is gmpublish's, not part of anybody's title.
            string withoutSize = Regex.Replace(rest, @"^\s*\d+(\.\d+)?\s*(B|KB|MB|GB|TB)\s*", "",
                RegexOptions.IgnoreCase);

            return Sanitize.DisplayText(withoutSize.Trim(' ', '\t', '-', ':', '|'), Sanitize.MaxTitle);
        }

        /// <summary>Creates a new item. The icon is required; gmpublish fails with (9) without one.</summary>
        public static ToolResult Create(string gmpublishPath, string gmaPath, string iconPath)
        {
            return Run(gmpublishPath, "gmpub", new[]
            {
                "create",
                "-addon", gmaPath,
                "-icon", iconPath,
            });
        }

        /// <summary>
        /// Recovers the ID of a newly created item from gmpublish's output.
        ///
        /// UNVERIFIED, AND TREATED AS SUCH
        ///
        /// gmpublish is closed source and its create output is not documented. What is documented is
        /// that a created item has an ID, and that the ID is a long decimal number - so this looks
        /// for one and takes the last, which is the one nearest whatever success line was printed.
        ///
        /// The caller must handle this returning null as a serious event and not as a failure to
        /// publish: at that point an item exists on the account with content on it, and the only
        /// record of which item is a line in a log. It says so, loudly, rather than pretending the
        /// publish did not happen.
        /// </summary>
        public static ulong? ParseCreatedId(string output)
        {
            ulong? found = null;

            foreach (Match match in Regex.Matches(output, @"\b\d{6,20}\b"))
                if (Sanitize.IsWorkshopId(match.Value, out ulong id))
                    found = id;

            return found;
        }

        /// <summary>
        /// Starts a child process with an argument list rather than a command line.
        ///
        /// ArgumentList, not Arguments: the values here include a change note that originated as
        /// free text in a Compile Pal parameter, and .NET quotes each element of an argument list
        /// for the platform instead of leaving the splitting to whatever the string happens to
        /// contain. The note is sanitised as well - both, because either alone is one mistake away
        /// from an argument landing on the wrong flag.
        ///
        /// The working directory is the tool's own folder. gmpublish links steam_api64.dll from
        /// beside itself, and started from anywhere else it fails to initialise Steam with a message
        /// that says nothing about the working directory.
        /// </summary>
        private static ToolResult Run(string exePath, string logPrefix, IEnumerable<string> arguments, bool quiet = false)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = exePath,
                WorkingDirectory = SteamAppDirectory(exePath),
                RedirectStandardOutput = true,
                RedirectStandardError = true,

                /*
                 * gmpublish ends with "Press any key to continue". With a console attached that is a
                 * pause; with stdin redirected and then closed it reads end-of-file and exits. Without
                 * this, a publish inside a compile step waits for a keypress nobody can give it -
                 * the step hangs until the compile is cancelled.
                 */
                RedirectStandardInput = true,

                UseShellExecute = false,
                CreateNoWindow = true,

                /*
                 * gmpublish writes UTF-8. Read with the console's OEM code page instead - which is
                 * what .NET does by default - an item called "Zero's Trashman" comes back as
                 * "Zero\u00b4s", and every accented title in somebody's Workshop turns to mojibake
                 * on the way into the list.
                 */
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };

            foreach (string argument in arguments)
                startInfo.ArgumentList.Add(argument);

            var captured = new StringBuilder();

            using var process = new Process { StartInfo = startInfo };

            process.OutputDataReceived += (_, e) => Capture(captured, logPrefix, e.Data, quiet);
            process.ErrorDataReceived += (_, e) => Capture(captured, logPrefix, e.Data, quiet);

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            process.StandardInput.Close();
            process.WaitForExit();

            return new ToolResult(process.ExitCode, captured.ToString());
        }

        /// <summary>
        /// The folder gmpublish has to be run from.
        ///
        /// Steamworks decides which application it is initialising from steam_appid.txt in the
        /// working directory, and Garry's Mod keeps that file in its root rather than beside the
        /// tools in bin. Run from bin, gmpublish has no app id to claim.
        ///
        /// Walks up from the executable looking for it, and falls back to the executable's own
        /// folder - which is what this used to do unconditionally.
        /// </summary>
        internal static string SteamAppDirectory(string exePath)
        {
            var directory = new DirectoryInfo(Path.GetDirectoryName(Path.GetFullPath(exePath)) ?? ".");
            var start = directory;

            for (int levels = 0; directory != null && levels < 4; levels++, directory = directory.Parent)
                if (File.Exists(Path.Combine(directory.FullName, "steam_appid.txt")))
                    return directory.FullName;

            return start.FullName;
        }

        private static void Capture(StringBuilder captured, string prefix, string? line, bool quiet = false)
        {
            if (line is null)
                return;

            captured.AppendLine(line);

            if (!quiet)
                Log.FromChild(prefix, line);
        }
    }
}

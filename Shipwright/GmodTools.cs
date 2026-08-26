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
        private static ToolResult Run(string exePath, string logPrefix, IEnumerable<string> arguments)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = exePath,
                WorkingDirectory = Path.GetDirectoryName(exePath) ?? ".",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            foreach (string argument in arguments)
                startInfo.ArgumentList.Add(argument);

            var captured = new StringBuilder();

            using var process = new Process { StartInfo = startInfo };

            process.OutputDataReceived += (_, e) => Capture(captured, logPrefix, e.Data);
            process.ErrorDataReceived += (_, e) => Capture(captured, logPrefix, e.Data);

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            process.WaitForExit();

            return new ToolResult(process.ExitCode, captured.ToString());
        }

        private static void Capture(StringBuilder captured, string prefix, string? line)
        {
            if (line is null)
                return;

            captured.AppendLine(line);
            Log.FromChild(prefix, line);
        }
    }
}

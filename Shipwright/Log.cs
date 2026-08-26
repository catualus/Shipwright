using System;
using System.Collections.Generic;

namespace Shipwright
{
    /// <summary>
    /// Everything this tool prints goes through here.
    ///
    /// Compile Pal reads a plugin's stdout as a control channel as well as a log: a line beginning
    /// with COMPILE_PAL_SET rewrites the current game configuration, including the paths to vbsp,
    /// vrad and the game executable, and those changes persist into the next compile. Nothing this
    /// tool prints is meant to do that, but plenty of what it prints is text it did not author -
    /// file names from a staging directory, the output of gmad and gmpublish, the title of a
    /// Workshop item fetched over the network. Any of that reaching the host at the start of a line
    /// unfiltered is a path from "something named a file oddly" to "the next compile ran a different
    /// vbsp".
    ///
    /// So every line goes out through <see cref="Line"/>, which neutralises the token, and forwarded
    /// child output additionally carries a prefix so it cannot begin a line at all.
    ///
    /// The prefixes are Meshwright's convention, which Compile Pal's log already knows how to
    /// colour: bsp, out and check are informational, warn is a warning, and a line beginning
    /// "error:" is filed as an error.
    /// </summary>
    public static class Log
    {
        /// <summary>The token Compile Pal treats as a command rather than a message.</summary>
        internal const string HostControlToken = "COMPILE_PAL_SET";

        /// <summary>Set by the tests to capture output instead of writing to the console.</summary>
        public static Action<string>? Sink;

        public static void Line(string text = "")
        {
            string safe = Neutralise(text);

            if (Sink != null)
                Sink(safe);
            else
                Console.Out.WriteLine(safe);
        }

        public static void Bsp(string text) => Line("bsp   " + text);
        public static void Out(string text) => Line("out   " + text);
        public static void Check(string text) => Line("check " + text);
        public static void Warn(string text) => Line("warn  " + text);

        /// <summary>
        /// An error the host should file as one. Written to stdout rather than stderr because
        /// Compile Pal only reads a step's stdout - anything on stderr is invisible to the user,
        /// which for the one message explaining a failed publish is the wrong place for it.
        /// </summary>
        public static void Error(string text) => Line("error: " + text);

        /// <summary>
        /// Forwards a child process's output, one line at a time, under a prefix.
        ///
        /// The prefix is not decoration. gmad prints the name of every file it packs and gmpublish
        /// prints whatever Steam hands back, so this is the exact text that must never be able to
        /// start a line with the host's control token.
        /// </summary>
        public static void FromChild(string tool, string line)
        {
            string trimmed = line.TrimEnd();
            if (trimmed.Length == 0)
                return;

            Line(tool.PadRight(5) + " " + trimmed);
        }

        /// <summary>
        /// Makes a string safe to print at the start of a line.
        ///
        /// Leading whitespace is stripped before the test, because a line that starts with a space
        /// today may not after some other layer trims it. Control characters go too: a carriage
        /// return mid-message ends the line as far as the host's reader is concerned, and lets the
        /// rest of the message start a new one.
        /// </summary>
        internal static string Neutralise(string text)
        {
            var chars = new List<char>(text.Length);
            foreach (char c in text)
            {
                if (c == '\r' || c == '\n' || (char.IsControl(c) && c != '\t'))
                    chars.Add(' ');
                else
                    chars.Add(c);
            }

            string flattened = new string(chars.ToArray());

            return flattened.TrimStart().StartsWith(HostControlToken, StringComparison.Ordinal)
                ? "[filtered] " + flattened.TrimStart()
                : flattened;
        }
    }
}

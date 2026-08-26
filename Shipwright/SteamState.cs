using System;
using System.Diagnostics;
using System.Linq;

namespace Shipwright
{
    /// <summary>
    /// What the machine's Steam looks like from outside it.
    ///
    /// Deliberately shallow. This asks whether a process is running and nothing else: it does not
    /// read the registry for the signed-in account, does not open loginusers.vdf, does not learn or
    /// print who is logged in. None of that is needed to decide whether to attempt a publish, and a
    /// tool that reports a Workshop failure by naming the user's Steam account has put a personal
    /// identifier into a log file that gets shared.
    /// </summary>
    public static class SteamState
    {
        /// <summary>
        /// Whether the Steam client is running.
        ///
        /// gmpublish uploads through the running client, so without one it fails at Steam
        /// initialisation - a failure that reads like a broken tool rather than "Steam is closed".
        /// </summary>
        public static bool SteamRunning() => ProcessExists("steam");

        /// <summary>
        /// Whether Garry's Mod itself is running.
        ///
        /// A warning rather than a refusal. gmpublish initialising Steam against app 4000 while the
        /// game holds the same app is the sort of thing that works until it does not, and Compile
        /// Pal already asks for the game to be closed for the NAV and CUBEMAPS steps - so it is
        /// worth saying, and not worth failing over.
        /// </summary>
        public static bool GameRunning() => ProcessExists("gmod") || ProcessExists("hl2");

        private static bool ProcessExists(string name)
        {
            try
            {
                return Process.GetProcessesByName(name).Length > 0;
            }
            catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                // Enumerating processes can be denied. Unknown is not the same as absent, and the
                // caller treats it as "carry on" rather than blocking a publish on a check that
                // could not run.
                return true;
            }
        }
    }
}

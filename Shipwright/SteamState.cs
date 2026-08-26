using System;
using System.Diagnostics;
using Microsoft.Win32;

namespace Shipwright
{
    /// <summary>Whether the Steam client is in a state a publish can go through.</summary>
    public enum SteamReadiness
    {
        /// <summary>Running, signed in, and registered. gmpublish should work.</summary>
        Ready,

        /// <summary>No Steam client at all.</summary>
        NotRunning,

        /// <summary>Running, but nobody is signed in.</summary>
        NotSignedIn,

        /// <summary>
        /// Running and signed in, but the client registration names a process that is gone.
        ///
        /// This is the one worth having a name for. Steam records the client's process id in the
        /// registry, and an application asking Steamworks to initialise is refused when that id
        /// belongs to nothing - so gmpublish says "Couldn't initialize Steam! Make sure it is
        /// running!" while Steam is plainly running, and there is no way to guess from that message
        /// that the answer is to restart it.
        /// </summary>
        Stale,

        /// <summary>Could not be determined. Treated as ready, because a guess should not block a publish.</summary>
        Unknown,
    }

    public sealed record SteamStatus(SteamReadiness Readiness, string Message)
    {
        /// <summary>Whether it is worth attempting a publish at all.</summary>
        public bool CanPublish => Readiness is SteamReadiness.Ready or SteamReadiness.Unknown;
    }

    /// <summary>
    /// What the machine's Steam looks like from outside it.
    ///
    /// Deliberately shallow, and deliberately not personal. It reads whether a client is running,
    /// whether somebody is signed in, and whether the client's own registration still points at a
    /// live process. It does not read who is signed in, does not open loginusers.vdf, and never puts
    /// an account name or a SteamID into a log that gets pasted into Discord.
    /// </summary>
    public static class SteamState
    {
        private const string ActiveProcessKey = @"Software\Valve\Steam\ActiveProcess";

        /// <summary>Whether a Steam client process exists at all.</summary>
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

        /// <summary>
        /// Works out why a publish would fail before attempting one.
        ///
        /// The registry is read rather than trusted blindly: every failure to read it is Unknown,
        /// which the caller treats as "carry on". A check that cannot run is not evidence of a
        /// problem, and refusing to publish on one would be worse than the confusing error it is
        /// trying to replace.
        /// </summary>
        public static SteamStatus Check()
        {
            if (!SteamRunning())
                return new SteamStatus(SteamReadiness.NotRunning,
                    "Steam is not running. gmpublish uploads through the signed-in Steam client.");

            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(ActiveProcessKey);

                if (key is null)
                    return new SteamStatus(SteamReadiness.Unknown, "Steam is running.");

                int activeUser = key.GetValue("ActiveUser") as int? ?? 0;
                int clientPid = key.GetValue("pid") as int? ?? 0;

                if (activeUser == 0)
                    return new SteamStatus(SteamReadiness.NotSignedIn,
                        "Steam is running but nobody is signed in. Sign in and try again.");

                if (clientPid != 0 && !ProcessExists(clientPid))
                    return new SteamStatus(SteamReadiness.Stale,
                        $"Steam is running and signed in, but its registration still points at process {clientPid}, " +
                        "which no longer exists - so anything asking Steam to initialise is refused. " +
                        "Restart Steam and it will work.");

                return new SteamStatus(SteamReadiness.Ready, "Steam is ready.");
            }
            catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or System.IO.IOException)
            {
                return new SteamStatus(SteamReadiness.Unknown, "Steam is running.");
            }
        }

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

        private static bool ProcessExists(int id)
        {
            try
            {
                using var process = Process.GetProcessById(id);
                return !process.HasExited;
            }
            catch (ArgumentException)
            {
                return false;       // no such process, which is the case this exists to catch
            }
            catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                return true;
            }
        }
    }
}

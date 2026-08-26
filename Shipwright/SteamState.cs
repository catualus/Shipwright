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
        /// A hint, and nothing stronger. Steam records a process id in the registry and an
        /// application refused by Steamworks often finds that id belongs to nothing - but a machine
        /// has been seen where the id was long dead, stayed dead across restarts, and gmpublish
        /// published perfectly well anyway. So this is never announced up front; it is only offered
        /// as a possible reason once something has actually failed.
        /// </summary>
        Stale,

        /// <summary>Could not be determined. Treated as ready, because a guess should not block a publish.</summary>
        Unknown,
    }

    public sealed record SteamStatus(SteamReadiness Readiness, string Message)
    {
        /// <summary>
        /// Whether Steam looks usable. Never a reason to refuse - see <see cref="WorthSayingUpFront"/>.
        /// </summary>
        public bool Healthy => Readiness is SteamReadiness.Ready or SteamReadiness.Unknown;

        /// <summary>
        /// Whether this is solid enough to say before anything has gone wrong.
        ///
        /// Only the two facts that are actually observable: there is no Steam process, or nobody is
        /// signed in. Both are read directly rather than inferred, and both are worth knowing before
        /// a map is packed.
        ///
        /// <see cref="SteamReadiness.Stale"/> is deliberately not one of them. It is an inference
        /// from a registry value about what Steamworks will do, it has been wrong on a real machine -
        /// where every publish would have carried a confident warning about a problem that did not
        /// exist - and a warning that cries wolf on a working setup is worse than no warning.
        /// </summary>
        public bool WorthSayingUpFront =>
            Readiness is SteamReadiness.NotRunning or SteamReadiness.NotSignedIn;
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
                        $"Steam is running and signed in, but its client registration points at process {clientPid}, " +
                        "which no longer exists. That usually means anything asking Steam to initialise will be " +
                        "refused. Restarting Steam normally clears it - and on at least one machine it did not, " +
                        "in which case the client is refusing sessions for app 4000 for a reason only Steam knows.");

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

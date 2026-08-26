using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace Shipwright
{
    /// <summary>What a run decided to do, before it did it.</summary>
    public sealed record PublishPlan(
        string MapName,
        ulong? ItemId,
        bool WouldCreate,
        IReadOnlyList<string> Files,
        long AddonBytes,
        string Sha256);

    /// <summary>
    /// The run itself: inspect, stage, pack, decide, and only then upload.
    ///
    /// The order is the design. Everything that can be learned without touching the Workshop is
    /// learned first and printed, so that the last step - the irreversible one - happens with the
    /// full picture already on screen. A run that stops before that point has cost the user nothing
    /// and told them everything, which is why it is the default.
    /// </summary>
    public static class Publisher
    {
        /// <summary>Exit code for a run that did what it was asked, including deciding not to publish.</summary>
        public const int Ok = 0;

        /// <summary>
        /// Exit code for a failure.
        ///
        /// Compile Pal turns a non-zero exit from a step into a fatal error and cancels the rest of
        /// the compile, so this is reserved for things that really are failures. Declining to
        /// publish - no item bound, nothing changed, too soon since the last one - is a normal
        /// outcome and exits <see cref="Ok"/>.
        /// </summary>
        public const int Failed = 1;

        public static int Run(Options options)
        {
            string mapName = Path.GetFileNameWithoutExtension(options.BspPath);

            if (!File.Exists(options.BspPath))
            {
                Log.Error($"no compiled map at {options.BspPath}");
                return Failed;
            }

            var facts = BspReader.Read(options.BspPath);
            if (!facts.Readable)
            {
                Log.Error($"{Path.GetFileName(options.BspPath)}: {facts.Message}");
                return Failed;
            }

            var bspInfo = new FileInfo(options.BspPath);
            Log.Bsp($"{Path.GetFileName(options.BspPath)}  version {facts.Version}, revision {facts.MapRevision}, {bspInfo.Length:N0} bytes");

            var lump = LumpFiles.Inspect(options.BspPath);
            ReportLumpState(facts, lump);

            bool blockedByCompile = CompileHadErrors(out string compileVerdict);
            if (compileVerdict.Length > 0)
                Log.Warn(compileVerdict);

            // Everything above is inspection. From here on the run needs the tools.
            string? gmad = GmodTools.Find("gmad.exe", options.BinFolder);
            string? gmpublish = GmodTools.Find("gmpublish.exe", options.BinFolder);

            if (gmad is null || gmpublish is null)
            {
                Log.Error($"gmad.exe and gmpublish.exe were not found near {options.BinFolder}. " +
                          "They ship with Garry's Mod, in its bin folder.");
                return Failed;
            }

            /*
             * Before anything is packed, not after.
             *
             * Packing a large map takes a while and produces a file nobody will use if the upload
             * cannot happen, and the reasons it cannot are all knowable up front. Only when this run
             * intends to publish: a dry run is exactly what someone with Steam closed should still
             * be able to do.
             *
             * Said here rather than left to gmpublish, which reports every one of these the same way
             * - "Couldn't initialize Steam! Make sure it is running!" - including the case where
             * Steam is running, is signed in, and the only thing wrong is that its own registration
             * points at a process that has exited.
             */
            if (options.Publish && SteamState.Check() is { Healthy: false } steam)
                Log.Warn(steam.Message);

            using var staging = Staging.Create(mapName, options.KeepStaging);

            var chosen = ChooseFiles(options, facts, lump, mapName);
            foreach (var (source, relative) in chosen)
                staging.Add(source, relative);

            var state = WorkshopState.Load(options.StatePath!);

            staging.Write("addon.json", AddonManifest.Build(
                options.ResolveTitle(state), options.ResolveTags(state)));

            string gmaPath = Path.Combine(staging.Root, mapName + ".gma");

            var packed = GmodTools.Pack(gmad, staging.Root, gmaPath);
            if (!packed.Ok || !File.Exists(gmaPath))
            {
                Log.Error($"gmad could not pack the addon (exit code {packed.ExitCode}). Its output is above.");
                return Failed;
            }

            long addonBytes = new FileInfo(gmaPath).Length;
            string hash = Sha256Of(gmaPath);

            ulong? target = ResolveTarget(options, state);
            bool wouldCreate = target is null;

            var plan = new PublishPlan(
                mapName,
                target,
                wouldCreate,
                staging.Files.Select(f => $"{f.RelativePath}  ({f.Bytes:N0} bytes)").ToList(),
                addonBytes,
                hash);

            ReportPlan(plan, options, state);

            if (target is { } id && !options.Offline)
            {
                var details = WorkshopLookup.Describe(id, TimeSpan.FromSeconds(10));

                if (details.Found)
                {
                    Log.Check($"item {id} is \"{details.Title}\", app {details.ConsumerAppId}" +
                              (details.Updated is { } when_ ? $", last updated {when_.ToLocalTime():yyyy-MM-dd HH:mm}" : ""));

                    if (details.ConsumerAppId != WorkshopLookup.GarrysModAppId)
                    {
                        Log.Error($"item {id} belongs to app {details.ConsumerAppId}, not Garry's Mod " +
                                  $"({WorkshopLookup.GarrysModAppId}). Refusing to touch it.");
                        return Failed;
                    }
                }
                else
                {
                    Log.Warn($"item {id} {details.Message}");
                }
            }

            if (wouldCreate && !options.AllowCreate)
            {
                Log.Warn($"no Workshop item is bound to this map, and creating one was not allowed. " +
                         $"Nothing was published. Bind one by putting its ID in " +
                         $"{Path.GetFileName(options.StatePath!)}, or enable \"Allow creating a new item\".");
                return Ok;
            }

            if (!options.Publish)
            {
                Log.Out("dry run: nothing was uploaded. Enable \"Actually publish\" to do it for real.");
                return Ok;
            }

            if (blockedByCompile)
            {
                Log.Error("this compile logged errors, so the map was not published. " +
                          "Fix them and compile again, or publish by hand if the errors are known to be harmless.");
                return Failed;
            }

            if (!options.Force && !wouldCreate && state.GmaSha256 == hash)
            {
                Log.Out("the packed addon is identical to the one published last time; skipping the update. " +
                        "Every subscriber and every server would have redownloaded the same bytes.");
                return Ok;
            }

            if (!options.Force && TooSoon(state, options, out string wait))
            {
                Log.Warn(wait);
                return Ok;
            }

            if (SteamState.GameRunning())
                Log.Warn("Garry's Mod appears to be running. If the upload fails to initialise Steam, close it first.");

            return wouldCreate
                ? Create(options, gmpublish, gmaPath, facts, hash, state)
                : Update(options, gmpublish, gmaPath, target!.Value, facts, hash, state);
        }

        private static int Update(Options options, string gmpublish, string gmaPath, ulong id,
            BspFacts facts, string hash, WorkshopState state)
        {
            string note = options.ResolveChangeNote();

            Log.Out($"updating Workshop item {id}");

            var result = GmodTools.Update(gmpublish, gmaPath, id, note);

            if (!result.Ok || FailedToReachSteam(result))
            {
                Log.Error($"gmpublish failed with exit code {result.ExitCode}. Nothing was published. " +
                          "Its output is above; a failure here usually means Steam is signed in as an account " +
                          "that does not own the item.");

                ExplainSteamFailure(result);
                return Failed;
            }

            Record(options, state, id, facts, hash);

            Log.Out($"published. https://steamcommunity.com/sharedfiles/filedetails/?id={id}");
            WarnAboutStrippedEntities(facts, options);
            return Ok;
        }

        private static int Create(Options options, string gmpublish, string gmaPath,
            BspFacts facts, string hash, WorkshopState state)
        {
            string? iconPath = options.IconPath ?? state.IconPath;

            if (iconPath is null)
            {
                Log.Error("creating a new item needs an icon: a 512x512 baseline JPEG. " +
                          "gmpublish refuses to create one without it.");
                return Failed;
            }

            var icon = IconCheck.Inspect(iconPath);
            if (!icon.Acceptable)
            {
                Log.Error($"the icon {Path.GetFileName(iconPath)} {icon.Message}");
                return Failed;
            }

            Log.Check($"icon: {icon.Message}");

            /*
             * gmpublish wants the icon to share the .gma's base name, so it is copied beside it
             * rather than passed from wherever the user keeps it. The staging directory is deleted
             * afterwards either way.
             */
            string icoBeside = Path.ChangeExtension(gmaPath, ".jpg");
            File.Copy(iconPath, icoBeside, overwrite: true);

            Log.Out("creating a new Workshop item");

            var result = GmodTools.Create(gmpublish, gmaPath, icoBeside);

            if (!result.Ok)
            {
                Log.Error($"gmpublish failed with exit code {result.ExitCode}. Its output is above.");
                return Failed;
            }

            if (FailedToReachSteam(result))
            {
                Log.Error("gmpublish could not reach Steam, so nothing was created.");
                ExplainSteamFailure(result);
                return Failed;
            }

            ulong? created = GmodTools.ParseCreatedId(result.Output);

            if (created is null)
            {
                /*
                 * The worst outcome this program has. The item probably exists, with the map on it,
                 * and nothing here knows its ID - so the next run would create a second one. Said as
                 * an error, with what to do about it, rather than as a successful publish.
                 */
                Log.Error("the item was created but its ID could not be read from gmpublish's output. " +
                          "Find it at https://steamcommunity.com/my/myworkshopfiles/ and put the ID in " +
                          $"{Path.GetFileName(options.StatePath!)} before publishing again, or the next run " +
                          "will create a second item.");
                return Failed;
            }

            Record(options, state, created.Value, facts, hash);

            Log.Out($"created item {created.Value}. https://steamcommunity.com/sharedfiles/filedetails/?id={created.Value}");
            Log.Warn("a newly created item stays hidden until the Steam Workshop legal agreement has been " +
                     "accepted on its page, and its description and images are set on the website.");
            WarnAboutStrippedEntities(facts, options);
            return Ok;
        }

        /// <summary>
        /// Whether gmpublish gave up before talking to Steam at all.
        ///
        /// It exits 0 when this happens, so the exit code says the run succeeded while nothing was
        /// uploaded. The message it printed is the only evidence.
        /// </summary>
        private static bool FailedToReachSteam(ToolResult result) =>
            result.Output.Contains("initialize Steam", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Adds what can be seen from outside Steam to gmpublish's one-line explanation.
        ///
        /// "Couldn't initialize Steam! Make sure it is running!" is what it says whether Steam is
        /// closed, signed out, or running and signed in with a client registration pointing at a
        /// process that no longer exists - and the last of those is the one nobody guesses.
        /// </summary>
        private static void ExplainSteamFailure(ToolResult result)
        {
            if (!FailedToReachSteam(result))
                return;

            var steam = SteamState.Check();

            Log.Warn(steam.Healthy
                ? "Steam looks healthy from here - running, signed in and registered - so this is the client " +
                  "refusing a session for app 4000 rather than anything this tool can see. Running gmpublish " +
                  "by hand from Garry's Mod's own folder will fail the same way."
                : steam.Message);
        }

        private static void Record(Options options, WorkshopState state, ulong id, BspFacts facts, string hash)
        {
            state.WorkshopId = id.ToString();
            state.Title = options.ResolveTitle(state);
            state.LastPublished = DateTimeOffset.UtcNow;
            state.BspRevision = facts.MapRevision;
            state.EntitiesStripped = facts.EntitiesStripped;
            state.GmaSha256 = hash;

            try
            {
                state.Save(options.StatePath!);
                Log.Out($"recorded in {Path.GetFileName(options.StatePath!)}");
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Log.Error($"the publish succeeded but {options.StatePath} could not be written: {e.Message}. " +
                          $"Put the ID {id} in it by hand, or the next run will create a second item.");
            }
        }

        /// <summary>
        /// Decides what goes in the addon.
        ///
        /// The .lmp is excluded by default and that is the whole point of the tool: the Workshop copy
        /// is what clients download, clients receive entities from the server they are playing on,
        /// and a map whose entity lump is not in the BSP cannot be decompiled from the Workshop
        /// download. The cost is that the same download is a lifeless map in singleplayer, which the
        /// warning says every time rather than once in a readme.
        /// </summary>
        private static List<(string Source, string Relative)> ChooseFiles(
            Options options, BspFacts facts, LumpPairing lump, string mapName)
        {
            var chosen = new List<(string, string)> { (options.BspPath, $"maps/{mapName}.bsp") };

            if (options.IncludeLump)
            {
                switch (lump.State)
                {
                    case LumpPairingState.Matched:
                        chosen.Add((lump.Path!, $"maps/{Path.GetFileName(lump.Path!)}"));
                        Log.Out("including the entity lump, so the published map plays on its own.");
                        break;

                    case LumpPairingState.Stale:
                        Log.Warn($"not including {Path.GetFileName(lump.Path!)}: it belongs to map revision " +
                                 $"{lump.LumpRevision} and this map is revision {lump.BspRevision}. The engine " +
                                 "would ignore it.");
                        break;

                    case LumpPairingState.Missing:
                        Log.Warn("no entity lump file was found to include.");
                        break;

                    case LumpPairingState.Unreadable:
                        Log.Warn($"not including {Path.GetFileName(lump.Path!)}: it could not be read as a lump file.");
                        break;
                }
            }

            string navPath = Path.ChangeExtension(options.BspPath, ".nav");
            if (options.IncludeNav && File.Exists(navPath))
                chosen.Add((navPath, $"maps/{mapName}.nav"));
            else if (options.IncludeNav)
                Log.Warn($"no nav mesh at {Path.GetFileName(navPath)} to include.");

            string ainPath = Path.ChangeExtension(options.BspPath, ".ain");
            if (options.IncludeAin && File.Exists(ainPath))
                chosen.Add((ainPath, $"maps/graphs/{mapName}.ain"));

            string thumb = Path.Combine(Path.GetDirectoryName(options.BspPath)!, "thumb", mapName + ".png");
            if (File.Exists(thumb))
                chosen.Add((thumb, $"maps/thumb/{mapName}.png"));

            return chosen;
        }

        private static void ReportLumpState(BspFacts facts, LumpPairing lump)
        {
            switch (facts.LumpState)
            {
                case EntityLumpState.Present:
                    Log.Bsp(facts.EntityCount >= 0
                        ? $"entity lump: {facts.EntityCount:N0} entities, {facts.EntityLumpLength:N0} bytes"
                        : $"entity lump: {facts.EntityLumpLength:N0} bytes");
                    break;

                case EntityLumpState.WorldspawnOnly:
                    Log.Bsp("entity lump: worldspawn only - entities have been moved out to a .lmp");
                    break;

                case EntityLumpState.Empty:
                    Log.Bsp("entity lump: empty - entities and worldspawn have both been moved out");
                    break;
            }

            switch (lump.State)
            {
                case LumpPairingState.Matched:
                    Log.Check($"{Path.GetFileName(lump.Path!)} matches this map (revision {lump.BspRevision})");
                    break;

                case LumpPairingState.Stale:
                    Log.Warn($"{Path.GetFileName(lump.Path!)} is revision {lump.LumpRevision}, this map is " +
                             $"{lump.BspRevision}. It belongs to an older compile.");
                    break;

                case LumpPairingState.Missing when facts.EntitiesStripped:
                    Log.Warn("this map's entities have been moved out and no .lmp was found beside it. " +
                             "Without one, the map has no entities anywhere.");
                    break;
            }
        }

        private static void ReportPlan(PublishPlan plan, Options options, WorkshopState state)
        {
            Log.Line();
            Log.Out($"addon: {plan.MapName}.gma, {plan.AddonBytes:N0} bytes, sha256 {plan.Sha256[..16]}...");

            foreach (string file in plan.Files)
                Log.Out("  " + file);

            Log.Out(plan.WouldCreate
                ? "target: a new Workshop item"
                : $"target: existing Workshop item {plan.ItemId}" +
                  (state.LastPublished is { } last ? $", last published {last.ToLocalTime():yyyy-MM-dd HH:mm}" : ""));

            if (!plan.WouldCreate && options.ItemId != null)
                Log.Out("        (from -id on the command line, overriding the state file)");
        }

        private static void WarnAboutStrippedEntities(BspFacts facts, Options options)
        {
            if (!facts.EntitiesStripped || options.IncludeLump)
                return;

            Log.Warn($"the published map has no entity lump, and this compile is map revision {facts.MapRevision}. " +
                     "Every server running it needs the matching .lmp copied into its maps folder - the one from " +
                     "an earlier compile will be ignored, and the map will load with nothing in it.");
        }

        private static ulong? ResolveTarget(Options options, WorkshopState state)
        {
            if (options.ItemId != null)
            {
                if (!Sanitize.IsWorkshopId(options.ItemId, out ulong explicitId))
                    throw new ArgumentException($"-id is not a Workshop ID: {Sanitize.PlainText(options.ItemId, 32)}");

                return explicitId;
            }

            return Sanitize.IsWorkshopId(state.WorkshopId, out ulong stored) ? stored : null;
        }

        private static bool TooSoon(WorkshopState state, Options options, out string message)
        {
            message = "";

            if (state.LastPublished is not { } last || options.MinIntervalMinutes <= 0)
                return false;

            var elapsed = DateTimeOffset.UtcNow - last;
            var minimum = TimeSpan.FromMinutes(options.MinIntervalMinutes);

            if (elapsed >= minimum)
                return false;

            message = $"the last publish was {elapsed.TotalMinutes:F0} minutes ago and the minimum interval is " +
                      $"{options.MinIntervalMinutes}. Nothing was uploaded - every update makes every subscriber " +
                      "redownload the map.";
            return true;
        }

        /// <summary>
        /// Whether the compile that led here logged errors.
        ///
        /// Compile Pal does not tell a plugin this, and that is a real gap: only a step exiting
        /// non-zero stops a run, so a leak, a failed pack or a missing texture all reach this step
        /// with the compile still nominally in progress. Publishing the result of one of those is
        /// exactly the case worth refusing.
        ///
        /// So this reads an environment variable the host does not currently set. Where it is
        /// absent, the run says it could not tell rather than assuming the compile went well - and a
        /// host that does start setting it gets the check for free.
        /// </summary>
        private static bool CompileHadErrors(out string message)
        {
            string? raw = Environment.GetEnvironmentVariable("COMPILE_PAL_ERRORS");

            if (raw is null)
            {
                message = "this step cannot see whether the compile logged errors - Compile Pal does not tell " +
                          "plugins. Check the output above before publishing.";
                return false;
            }

            if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int errors) || errors < 0)
            {
                message = $"COMPILE_PAL_ERRORS is \"{Sanitize.PlainText(raw, 32)}\", which is not a count. Ignoring it.";
                return false;
            }

            message = errors > 0
                ? $"this compile has logged {errors} error(s) so far."
                : "";

            return errors > 0;
        }

        private static string Sha256Of(string path)
        {
            using var stream = File.OpenRead(path);
            return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        }
    }
}

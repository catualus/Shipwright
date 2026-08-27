using System;
using System.Text.Json;

namespace Shipwright
{
    /// <summary>
    /// How much attention a map's binding needs before a compile starts.
    ///
    /// The names are the host's vocabulary, not this tool's: Compile Pal colours a chip by them and,
    /// for <see cref="Blocking"/>, refuses to start a run at all. Anything it does not recognise it
    /// treats as <see cref="Info"/>, so a new level here degrades rather than breaks.
    /// </summary>
    public static class StatusSeverity
    {
        public const string Ok = "ok";
        public const string Info = "info";
        public const string Warn = "warn";
        public const string Blocking = "blocking";
    }

    /// <summary>What the queue row says about one map, and whether the run may start.</summary>
    public sealed record MapStatus(string Label, string Detail, string Severity, bool Confirm)
    {
        public string ToJson() =>
            JsonSerializer.Serialize(new
            {
                label = Label,
                detail = Detail,
                severity = Severity,
                confirm = Confirm,
            });
    }

    /// <summary>
    /// Answers "what will happen to this map" without doing any of it.
    ///
    /// WHY IT IS OFFLINE
    ///
    /// This runs whenever the queue changes, for every queued map, in front of someone who is trying
    /// to press Compile. A lookup per refresh would put a network round trip between adding a map
    /// and seeing the window settle, and would fail differently depending on the wifi. Everything
    /// here is read from the state file beside the map, which is also the thing the settings window
    /// just wrote.
    ///
    /// The live lookup still happens - during the publish itself, where a stale title is worth a
    /// round trip and there is a log to print it into.
    ///
    /// WHAT MAKES IT BLOCK
    ///
    /// Only the combination that would waste a whole compile: the step is enabled, it is set to
    /// actually publish, the map is bound to nothing, and creating an item was not allowed. That run
    /// would compile for an hour and then publish nothing. Everything else is a chip and a sentence.
    /// </summary>
    public static class MapStatusReporter
    {
        /// <summary>
        /// The step's current arguments, as Compile Pal resolved them for this preset.
        ///
        /// Passed through the environment rather than the command line because they are a command
        /// line themselves - quotes, paths with spaces, and a change note someone typed - and
        /// nesting one inside another is how a status check ends up reporting on a different map.
        /// </summary>
        public const string StepArgsVariable = "COMPILE_PAL_STEP_ARGS";

        /// <summary>Whether the step is ticked in this map's preset.</summary>
        public const string StepEnabledVariable = "COMPILE_PAL_STEP_ENABLED";

        public static MapStatus Describe(string statePath, string? stepArgs, bool stepEnabled)
        {
            var state = WorkshopState.Load(statePath);

            string args = stepArgs ?? "";

            /*
             * Either source can arm a publish, and neither can disarm the other. The window writes
             * the map's own answer - which is where the switch lives now - and the flag is still
             * read for anyone driving the step from a script.
             */
            bool willPublish = stepEnabled && (state.Publish || HasFlag(args, "-publish"));
            bool mayCreate = state.AllowCreate || HasFlag(args, "-allowcreate");

            bool bound = Sanitize.IsWorkshopId(state.WorkshopId, out ulong id);
            string title = string.IsNullOrWhiteSpace(state.Title) ? "" : state.Title!;

            if (bound)
            {
                string name = title.Length > 0 ? title : id.ToString();

                if (!willPublish)
                    return new MapStatus(name, $"Bound to item {id}. Publishing is off for this map.",
                        StatusSeverity.Ok, Confirm: false);

                string detail = $"Replaces \"{name}\" (item {id}) on the Workshop, for everyone subscribed to it.";

                if (state.EntitiesStripped)
                    detail += " Servers running it will need the new .lmp beside the map.";

                return new MapStatus(name, detail, StatusSeverity.Info, Confirm: true);
            }

            if (!willPublish)
                return new MapStatus("not bound", "No Workshop item, and publishing is off for this map.",
                    StatusSeverity.Ok, Confirm: false);

            if (mayCreate)
                return new MapStatus("will create a new item",
                    title.Length > 0
                        ? $"Creates a new Workshop item called \"{title}\"."
                        : "Creates a new Workshop item.",
                    StatusSeverity.Warn, Confirm: true);

            return new MapStatus("not bound",
                "This map is set to publish, but no Workshop item is bound to it and creating one is not " +
                "allowed - so this compile would finish and publish nothing. Press Workshop on the step to " +
                "bind it, or turn publishing off for this map.",
                StatusSeverity.Blocking, Confirm: true);
        }

        /// <summary>
        /// Whether a flag is present in an argument string.
        ///
        /// Word boundaries on both sides, so -publish is not found inside -publishnothing and a path
        /// that happens to contain the text does not count as the switch being set.
        /// </summary>
        private static bool HasFlag(string args, string flag)
        {
            int at = 0;

            while ((at = args.IndexOf(flag, at, StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                bool startsClean = at == 0 || char.IsWhiteSpace(args[at - 1]);
                int after = at + flag.Length;
                bool endsClean = after >= args.Length || char.IsWhiteSpace(args[after]);

                if (startsClean && endsClean)
                    return true;

                at = after;
            }

            return false;
        }
    }
}

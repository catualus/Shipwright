using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Shipwright
{
    /// <summary>
    /// The record of which Workshop item a map belongs to.
    ///
    /// WHY THIS IS A FILE AND NOT A SEARCH
    ///
    /// "Update the existing item, or create a new one" is the sentence that describes this tool, and
    /// it is also its most dangerous sentence, because the obvious way to decide is to look through
    /// the account's published items for one with a matching name. Titles are not unique, an old
    /// item for a map that was renamed still carries the old name, and the failure mode is silently
    /// replacing an unrelated published map with this one - for every subscriber, at once, with no
    /// undo.
    ///
    /// So the binding is explicit and local: a file next to the map source, holding the ID that this
    /// map has been published under before. No ID, no update. Nothing is ever inferred from a title.
    ///
    /// It sits next to the .vmf rather than in the plugin folder because it belongs to the map, not
    /// to the installation: it survives reinstalling Compile Pal, it travels with the map to another
    /// machine, and it is a text file the user can read and correct.
    /// </summary>
    public sealed class WorkshopState
    {
        [JsonPropertyName("workshopId")]
        public string? WorkshopId { get; set; }

        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("lastPublished")]
        public DateTimeOffset? LastPublished { get; set; }

        /// <summary>
        /// The map revision of the BSP that was last published.
        ///
        /// Recorded because of what it means for a stripped map: a server's .lmp is only accepted
        /// beside a BSP of the same revision, so this number is the answer to "do the servers need
        /// the new lump file" - and the answer is yes every time this changes.
        /// </summary>
        [JsonPropertyName("bspRevision")]
        public int BspRevision { get; set; }

        /// <summary>Whether the last publish shipped a map whose entities had been moved out.</summary>
        [JsonPropertyName("entitiesStripped")]
        public bool EntitiesStripped { get; set; }

        /// <summary>
        /// SHA-256 of the .gma that was last uploaded, so an unchanged map does not become an update.
        ///
        /// A Workshop update is not free for anyone: every subscriber redownloads the addon, and
        /// every server that mounts it does too. Recompiling a map with no changes still produces a
        /// different BSP - timestamps and revision move - so this is a hash of the packed result,
        /// which is the thing that actually decides whether subscribers would receive anything new.
        /// </summary>
        [JsonPropertyName("gmaSha256")]
        public string? GmaSha256 { get; set; }

        /// <summary>
        /// What a new item should be created as, when no ID is bound yet.
        ///
        /// These three are per map, which is exactly why they moved out of the compile parameters:
        /// a parameter belongs to the preset, the preset applies to every map in the queue, and
        /// "the title of the item" is not something two maps can share. The parameters still exist
        /// and still win when they are set, for anyone driving the tool from a script.
        /// </summary>
        [JsonPropertyName("tags")]
        public string[]? Tags { get; set; }

        [JsonPropertyName("iconPath")]
        public string? IconPath { get; set; }

        private static readonly JsonSerializerOptions Options = new()
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        /// <summary>The state file for a map: mapname.workshop.json, beside the map.</summary>
        public static string PathFor(string mapPath) =>
            Path.Combine(
                Path.GetDirectoryName(mapPath) ?? ".",
                Path.GetFileNameWithoutExtension(mapPath) + ".workshop.json");

        /// <summary>
        /// Reads the state file, or returns an empty one.
        ///
        /// A malformed file is reported and then treated as absent, which means "no ID" - so the run
        /// declines to publish rather than falling back to any other way of choosing an item.
        /// </summary>
        public static WorkshopState Load(string statePath)
        {
            if (!File.Exists(statePath))
                return new WorkshopState();

            try
            {
                var loaded = JsonSerializer.Deserialize<WorkshopState>(File.ReadAllText(statePath), Options);

                if (loaded is null)
                    return new WorkshopState();

                if (loaded.WorkshopId != null && !Sanitize.IsWorkshopId(loaded.WorkshopId, out _))
                {
                    Log.Warn($"{Path.GetFileName(statePath)} holds \"{Sanitize.Title(loaded.WorkshopId)}\", which is not a Workshop ID. Ignoring it.");
                    loaded.WorkshopId = null;
                }

                return loaded;
            }
            catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
            {
                Log.Warn($"could not read {Path.GetFileName(statePath)}: {e.Message}");
                return new WorkshopState();
            }
        }

        /// <summary>
        /// Writes the state file through a temporary and a rename.
        ///
        /// The write that matters is the one immediately after a new item is created, and the thing
        /// that must not happen is losing the ID of an item that now exists. Compile Pal cancels a
        /// step by killing the process, so a plain write can be interrupted halfway; a rename cannot
        /// leave a half written file in its place.
        /// </summary>
        public void Save(string statePath)
        {
            string directory = Path.GetDirectoryName(statePath) ?? ".";
            Directory.CreateDirectory(directory);

            string temp = Path.Combine(directory, Path.GetFileName(statePath) + ".tmp");

            File.WriteAllText(temp, JsonSerializer.Serialize(this, Options));
            File.Move(temp, statePath, overwrite: true);
        }
    }
}

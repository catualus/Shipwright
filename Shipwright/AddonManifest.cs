using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace Shipwright
{
    /// <summary>
    /// Writes the addon.json that goes in the staging directory.
    ///
    /// The fields gmad reads are title, type, tags and ignore. Everything else about a Workshop item
    /// - description, images, visibility - is set on the website and cannot be set from here, which
    /// is worth knowing before wondering where the description parameter went.
    ///
    /// Type and tags are validated rather than passed through. gmad rejects an unknown type with
    /// "Addon has invalid type!" after the packing work is done, and rejects nothing at all for a
    /// misspelled tag - the tag is simply absent from the item afterwards.
    /// </summary>
    public static class AddonManifest
    {
        /// <summary>The only type this tool writes. It publishes maps.</summary>
        public const string MapType = "map";

        /// <summary>
        /// The tags gmad accepts. Two at most, which is Steam's limit for a Garry's Mod addon.
        /// </summary>
        public static readonly string[] AllowedTags =
        {
            "fun", "roleplay", "scenic", "movie", "realism", "cartoon", "water", "comic", "build",
        };

        public const int MaxTags = 2;

        public static string Build(string title, IEnumerable<string> tags)
        {
            string cleanTitle = Sanitize.Title(title);
            if (cleanTitle.Length == 0)
                throw new ArgumentException("An addon needs a title.", nameof(title));

            var accepted = new List<string>();
            foreach (string tag in tags)
            {
                string lower = tag.Trim().ToLowerInvariant();

                if (lower.Length == 0)
                    continue;

                if (!AllowedTags.Contains(lower))
                {
                    Log.Warn($"ignoring the tag \"{Sanitize.Title(tag)}\" - gmad only accepts: {string.Join(", ", AllowedTags)}");
                    continue;
                }

                if (accepted.Contains(lower))
                    continue;

                if (accepted.Count == MaxTags)
                {
                    Log.Warn($"ignoring the tag \"{lower}\" - an addon may carry {MaxTags}");
                    continue;
                }

                accepted.Add(lower);
            }

            var buffer = new System.IO.MemoryStream();
            using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
            {
                writer.WriteStartObject();
                writer.WriteString("title", cleanTitle);
                writer.WriteString("type", MapType);

                writer.WriteStartArray("tags");
                foreach (string tag in accepted)
                    writer.WriteStringValue(tag);
                writer.WriteEndArray();

                /*
                 * Empty, and deliberately so. An ignore list is how you pack a folder that contains
                 * things you do not want packed - and the staging directory contains nothing that
                 * was not chosen, so there is nothing here to ignore. If this list ever needs an
                 * entry, something upstream has put a file in the addon that should not be there.
                 */
                writer.WriteStartArray("ignore");
                writer.WriteEndArray();

                writer.WriteEndObject();
            }

            return Encoding.UTF8.GetString(buffer.ToArray());
        }
    }
}

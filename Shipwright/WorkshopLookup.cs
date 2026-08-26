using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Shipwright
{
    /// <summary>What Steam says publicly about an item, or why it could not be asked.</summary>
    public sealed record ItemDetails(
        bool Found,
        string Message,
        string Title = "",
        int ConsumerAppId = 0,
        DateTimeOffset? Updated = null,
        long SizeBytes = 0,
        string PreviewUrl = "",
        string Creator = "");

    /// <summary>
    /// Looks up a Workshop item before overwriting it.
    ///
    /// WHAT THIS IS FOR
    ///
    /// The state file says which item to update. It is a text file on the user's disk, and it can be
    /// wrong - copied from another map's folder, edited by hand, or carried over from a map that was
    /// renamed. An update is irreversible for everyone subscribed to whatever item the ID actually
    /// names, so before pushing anything the run prints what is at the other end: the title, the app
    /// it belongs to, and when it was last updated. Someone about to overwrite the wrong map sees
    /// the wrong map's name.
    ///
    /// WHY THIS ENDPOINT
    ///
    /// ISteamRemoteStorage/GetPublishedFileDetails takes no API key. A key would have to come from
    /// somewhere - embedded in the plugin, where it belongs to whoever built it, or entered by the
    /// user, at which point this is a tool that asks for Steam credentials. Neither is worth a
    /// nicer response body.
    ///
    /// It returns only what is already public. Nothing about the signed-in account is sent, and the
    /// only thing transmitted is an item ID the user supplied.
    /// </summary>
    public static class WorkshopLookup
    {
        private const string Endpoint = "https://api.steampowered.com/ISteamRemoteStorage/GetPublishedFileDetails/v1/";

        /// <summary>Garry's Mod. An item belonging to any other app is not this tool's to touch.</summary>
        public const int GarrysModAppId = 4000;

        public static ItemDetails Describe(ulong id, TimeSpan timeout)
        {
            try
            {
                return DescribeAsync(id, timeout).GetAwaiter().GetResult();
            }
            catch (Exception e)
            {
                /*
                 * Every exception, and deliberately. This is a courtesy call - it exists so someone
                 * can see the name of the item they are about to replace - and the publish that
                 * follows does not depend on it. A response shaped differently than expected should
                 * cost the title in the log, not the run.
                 */
                return new ItemDetails(false, $"could not be looked up ({e.GetType().Name}). Working offline.");
            }
        }

        private static async Task<ItemDetails> DescribeAsync(ulong id, TimeSpan timeout)
        {
            using var client = new HttpClient { Timeout = timeout };
            using var cancel = new CancellationTokenSource(timeout);

            var form = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("itemcount", "1"),
                new KeyValuePair<string, string>("publishedfileids[0]", id.ToString()),
            });

            using var response = await client.PostAsync(Endpoint, form, cancel.Token).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
                return new ItemDetails(false, $"lookup returned HTTP {(int)response.StatusCode}.");

            string body = await response.Content.ReadAsStringAsync(cancel.Token).ConfigureAwait(false);

            return ParseResponse(body);
        }

        /// <summary>
        /// Reads one item out of a GetPublishedFileDetails response.
        ///
        /// Separated from the request so it can be tested against the shapes this endpoint actually
        /// returns, which is not the shape its field names suggest - see <see cref="Number"/>.
        /// </summary>
        internal static ItemDetails ParseResponse(string body)
        {
            using var document = JsonDocument.Parse(body);

            if (!document.RootElement.TryGetProperty("response", out var wrapper)
                || !wrapper.TryGetProperty("publishedfiledetails", out var details)
                || details.GetArrayLength() == 0)
                return new ItemDetails(false, "lookup returned nothing for that ID.");

            var item = details[0];

            // result 1 is success. Anything else means the ID names nothing the public API will
            // describe - deleted, hidden, or never existed.
            if (Number(item, "result") is { } outcome && outcome != 1)
                return new ItemDetails(false, "no public item has that ID. It may be deleted, or private.");

            string title = item.TryGetProperty("title", out var t) ? (t.GetString() ?? "") : "";
            int appId = (int)(Number(item, "consumer_app_id") ?? 0);
            long size = Number(item, "file_size") ?? 0;

            DateTimeOffset? updated = null;
            if (Number(item, "time_updated") is { } epoch && epoch > 0)
                updated = DateTimeOffset.FromUnixTimeSeconds(epoch);

            /*
             * The preview image and the creator are here for the settings window, which shows both
             * before anything is bound: a picture and an owner are what make "this is not the item I
             * meant" obvious, in a way that a title alone does not when someone has five maps whose
             * names differ by one word.
             *
             * The creator is a number, not a name - putting a name to it needs a keyed endpoint, and
             * a key is the thing this tool does not have and does not want.
             */
            string preview = item.TryGetProperty("preview_url", out var p) ? (p.GetString() ?? "") : "";
            string creator = item.TryGetProperty("creator", out var c) ? (c.GetString() ?? "") : "";

            return new ItemDetails(true, "found.", Sanitize.Title(title), appId, updated, size,
                SafePreviewUrl(preview), creator);
        }

        /// <summary>
        /// Reads a number that may not have been sent as one.
        ///
        /// This endpoint is not consistent about it: file_size comes back as a JSON string - "1360"
        /// with the quotes - while consumer_app_id comes back as a bare number, and which is which
        /// is not documented anywhere. Asking a string element for an integer throws, so the first
        /// real item ever looked up failed with "the target element has type 'String'" and no clue
        /// as to which field it meant.
        ///
        /// Both shapes are read, and anything else is absent rather than fatal: none of these
        /// numbers is worth failing a lookup over when the title has already arrived.
        /// </summary>
        private static long? Number(JsonElement item, string property)
        {
            if (!item.TryGetProperty(property, out var value))
                return null;

            return value.ValueKind switch
            {
                JsonValueKind.Number when value.TryGetInt64(out long number) => number,
                JsonValueKind.String when long.TryParse(value.GetString(), out long parsed) => parsed,
                _ => null,
            };
        }

        /// <summary>
        /// Only an https URL on Steam's own image hosts is handed back for display.
        ///
        /// The response is data from the internet, and the window turns this into a network request
        /// of its own. An unchecked URL there is a plugin that fetches whatever a Workshop response
        /// names - so the scheme and the host are checked here, once, rather than at the call site.
        /// </summary>
        private static string SafePreviewUrl(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed))
                return "";

            if (parsed.Scheme != Uri.UriSchemeHttps)
                return "";

            string host = parsed.Host;

            bool steamImageHost =
                host.EndsWith(".steamstatic.com", StringComparison.OrdinalIgnoreCase) ||
                host.EndsWith(".akamaihd.net", StringComparison.OrdinalIgnoreCase) ||
                host.EndsWith(".steamusercontent.com", StringComparison.OrdinalIgnoreCase) ||
                host.EndsWith(".steampowered.com", StringComparison.OrdinalIgnoreCase);

            return steamImageHost ? parsed.AbsoluteUri : "";
        }
    }
}

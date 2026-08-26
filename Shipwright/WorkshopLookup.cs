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
        long SizeBytes = 0);

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
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException)
            {
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

            using var document = JsonDocument.Parse(body);

            if (!document.RootElement.TryGetProperty("response", out var wrapper)
                || !wrapper.TryGetProperty("publishedfiledetails", out var details)
                || details.GetArrayLength() == 0)
                return new ItemDetails(false, "lookup returned nothing for that ID.");

            var item = details[0];

            // result 1 is success. Anything else means the ID names nothing the public API will
            // describe - deleted, hidden, or never existed.
            if (item.TryGetProperty("result", out var result) && result.GetInt32() != 1)
                return new ItemDetails(false, "no public item has that ID. It may be deleted, or private.");

            string title = item.TryGetProperty("title", out var t) ? (t.GetString() ?? "") : "";
            int appId = item.TryGetProperty("consumer_app_id", out var a) ? a.GetInt32() : 0;
            long size = item.TryGetProperty("file_size", out var s) && s.TryGetInt64(out long parsed) ? parsed : 0;

            DateTimeOffset? updated = null;
            if (item.TryGetProperty("time_updated", out var u) && u.TryGetInt64(out long epoch) && epoch > 0)
                updated = DateTimeOffset.FromUnixTimeSeconds(epoch);

            return new ItemDetails(true, "found.", Sanitize.Title(title), appId, updated, size);
        }
    }
}

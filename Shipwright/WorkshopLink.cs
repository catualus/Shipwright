using System;
using System.Text.RegularExpressions;

namespace Shipwright
{
    /// <summary>
    /// Gets an item ID out of whatever someone pasted.
    ///
    /// The point of the settings window is that nobody types an ID. What they do instead is copy the
    /// address bar, and what lands in the box is one of half a dozen shapes: the full item URL, the
    /// same URL with a search term or a language on the end, the steam:// address the client copies,
    /// or - when they have done this before - the bare number.
    ///
    /// All of them are read here, and anything else is refused rather than guessed at. A paste that
    /// cannot be understood is a message in the window; a paste that is understood wrongly is
    /// somebody else's map replaced.
    /// </summary>
    public static class WorkshopLink
    {
        /// <summary>
        /// Matches the id parameter of a Workshop URL, or a bare run of digits.
        ///
        /// Anchored on <c>id=</c> rather than "the first long number in the string", because a
        /// Workshop URL can carry other numbers - <c>appid=4000</c>, a search filter, a comment
        /// anchor - and picking whichever came first would resolve to a different item without ever
        /// looking wrong.
        /// </summary>
        private static readonly Regex IdParameter =
            new(@"[?&]id=(\d{1,20})\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public static bool TryParse(string? text, out ulong id)
        {
            id = 0;

            if (string.IsNullOrWhiteSpace(text))
                return false;

            string trimmed = text!.Trim();

            if (Sanitize.IsWorkshopId(trimmed, out id))
                return true;

            var match = IdParameter.Match(trimmed);

            return match.Success && Sanitize.IsWorkshopId(match.Groups[1].Value, out id);
        }

        /// <summary>The page for an item, for the window to link to and the log to print.</summary>
        public static string UrlFor(ulong id) =>
            $"https://steamcommunity.com/sharedfiles/filedetails/?id={id}";
    }
}

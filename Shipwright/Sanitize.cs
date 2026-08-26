using System;
using System.Text;

namespace Shipwright
{
    /// <summary>
    /// Cleans the free text that reaches a command line or a Workshop page.
    ///
    /// Two separate reasons, and both matter.
    ///
    /// The first is that Compile Pal builds a step's command line by string concatenation and does
    /// not quote a parameter's value unless the parameter is declared to be a file path. A change
    /// note containing a quote therefore re-splits the arguments of the process it is passed to, and
    /// the argument after the split can land on a flag that was meant to be something else - which
    /// for a tool whose flags include the ID of the item being overwritten is not a cosmetic
    /// problem. This tool builds its own child command lines from an argument array, so the exposure
    /// is only on the host-to-plugin hop, but the text arrives here already split and has to be
    /// reassembled into something predictable.
    ///
    /// The second is that gmpublish asks for US-ASCII change notes: anything else and the note is
    /// silently dropped from the item's page.
    /// </summary>
    public static class Sanitize
    {
        /// <summary>Steam truncates well before this; the cap is here so a runaway file cannot be one.</summary>
        public const int MaxChangeNote = 4000;

        /// <summary>Workshop titles are short. Longer than this is a mistake, not a title.</summary>
        public const int MaxTitle = 128;

        /// <summary>
        /// Printable US-ASCII only, no quotes, collapsed whitespace, capped.
        ///
        /// Quotes are removed rather than escaped. Escaping is correct in exactly one quoting
        /// dialect and wrong in the others, and no change note needs a quote badly enough to be
        /// worth getting that wrong.
        /// </summary>
        public static string PlainText(string? text, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(text))
                return "";

            var builder = new StringBuilder(text!.Length);
            bool lastWasSpace = false;

            foreach (char c in text)
            {
                if (c == '"' || c == '\'' || c == '`')
                    continue;

                if (char.IsWhiteSpace(c))
                {
                    if (!lastWasSpace && builder.Length > 0)
                        builder.Append(' ');
                    lastWasSpace = true;
                    continue;
                }

                if (c < 0x20 || c > 0x7E)
                    continue;

                builder.Append(c);
                lastWasSpace = false;
            }

            string result = builder.ToString().TrimEnd();

            return result.Length <= maxLength ? result : result.Substring(0, maxLength).TrimEnd();
        }

        public static string ChangeNote(string? text) => PlainText(text, MaxChangeNote);

        /// <summary>
        /// Text that is only ever shown or written to a JSON file, cleaned but not flattened to ASCII.
        ///
        /// Workshop titles belong to whoever wrote them, and plenty of them are not English. Reducing
        /// them the way a change note is reduced turns a Cyrillic map name into an empty string,
        /// which is worse than useless in a list someone is picking from. What still goes is what
        /// makes text dangerous rather than foreign: control characters, and the quotes that would
        /// end an argument early if this ever did reach a command line.
        /// </summary>
        public static string DisplayText(string? text, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(text))
                return "";

            var builder = new StringBuilder(text!.Length);
            bool lastWasSpace = false;

            foreach (char c in text)
            {
                if (c == '"' || c == '\'' || c == '`')
                    continue;

                if (char.IsWhiteSpace(c))
                {
                    if (!lastWasSpace && builder.Length > 0)
                        builder.Append(' ');
                    lastWasSpace = true;
                    continue;
                }

                if (char.IsControl(c))
                    continue;

                builder.Append(c);
                lastWasSpace = false;
            }

            string result = builder.ToString().TrimEnd();

            return result.Length <= maxLength ? result : result.Substring(0, maxLength).TrimEnd();
        }

        /// <summary>
        /// A Workshop item's title, as shown and as written to the state file.
        ///
        /// Not the strict ASCII treatment: this never becomes a command line argument - gmpublish
        /// takes the title from the addon's own addon.json, which is UTF-8 - and a title stripped to
        /// nothing helps nobody.
        /// </summary>
        public static string Title(string? text) => DisplayText(text, MaxTitle);

        /// <summary>
        /// A Workshop file ID as Steam issues them: decimal, unsigned, inside 64 bits. Anything else
        /// is not a near miss to be corrected - it is a mistyped ID pointing at somebody's item.
        /// </summary>
        public static bool IsWorkshopId(string? text, out ulong id)
        {
            id = 0;

            if (string.IsNullOrWhiteSpace(text))
                return false;

            string trimmed = text!.Trim();

            foreach (char c in trimmed)
                if (c < '0' || c > '9')
                    return false;

            return ulong.TryParse(trimmed, out id) && id != 0;
        }
    }
}

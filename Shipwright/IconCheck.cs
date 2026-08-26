using System;
using System.IO;

namespace Shipwright
{
    public sealed record IconVerdict(bool Acceptable, string Message, int Width = 0, int Height = 0);

    /// <summary>
    /// Checks a Workshop icon before gmpublish gets it.
    ///
    /// gmpublish wants a 512x512 baseline JPEG with 4:2:0 chroma, and what it does with anything
    /// else is fail after the upload has started, with a number: "PublishWorkshopFile failed! (9)".
    /// Since the icon is only required when creating a new item, that failure lands on the one path
    /// where a partial result is worst - a new item that may or may not exist, with an ID this tool
    /// never saw.
    ///
    /// So the file is parsed here first, and the message says which of the three requirements it
    /// missed. This reads the JPEG marker segments directly rather than decoding the image: the
    /// three facts needed are all in the frame header, and decoding a picture to learn its size
    /// would mean carrying an image library into a plugin folder.
    /// </summary>
    public static class IconCheck
    {
        public const int RequiredSize = 512;

        /// <summary>Steam's own preview image limit. Bigger than this is refused upstream.</summary>
        public const long MaxBytes = 1024 * 1024;

        public static IconVerdict Inspect(string path)
        {
            byte[] bytes;
            try
            {
                bytes = File.ReadAllBytes(path);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                return new IconVerdict(false, $"could not be read: {e.Message}");
            }

            if (bytes.Length < 4 || bytes[0] != 0xFF || bytes[1] != 0xD8)
                return new IconVerdict(false, "is not a JPEG. A PNG renamed to .jpg is the usual cause.");

            if (bytes.Length > MaxBytes)
                return new IconVerdict(false, $"is {bytes.Length / 1024:N0} KB; the limit is {MaxBytes / 1024:N0} KB.");

            int i = 2;
            while (i + 3 < bytes.Length)
            {
                if (bytes[i] != 0xFF)
                {
                    i++;        // padding between segments is legal and common
                    continue;
                }

                byte marker = bytes[i + 1];
                i += 2;

                // Standalone markers, carrying no length field.
                if (marker == 0xD8 || marker == 0x01 || (marker >= 0xD0 && marker <= 0xD7))
                    continue;

                if (marker == 0xD9 || marker == 0xDA)
                    break;      // end of image, or the start of entropy coded data

                if (i + 1 >= bytes.Length)
                    break;

                int length = (bytes[i] << 8) | bytes[i + 1];
                if (length < 2 || i + length > bytes.Length)
                    return new IconVerdict(false, "is a JPEG with a damaged segment header.");

                bool isFrameHeader = marker >= 0xC0 && marker <= 0xCF
                                     && marker != 0xC4 && marker != 0xC8 && marker != 0xCC;

                if (isFrameHeader)
                    return InspectFrame(bytes, i, marker);

                i += length;
            }

            return new IconVerdict(false, "is a JPEG with no frame header, so its size cannot be read.");
        }

        /// <summary>
        /// Reads a SOFn segment: precision, height, width, component count, then three bytes per
        /// component of which the second holds the sampling factors as two nibbles.
        /// </summary>
        private static IconVerdict InspectFrame(byte[] bytes, int offset, byte marker)
        {
            int height = (bytes[offset + 3] << 8) | bytes[offset + 4];
            int width = (bytes[offset + 5] << 8) | bytes[offset + 6];
            int components = bytes[offset + 7];

            if (marker != 0xC0 && marker != 0xC1)
            {
                string kind = marker == 0xC2 ? "progressive" : $"an unsupported JPEG mode (SOF{marker - 0xC0:X})";
                return new IconVerdict(false, $"is {kind}. gmpublish needs a baseline JPEG.", width, height);
            }

            if (width != RequiredSize || height != RequiredSize)
                return new IconVerdict(false, $"is {width}x{height}. It has to be {RequiredSize}x{RequiredSize}.", width, height);

            if (components != 3)
                return new IconVerdict(false, $"has {components} colour components. A greyscale or CMYK JPEG is not accepted.", width, height);

            int luma = bytes[offset + 9];
            int hSampling = luma >> 4;
            int vSampling = luma & 0x0F;

            if (hSampling != 2 || vSampling != 2)
                return new IconVerdict(false,
                    $"has {hSampling}x{vSampling} luma sampling, which is 4:4:4 or 4:2:2 chroma. gmpublish needs 4:2:0 - " +
                    "re-exporting from Paint is the usual fix.", width, height);

            return new IconVerdict(true, "512x512 baseline JPEG, 4:2:0.", width, height);
        }
    }
}

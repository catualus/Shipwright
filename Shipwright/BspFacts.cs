using System;
using System.IO;

namespace Shipwright
{
    /// <summary>How the entity lump of a compiled map currently looks.</summary>
    public enum EntityLumpState
    {
        /// <summary>Entities are still in the BSP. Nothing has been moved out.</summary>
        Present,

        /// <summary>Worldspawn and nothing else, which is what Compile Pal's ENTLUMP step leaves.</summary>
        WorldspawnOnly,

        /// <summary>Empty, which is ENTLUMP with "Remove Worldspawn" set.</summary>
        Empty,
    }

    /// <summary>What could be read out of a BSP header, or why it could not.</summary>
    public sealed record BspFacts(
        bool Readable,
        string Message,
        int Version = 0,
        int MapRevision = 0,
        int EntityLumpLength = 0,
        int EntityCount = 0,
        EntityLumpState LumpState = EntityLumpState.Present)
    {
        public bool EntitiesStripped => LumpState != EntityLumpState.Present;
    }

    /// <summary>
    /// Reads the few facts about a compiled map that decide whether it is safe to publish and what
    /// has to travel with it.
    ///
    /// The header layout is the one Compile Pal's own EntityLumpExtractor works against, and the
    /// constants are repeated here rather than shared because this is a separate program: a plugin
    /// that had to be rebuilt in step with the host would be the wrong shape for something installed
    /// by copying a folder.
    ///
    /// Only the header and the entity lump are read. A packed BSP is hundreds of megabytes and
    /// nothing here needs any of it.
    /// </summary>
    public static class BspReader
    {
        /// <summary>'VBSP' as a little-endian int, which is what sits at offset 0 of every BSP.</summary>
        private const int VbspIdent = 0x50534256;

        private const int LumpCount = 64;
        private const int LumpEntryBytes = 16;
        private const int LumpTableOffset = 8;
        private const int MapRevisionOffset = LumpTableOffset + (LumpCount * LumpEntryBytes);   // 1032
        private const int HeaderBytes = MapRevisionOffset + 4;                                  // 1036

        private const int EntityLumpIndex = 0;

        /// <summary>
        /// Above this, the lump is read for its length alone and not parsed.
        ///
        /// The question this class asks of the entity lump is only ever "is it down to worldspawn",
        /// and a lump of half a megabyte answers that by its size. Reading a 20 MB entity lump to
        /// count braces in it would be work done to learn something already known.
        /// </summary>
        private const int MaxParsedLumpBytes = 512 * 1024;

        public static BspFacts Read(string bspPath)
        {
            FileStream file;
            try
            {
                file = new FileStream(bspPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                return new BspFacts(false, $"Could not read {Path.GetFileName(bspPath)}: {e.Message}");
            }

            using (file)
            {
                if (file.Length < HeaderBytes)
                    return new BspFacts(false, "File is too small to be a BSP.");

                var header = new byte[HeaderBytes];
                if (!ReadExactly(file, 0, header, header.Length))
                    return new BspFacts(false, "The BSP header could not be read.");

                if (BitConverter.ToInt32(header, 0) != VbspIdent)
                    return new BspFacts(false, "Not a VBSP file.");

                int version = BitConverter.ToInt32(header, 4);

                /*
                 * Left 4 Dead 2 reorders the fields inside each lump entry to version/offset/length
                 * rather than offset/length/version. Detected the way Compile Pal detects it: on
                 * that branch the first field of lump 0 is the lump version, which is 0.
                 *
                 * Kept even though this tool only publishes for Garry's Mod, because reading the
                 * wrong two fields of an L4D2 map would not fail - it would report a plausible
                 * looking length for a lump that is somewhere else entirely.
                 */
                bool isL4D2 = version == 21 && BitConverter.ToInt32(header, LumpTableOffset) == 0;

                int entry = LumpTableOffset + (EntityLumpIndex * LumpEntryBytes);
                int lumpOffset = BitConverter.ToInt32(header, isL4D2 ? entry + 4 : entry);
                int lumpLength = BitConverter.ToInt32(header, isL4D2 ? entry + 8 : entry + 4);
                int mapRevision = BitConverter.ToInt32(header, MapRevisionOffset);

                if (lumpLength < 0 || lumpOffset < 0 || lumpOffset + (long)lumpLength > file.Length)
                    return new BspFacts(false, "The entity lump header is out of range.");

                if (lumpLength == 0)
                    return new BspFacts(true, "Read.", version, mapRevision, 0, 0, EntityLumpState.Empty);

                if (lumpLength > MaxParsedLumpBytes)
                    return new BspFacts(true, "Read.", version, mapRevision, lumpLength, -1, EntityLumpState.Present);

                var lump = new byte[lumpLength];
                if (!ReadExactly(file, lumpOffset, lump, lumpLength))
                    return new BspFacts(false, "The entity lump could not be read.");

                int entities = CountEntities(lump);
                var state = entities switch
                {
                    0 => EntityLumpState.Empty,
                    1 when IsWorldspawnOnly(lump) => EntityLumpState.WorldspawnOnly,
                    _ => EntityLumpState.Present,
                };

                return new BspFacts(true, "Read.", version, mapRevision, lumpLength, entities, state);
            }
        }

        /// <summary>The map revision, or null if the file could not be read as a BSP.</summary>
        public static int? Revision(string bspPath)
        {
            var facts = Read(bspPath);
            return facts.Readable ? facts.MapRevision : null;
        }

        /// <summary>
        /// Counts top level entity blocks. Braces inside a key or a value are not counted, which
        /// matters more than it sounds: an entity carrying a piece of Lua or a formatted message in
        /// a keyvalue is ordinary, and counting its braces would report a map as full of entities
        /// after every one of them had been moved out.
        /// </summary>
        private static int CountEntities(byte[] lump)
        {
            int count = 0;
            bool inString = false;

            foreach (byte b in lump)
            {
                if (b == (byte)'"')
                    inString = !inString;
                else if (b == (byte)'{' && !inString)
                    count++;
            }

            return count;
        }

        private static bool IsWorldspawnOnly(byte[] lump)
        {
            string text = System.Text.Encoding.ASCII.GetString(lump);
            return text.Contains("\"worldspawn\"", StringComparison.Ordinal);
        }

        private static bool ReadExactly(FileStream file, long offset, byte[] buffer, int count)
        {
            file.Seek(offset, SeekOrigin.Begin);

            int read = 0;
            while (read < count)
            {
                int got = file.Read(buffer, read, count - read);
                if (got <= 0)
                    return false;
                read += got;
            }

            return true;
        }
    }
}

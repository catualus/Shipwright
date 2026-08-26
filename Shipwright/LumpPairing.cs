using System;
using System.IO;

namespace Shipwright
{
    /// <summary>Why a lump file beside a map is or is not the one that belongs to it.</summary>
    public enum LumpPairingState
    {
        /// <summary>No .lmp beside the BSP. Either nothing was extracted, or it is somewhere else.</summary>
        Missing,

        /// <summary>Present, and its map revision matches the BSP.</summary>
        Matched,

        /// <summary>Present, and its revision is from a different compile. The engine will ignore it.</summary>
        Stale,

        /// <summary>Present and unreadable.</summary>
        Unreadable,
    }

    public sealed record LumpPairing(LumpPairingState State, string? Path, int LumpRevision, int BspRevision)
    {
        public bool ShippableWithMap => State == LumpPairingState.Matched;
    }

    /// <summary>
    /// Finds the entity lump override that belongs to a map, and says whether it really belongs to
    /// it.
    ///
    /// The engine loads maps/name_l_0.lmp in place of the BSP's own entity lump when one is there
    /// and its map revision matches. The revision is the whole safety mechanism: it is what stops a
    /// lump file from a previous compile being applied to a recompiled map. It is also the thing
    /// that makes publishing a stripped map to the Workshop consequential, because a Workshop update
    /// pushes a new BSP - and therefore a new revision - to every subscriber within minutes, while
    /// the .lmp sitting on a server is whatever was copied there by hand.
    ///
    /// So this reports the revision on both sides every time, and refuses to treat a mismatched pair
    /// as a pair.
    /// </summary>
    public static class LumpFiles
    {
        /// <summary>lumpfileheader_t: lumpOffset, lumpID, lumpVersion, lumpLength, mapRevision.</summary>
        private const int LumpFileHeaderBytes = 20;

        private const int EntityLumpIndex = 0;

        /// <summary>The name the engine looks for beside a map: mapname_l_0.lmp.</summary>
        public static string PathFor(string bspPath) =>
            System.IO.Path.Combine(
                System.IO.Path.GetDirectoryName(bspPath) ?? ".",
                $"{System.IO.Path.GetFileNameWithoutExtension(bspPath)}_l_{EntityLumpIndex}.lmp");

        public static LumpPairing Inspect(string bspPath)
        {
            int bspRevision = BspReader.Revision(bspPath) ?? 0;
            string lumpPath = PathFor(bspPath);

            if (!File.Exists(lumpPath))
                return new LumpPairing(LumpPairingState.Missing, null, 0, bspRevision);

            int? lumpRevision = RevisionOf(lumpPath);

            if (lumpRevision is null)
                return new LumpPairing(LumpPairingState.Unreadable, lumpPath, 0, bspRevision);

            return lumpRevision.Value == bspRevision
                ? new LumpPairing(LumpPairingState.Matched, lumpPath, lumpRevision.Value, bspRevision)
                : new LumpPairing(LumpPairingState.Stale, lumpPath, lumpRevision.Value, bspRevision);
        }

        /// <summary>The map revision a lump file claims to belong to, or null if it is not one.</summary>
        public static int? RevisionOf(string lumpPath)
        {
            try
            {
                using var file = new FileStream(lumpPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

                if (file.Length < LumpFileHeaderBytes)
                    return null;

                var header = new byte[LumpFileHeaderBytes];
                int read = 0;
                while (read < header.Length)
                {
                    int got = file.Read(header, read, header.Length - read);
                    if (got <= 0)
                        return null;
                    read += got;
                }

                int lumpId = BitConverter.ToInt32(header, 4);
                if (lumpId != EntityLumpIndex)
                    return null;

                return BitConverter.ToInt32(header, 16);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }
    }
}

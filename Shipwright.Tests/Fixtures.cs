using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

// The tests share static state - Log.Sink, the current directory - and none of them are slow enough
// for parallelism to be worth the interleaving.
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]

namespace Shipwright.Tests
{
    /// <summary>
    /// Builds the files the tool reads: a BSP, a lump file beside it, and a JPEG icon.
    ///
    /// Synthesised rather than checked in. A real map is tens of megabytes and is not ours to
    /// redistribute, and every fact these tests care about lives in a header that is a few dozen
    /// bytes long - so the fixture writes exactly those bytes and the tests can ask for a map with
    /// any revision, any entity lump and any state.
    /// </summary>
    internal static class Fixtures
    {
        private const int VbspIdent = 0x50534256;
        private const int LumpCount = 64;
        private const int LumpEntryBytes = 16;
        private const int LumpTableOffset = 8;
        private const int MapRevisionOffset = LumpTableOffset + (LumpCount * LumpEntryBytes);
        private const int HeaderBytes = MapRevisionOffset + 4;

        /// <summary>A map whose entity lump holds exactly the text given.</summary>
        public static void WriteBsp(string path, string entities, int revision = 1765, int version = 20)
        {
            byte[] lump = Encoding.ASCII.GetBytes(entities);
            var file = new byte[HeaderBytes + lump.Length];

            Write(file, 0, VbspIdent);
            Write(file, 4, version);

            int entry = LumpTableOffset;
            Write(file, entry, HeaderBytes);        // offset
            Write(file, entry + 4, lump.Length);    // length
            Write(file, entry + 8, 0);              // version
            Write(file, entry + 12, 0);             // fourCC

            Write(file, MapRevisionOffset, revision);

            Array.Copy(lump, 0, file, HeaderBytes, lump.Length);

            File.WriteAllBytes(path, file);
        }

        /// <summary>Three ordinary entities, one of them carrying braces inside a keyvalue.</summary>
        public const string SampleEntities =
            "{\n\"classname\" \"worldspawn\"\n\"skyname\" \"sky_day01_01\"\n}\n" +
            "{\n\"classname\" \"info_player_start\"\n\"origin\" \"0 0 64\"\n}\n" +
            "{\n\"classname\" \"logic_auto\"\n\"OnMapSpawn\" \"lua run {print(1)}\"\n}\n";

        public const string WorldspawnOnly =
            "{\n\"classname\" \"worldspawn\"\n\"skyname\" \"sky_day01_01\"\n}\n";

        /// <summary>The lump override the engine reads in place of the BSP's own entity lump.</summary>
        public static void WriteLumpFile(string path, string entities, int revision)
        {
            byte[] text = Encoding.ASCII.GetBytes(entities);
            var file = new byte[20 + text.Length];

            Write(file, 0, 20);              // lumpOffset
            Write(file, 4, 0);               // lumpID: entities
            Write(file, 8, 0);               // lumpVersion
            Write(file, 12, text.Length);    // lumpLength
            Write(file, 16, revision);       // mapRevision

            Array.Copy(text, 0, file, 20, text.Length);

            File.WriteAllBytes(path, file);
        }

        /// <summary>
        /// A JPEG carrying only the markers <see cref="IconCheck"/> reads: SOI, a frame header, and
        /// enough of a component list to state the sampling factors.
        /// </summary>
        public static void WriteJpeg(string path, int width, int height, byte sofMarker = 0xC0,
            byte lumaSampling = 0x22, int components = 3)
        {
            var bytes = new List<byte> { 0xFF, 0xD8 };

            int length = 8 + (components * 3);

            bytes.Add(0xFF);
            bytes.Add(sofMarker);
            bytes.Add((byte)(length >> 8));
            bytes.Add((byte)(length & 0xFF));
            bytes.Add(8);                              // precision
            bytes.Add((byte)(height >> 8));
            bytes.Add((byte)(height & 0xFF));
            bytes.Add((byte)(width >> 8));
            bytes.Add((byte)(width & 0xFF));
            bytes.Add((byte)components);

            for (int i = 0; i < components; i++)
            {
                bytes.Add((byte)(i + 1));                          // component id
                bytes.Add(i == 0 ? lumaSampling : (byte)0x11);     // sampling factors
                bytes.Add(0);                                      // quantisation table
            }

            bytes.Add(0xFF);
            bytes.Add(0xD9);

            File.WriteAllBytes(path, bytes.ToArray());
        }

        private static void Write(byte[] buffer, int offset, int value) =>
            Array.Copy(BitConverter.GetBytes(value), 0, buffer, offset, 4);
    }

    /// <summary>A temporary directory that removes itself.</summary>
    internal sealed class TempDir : IDisposable
    {
        public string Path { get; }

        public TempDir()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "shipwright-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string File(string name) => System.IO.Path.Combine(Path, name);

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path))
                    Directory.Delete(Path, recursive: true);
            }
            catch (IOException) { }
        }
    }
}

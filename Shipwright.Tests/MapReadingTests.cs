using System;
using System.IO;
using Xunit;

namespace Shipwright.Tests
{
    public class BspReaderTests
    {
        [Fact]
        public void ReadsTheRevisionAndCountsEntities()
        {
            using var temp = new TempDir();
            string bsp = temp.File("gm_test.bsp");
            Fixtures.WriteBsp(bsp, Fixtures.SampleEntities, revision: 1765);

            var facts = BspReader.Read(bsp);

            Assert.True(facts.Readable);
            Assert.Equal(1765, facts.MapRevision);
            Assert.Equal(3, facts.EntityCount);
            Assert.Equal(EntityLumpState.Present, facts.LumpState);
            Assert.False(facts.EntitiesStripped);
        }

        [Fact]
        public void BracesInsideAKeyvalueAreNotEntities()
        {
            using var temp = new TempDir();
            string bsp = temp.File("gm_test.bsp");

            // The logic_auto in the sample carries "{print(1)}" in an output. Counting braces
            // blindly would report four entities and, worse, would report a stripped map as full.
            Fixtures.WriteBsp(bsp, Fixtures.SampleEntities);

            Assert.Equal(3, BspReader.Read(bsp).EntityCount);
        }

        [Fact]
        public void WorldspawnOnlyIsRecognisedAsStripped()
        {
            using var temp = new TempDir();
            string bsp = temp.File("gm_test.bsp");
            Fixtures.WriteBsp(bsp, Fixtures.WorldspawnOnly);

            var facts = BspReader.Read(bsp);

            Assert.Equal(EntityLumpState.WorldspawnOnly, facts.LumpState);
            Assert.True(facts.EntitiesStripped);
        }

        [Fact]
        public void AnEmptyLumpIsRecognisedAsStripped()
        {
            using var temp = new TempDir();
            string bsp = temp.File("gm_test.bsp");
            Fixtures.WriteBsp(bsp, "");

            var facts = BspReader.Read(bsp);

            Assert.Equal(EntityLumpState.Empty, facts.LumpState);
            Assert.True(facts.EntitiesStripped);
        }

        [Fact]
        public void SomethingThatIsNotABspIsRefused()
        {
            using var temp = new TempDir();
            string path = temp.File("not.bsp");
            File.WriteAllBytes(path, new byte[2000]);

            var facts = BspReader.Read(path);

            Assert.False(facts.Readable);
            Assert.Contains("VBSP", facts.Message);
        }

        [Fact]
        public void AFileTooSmallToBeABspIsRefused()
        {
            using var temp = new TempDir();
            string path = temp.File("tiny.bsp");
            File.WriteAllBytes(path, new byte[8]);

            Assert.False(BspReader.Read(path).Readable);
        }
    }

    public class LumpPairingTests
    {
        [Fact]
        public void APairFromTheSameCompileMatches()
        {
            using var temp = new TempDir();
            string bsp = temp.File("gm_test.bsp");

            Fixtures.WriteBsp(bsp, Fixtures.WorldspawnOnly, revision: 1765);
            Fixtures.WriteLumpFile(temp.File("gm_test_l_0.lmp"), Fixtures.SampleEntities, revision: 1765);

            var pairing = LumpFiles.Inspect(bsp);

            Assert.Equal(LumpPairingState.Matched, pairing.State);
            Assert.True(pairing.ShippableWithMap);
        }

        [Fact]
        public void ALumpFromAnEarlierCompileIsStaleNotAPair()
        {
            using var temp = new TempDir();
            string bsp = temp.File("gm_test.bsp");

            Fixtures.WriteBsp(bsp, Fixtures.WorldspawnOnly, revision: 1766);
            Fixtures.WriteLumpFile(temp.File("gm_test_l_0.lmp"), Fixtures.SampleEntities, revision: 1765);

            var pairing = LumpFiles.Inspect(bsp);

            Assert.Equal(LumpPairingState.Stale, pairing.State);
            Assert.False(pairing.ShippableWithMap);
            Assert.Equal(1765, pairing.LumpRevision);
            Assert.Equal(1766, pairing.BspRevision);
        }

        [Fact]
        public void NoLumpFileIsMissing()
        {
            using var temp = new TempDir();
            string bsp = temp.File("gm_test.bsp");
            Fixtures.WriteBsp(bsp, Fixtures.SampleEntities);

            Assert.Equal(LumpPairingState.Missing, LumpFiles.Inspect(bsp).State);
        }

        [Fact]
        public void GarbageWhereALumpFileShouldBeIsUnreadable()
        {
            using var temp = new TempDir();
            string bsp = temp.File("gm_test.bsp");

            Fixtures.WriteBsp(bsp, Fixtures.WorldspawnOnly);
            File.WriteAllText(temp.File("gm_test_l_0.lmp"), "not a lump file at all");

            Assert.Equal(LumpPairingState.Unreadable, LumpFiles.Inspect(bsp).State);
        }

        [Fact]
        public void TheLumpFileNameIsTheOneTheEngineLooksFor() =>
            Assert.EndsWith("gm_test_l_0.lmp", LumpFiles.PathFor(Path.Combine("C:", "maps", "gm_test.bsp")));
    }

    public class IconCheckTests
    {
        [Fact]
        public void AcceptsABaselineFiveTwelveSquare420Jpeg()
        {
            using var temp = new TempDir();
            string icon = temp.File("icon.jpg");
            Fixtures.WriteJpeg(icon, 512, 512);

            Assert.True(IconCheck.Inspect(icon).Acceptable);
        }

        [Fact]
        public void RejectsAPngWearingAJpgExtension()
        {
            using var temp = new TempDir();
            string icon = temp.File("icon.jpg");
            File.WriteAllBytes(icon, new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A });

            var verdict = IconCheck.Inspect(icon);

            Assert.False(verdict.Acceptable);
            Assert.Contains("PNG", verdict.Message);
        }

        [Fact]
        public void RejectsProgressive()
        {
            using var temp = new TempDir();
            string icon = temp.File("icon.jpg");
            Fixtures.WriteJpeg(icon, 512, 512, sofMarker: 0xC2);

            var verdict = IconCheck.Inspect(icon);

            Assert.False(verdict.Acceptable);
            Assert.Contains("progressive", verdict.Message);
        }

        [Fact]
        public void RejectsTheWrongSize()
        {
            using var temp = new TempDir();
            string icon = temp.File("icon.jpg");
            Fixtures.WriteJpeg(icon, 1024, 1024);

            var verdict = IconCheck.Inspect(icon);

            Assert.False(verdict.Acceptable);
            Assert.Contains("1024x1024", verdict.Message);
        }

        [Fact]
        public void RejectsFourFourFourChroma()
        {
            using var temp = new TempDir();
            string icon = temp.File("icon.jpg");
            Fixtures.WriteJpeg(icon, 512, 512, lumaSampling: 0x11);

            var verdict = IconCheck.Inspect(icon);

            Assert.False(verdict.Acceptable);
            Assert.Contains("4:2:0", verdict.Message);
        }

        [Fact]
        public void RejectsGreyscale()
        {
            using var temp = new TempDir();
            string icon = temp.File("icon.jpg");
            Fixtures.WriteJpeg(icon, 512, 512, components: 1);

            Assert.False(IconCheck.Inspect(icon).Acceptable);
        }
    }

    public class OptionsTests
    {
        [Fact]
        public void PublishingIsOffUnlessAskedFor()
        {
            var options = Options.Parse(new[] { "publish", "gm_test.bsp" }, 1);

            Assert.False(options.Publish);
            Assert.False(options.AllowCreate);
            Assert.False(options.IncludeLump);
        }

        [Fact]
        public void AnUnknownSwitchIsRefusedRatherThanIgnored() =>
            Assert.Throws<ArgumentException>(() =>
                Options.Parse(new[] { "publish", "gm_test.bsp", "-StaticPropPolys" }, 1));

        [Fact]
        public void TwoMapsIsAnUnquotedPath() =>
            Assert.Throws<ArgumentException>(() =>
                Options.Parse(new[] { "publish", "C:\\my", "maps\\gm_test.bsp" }, 1));

        [Fact]
        public void TheStateFileFollowsTheMapSource()
        {
            var options = Options.Parse(
                new[] { "publish", "gm_test.bsp", "-vmf", Path.Combine("C:", "mapsrc", "gm_test.vmf") }, 1);

            Assert.EndsWith("gm_test.workshop.json", options.StatePath);
            Assert.Contains("mapsrc", options.StatePath);
        }

        [Fact]
        public void TagsSplitOnCommas()
        {
            var options = Options.Parse(new[] { "publish", "gm_test.bsp", "-tags", "scenic,build" }, 1);

            Assert.Equal(new[] { "scenic", "build" }, options.Tags);
        }

        [Fact]
        public void AChangeNoteIsSanitisedOnTheWayOut()
        {
            var options = Options.Parse(
                new[] { "publish", "gm_test.bsp", "-changes", "fixed \"the\" sky\nand the water" }, 1);

            Assert.Equal("fixed the sky and the water", options.ResolveChangeNote());
        }

        [Fact]
        public void TheTitleFallsBackToTheMapName()
        {
            var options = Options.Parse(new[] { "publish", Path.Combine("C:", "maps", "gm_test.bsp") }, 1);

            Assert.Equal("gm_test", options.ResolveTitle());
        }
    }
}

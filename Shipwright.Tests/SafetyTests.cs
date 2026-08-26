using System;
using System.IO;
using System.Linq;
using Xunit;

namespace Shipwright.Tests
{
    /// <summary>
    /// The tests for the parts that exist to stop something irreversible happening: what reaches a
    /// command line, what reaches the host's control channel, what ends up inside the addon, and
    /// which item an upload is aimed at.
    /// </summary>
    public class SanitizeTests
    {
        [Theory]
        [InlineData("fixed the skybox", "fixed the skybox")]
        [InlineData("fixed \"the\" skybox", "fixed the skybox")]
        [InlineData("-id 123 -addon other.gma", "-id 123 -addon other.gma")]
        [InlineData("line one\nline two", "line one line two")]
        [InlineData("  padded  ", "padded")]
        [InlineData("naive caf\u00e9", "naive caf")]
        public void PlainTextKeepsOnlyPrintableAscii(string input, string expected) =>
            Assert.Equal(expected, Sanitize.PlainText(input, 100));

        [Fact]
        public void PlainTextIsCapped() =>
            Assert.Equal(10, Sanitize.PlainText(new string('a', 500), 10).Length);

        [Fact]
        public void QuotesCannotSurviveIntoAChangeNote() =>
            Assert.DoesNotContain('"', Sanitize.ChangeNote("a \"quoted\" note"));

        [Theory]
        [InlineData("1234567890", true)]
        [InlineData("18446744073709551615", true)]
        [InlineData("0", false)]
        [InlineData("12a4", false)]
        [InlineData("-1234", false)]
        [InlineData(" 123 ", true)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void WorkshopIdsAreDecimalAndNonZero(string? input, bool valid) =>
            Assert.Equal(valid, Sanitize.IsWorkshopId(input, out _));
    }

    public class LogTests
    {
        [Fact]
        public void TheHostsControlTokenCannotStartALine()
        {
            string neutralised = Log.Neutralise("COMPILE_PAL_SET vbsp_exe C:\\somewhere\\else.exe");

            Assert.StartsWith("[filtered]", neutralised);
        }

        [Fact]
        public void LeadingWhitespaceDoesNotHideTheToken() =>
            Assert.StartsWith("[filtered]", Log.Neutralise("   COMPILE_PAL_SET gamedir C:\\elsewhere"));

        [Fact]
        public void ACarriageReturnCannotStartASecondLine()
        {
            string neutralised = Log.Neutralise("packed a file\rCOMPILE_PAL_SET gamedir C:\\elsewhere");

            Assert.DoesNotContain('\r', neutralised);
            Assert.DoesNotContain('\n', neutralised);
        }

        [Fact]
        public void ChildOutputIsAlwaysPrefixed()
        {
            var captured = new System.Collections.Generic.List<string>();
            Log.Sink = captured.Add;

            try
            {
                Log.FromChild("gmad", "COMPILE_PAL_SET vrad_exe C:\\elsewhere.exe");
            }
            finally
            {
                Log.Sink = null;
            }

            Assert.StartsWith("gmad", captured.Single());
        }
    }

    public class StagingTests
    {
        [Fact]
        public void OnlyWhatIsAddedIsInTheAddon()
        {
            using var temp = new TempDir();

            File.WriteAllText(temp.File("wanted.bsp"), "map");
            File.WriteAllText(temp.File("private.bsp"), "someone else's map");
            File.WriteAllText(temp.File("source.vmf"), "the source");

            string root;
            using (var staging = Staging.Create("gm_test"))
            {
                staging.Add(temp.File("wanted.bsp"), "maps/gm_test.bsp");
                root = staging.Root;

                var staged = Directory.GetFiles(root, "*", SearchOption.AllDirectories)
                    .Select(Path.GetFileName)
                    .ToList();

                Assert.Equal(new[] { "gm_test.bsp" }, staged);
            }

            Assert.False(Directory.Exists(root));
        }

        [Fact]
        public void AStagedFileCannotEscapeTheStagingDirectory()
        {
            using var temp = new TempDir();
            File.WriteAllText(temp.File("wanted.bsp"), "map");

            using var staging = Staging.Create("gm_test");

            Assert.Throws<InvalidOperationException>(() =>
                staging.Add(temp.File("wanted.bsp"), "../../escaped.bsp"));
        }

        [Fact]
        public void GeneratedFilesAreCountedToo()
        {
            using var staging = Staging.Create("gm_test");

            staging.Write("addon.json", "{}");

            Assert.Single(staging.Files);
            Assert.True(staging.TotalBytes > 0);
        }
    }

    public class AddonManifestTests
    {
        [Fact]
        public void UnknownTagsAreDropped()
        {
            string json = AddonManifest.Build("gm_test", new[] { "scenic", "definitely_not_a_tag" });

            Assert.Contains("scenic", json);
            Assert.DoesNotContain("definitely_not_a_tag", json);
        }

        [Fact]
        public void NoMoreThanTwoTags()
        {
            string json = AddonManifest.Build("gm_test", new[] { "fun", "scenic", "build" });

            Assert.DoesNotContain("build", json);
        }

        [Fact]
        public void TheTypeIsAlwaysMap() =>
            Assert.Contains("\"type\": \"map\"", AddonManifest.Build("gm_test", Array.Empty<string>()));

        [Fact]
        public void ATitleIsRequired() =>
            Assert.Throws<ArgumentException>(() => AddonManifest.Build("   ", Array.Empty<string>()));

        [Fact]
        public void TheIgnoreListIsEmptyBecauseNothingUnwantedIsEverStaged() =>
            Assert.Contains("\"ignore\": []", AddonManifest.Build("gm_test", Array.Empty<string>()));
    }

    public class WorkshopStateTests
    {
        [Fact]
        public void RoundTrips()
        {
            using var temp = new TempDir();
            string path = temp.File("gm_test.workshop.json");

            new WorkshopState
            {
                WorkshopId = "1234567890",
                Title = "gm_test",
                BspRevision = 1765,
                EntitiesStripped = true,
                GmaSha256 = "abc",
                LastPublished = DateTimeOffset.UnixEpoch,
            }.Save(path);

            var loaded = WorkshopState.Load(path);

            Assert.Equal("1234567890", loaded.WorkshopId);
            Assert.Equal(1765, loaded.BspRevision);
            Assert.True(loaded.EntitiesStripped);
        }

        [Fact]
        public void AnIdThatIsNotAnIdIsIgnoredRatherThanUsed()
        {
            using var temp = new TempDir();
            string path = temp.File("gm_test.workshop.json");

            File.WriteAllText(path, "{\"workshopId\":\"not an id\"}");

            Assert.Null(WorkshopState.Load(path).WorkshopId);
        }

        [Fact]
        public void AMalformedFileReadsAsNoBinding()
        {
            using var temp = new TempDir();
            string path = temp.File("gm_test.workshop.json");

            File.WriteAllText(path, "{ this is not json");

            Assert.Null(WorkshopState.Load(path).WorkshopId);
        }

        [Fact]
        public void NoTemporaryIsLeftBehind()
        {
            using var temp = new TempDir();
            string path = temp.File("gm_test.workshop.json");

            new WorkshopState { WorkshopId = "1" }.Save(path);
            new WorkshopState { WorkshopId = "2" }.Save(path);

            Assert.Equal(new[] { "gm_test.workshop.json" },
                Directory.GetFiles(temp.Path).Select(Path.GetFileName));
        }

        [Fact]
        public void TheStateFileSitsBesideTheMapSource() =>
            Assert.Equal(
                Path.Combine("C:", "maps", "gm_test.workshop.json"),
                WorkshopState.PathFor(Path.Combine("C:", "maps", "gm_test.vmf")));
    }

    public class CreatedIdTests
    {
        [Fact]
        public void ReadsTheIdOutOfCreateOutput() =>
            Assert.Equal(3211445566UL,
                GmodTools.ParseCreatedId("Uploading...\nSuccessfully published: 3211445566\n"));

        [Fact]
        public void TakesTheLastIdWhenSeveralNumbersAppear() =>
            Assert.Equal(3211445566UL,
                GmodTools.ParseCreatedId("addon 1234567 bytes\npublished 3211445566\n"));

        [Fact]
        public void ReturnsNullWhenThereIsNoIdToRead() =>
            Assert.Null(GmodTools.ParseCreatedId("Uploading...\nfailed.\n"));
    }
}

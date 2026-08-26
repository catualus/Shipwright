using System.IO;
using System.Linq;
using Xunit;

namespace Shipwright.Tests
{
    /// <summary>
    /// Reading the account's own items out of gmpublish.
    ///
    /// The format is a console tool's human-readable output, not an interface: it is undocumented,
    /// it has changed between versions, and the banner at the top contains a date that looks like an
    /// id if you squint. So the parser is written to find ids and ignore what it cannot read, and
    /// these fixtures cover the shapes it plausibly meets rather than one shape it must meet.
    /// </summary>
    public class PublishedListTests
    {
        private const string Banner =
            "Garry's Mod Workshop Publisher 1.2\nCompiled Jun 15 2026 - 16:07:09\n\n";

        [Fact]
        public void ReadsIdAndTitleFromASimpleList()
        {
            var items = GmodTools.ParseList(Banner +
                "3211445566 - Atlas RP | Downtown\n" +
                "3298112034 - Atlas RP | Docks (beta)\n");

            Assert.Equal(2, items.Count);
            Assert.Equal(3211445566UL, items[0].Id);
            Assert.Equal("Atlas RP | Downtown", items[0].Title);
            Assert.Equal("Atlas RP | Docks (beta)", items[1].Title);
        }

        [Fact]
        public void ReadsATitleThatComesFirst()
        {
            var items = GmodTools.ParseList(Banner + "Atlas RP | Downtown (3211445566)\n");

            Assert.Single(items);
            Assert.Equal(3211445566UL, items[0].Id);
            Assert.Contains("Atlas RP", items[0].Title);
        }

        [Fact]
        public void ReadsAColumnLayout()
        {
            var items = GmodTools.ParseList(Banner +
                "ID           Title\n" +
                "3211445566   Atlas RP | Downtown\n");

            Assert.Single(items);
            Assert.Equal("Atlas RP | Downtown", items[0].Title);
        }

        [Fact]
        public void TheBannerIsNotAnItem()
        {
            // "Compiled Jun 15 2026 - 16:07:09" has a run of digits in it.
            Assert.Empty(GmodTools.ParseList(Banner));
        }

        [Fact]
        public void AnItemWithNoTitleIsStillAnItem()
        {
            var items = GmodTools.ParseList(Banner + "3211445566\n");

            Assert.Single(items);
            Assert.Equal("", items[0].Title);
        }

        [Fact]
        public void TheSameItemTwiceIsOneItem()
        {
            var items = GmodTools.ParseList(Banner +
                "3211445566 - Atlas\n3211445566 - Atlas\n");

            Assert.Single(items);
        }

        [Fact]
        public void AnErrorInsteadOfAListIsNoItems()
        {
            Assert.Empty(GmodTools.ParseList(Banner + "Error:\n\nCouldn't initialize Steam!\nMake sure it is running!\n"));
        }

        [Fact]
        public void TitlesAreSanitisedTheWayEverythingElseIs()
        {
            var items = GmodTools.ParseList(Banner + "3211445566 - Atlas \"quoted\" RP\n");

            Assert.DoesNotContain('"', items[0].Title);
        }
    }

    public class SteamAppDirectoryTests
    {
        [Fact]
        public void FindsTheFolderHoldingSteamAppId()
        {
            using var temp = new TempDir();

            // The shape of a Garry's Mod install: the tools are two levels below the file that says
            // which application they belong to.
            string bin = Path.Combine(temp.Path, "bin", "win64");
            Directory.CreateDirectory(bin);
            File.WriteAllText(Path.Combine(temp.Path, "steam_appid.txt"), "4000");

            string exe = Path.Combine(bin, "gmpublish.exe");
            File.WriteAllText(exe, "");

            Assert.Equal(temp.Path, GmodTools.SteamAppDirectory(exe));
        }

        [Fact]
        public void FallsBackToTheProgramsOwnFolder()
        {
            using var temp = new TempDir();

            string exe = Path.Combine(temp.Path, "gmpublish.exe");
            File.WriteAllText(exe, "");

            Assert.Equal(temp.Path, GmodTools.SteamAppDirectory(exe));
        }
    }
}

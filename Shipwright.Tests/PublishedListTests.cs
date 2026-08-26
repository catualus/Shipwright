using System.IO;
using System.Linq;
using Xunit;

namespace Shipwright.Tests
{
    /// <summary>
    /// Reading the account's own items out of gmpublish.
    ///
    /// The first fixture is the real thing, captured from `gmpublish.exe list` on a machine with ten
    /// published items. Everything the parser does that is not obvious - refusing an id that is not
    /// the first thing on its line, dropping the size that sits between the id and the title - is
    /// there because of a trap in that output rather than a hypothetical.
    /// </summary>
    public class PublishedListTests
    {
        /// <summary>
        /// Real output. Note the second line: a SteamID is seventeen digits, and taking the first
        /// long number on any line would file it as a Workshop item called
        /// "SteamInternal_SetMinidumpSteamID: Caching Steam ID: [API loaded no]".
        /// </summary>
        private const string RealOutput =
            "Garry's Mod Workshop Publisher 1.2\n" +
            "[Compiled Jun 15 2026 - 16:07:09]\n" +
            "\n" +
            "Setting breakpad minidump AppID = 4000\n" +
            "SteamInternal_SetMinidumpSteamID:  Caching Steam ID:  76561198115990249 [API loaded no]\n" +
            "\n" +
            "Getting published files..\n" +
            "\t3790485925\t50.4 KB \"[Meowy Roleplay] Gang Flag\"\n" +
            "\t3709971909\t26.6 MB \"Zero´s Trashman - Contentpack [Reupload]\"\n" +
            "\t3706313781\t74.8 MB \"[Meowy Roleplay] Map\"\n" +
            "\t3657646236\t1.1 GB\t\"[Meowy Roleplay] Weaponry\"\n" +
            "\t3135471114\t113.2 MB\t\"Fixed M9K Specialties\"\n" +
            "Done\n";

        [Fact]
        public void ReadsEveryItemAndNothingElse()
        {
            var items = GmodTools.ParseList(RealOutput);

            Assert.Equal(5, items.Count);
            Assert.DoesNotContain(items, i => i.Id == 76561198115990249);
        }

        [Fact]
        public void TheSizeIsNotPartOfTheTitle()
        {
            var items = GmodTools.ParseList(RealOutput);

            Assert.Equal(3790485925UL, items[0].Id);
            Assert.Equal("[Meowy Roleplay] Gang Flag", items[0].Title);
            Assert.DoesNotContain("KB", items[0].Title);
            Assert.DoesNotContain("MB", items[2].Title);
        }

        [Fact]
        public void ATitleKeepsItsOwnCharacters()
        {
            var items = GmodTools.ParseList(RealOutput);

            // Not everybody's Workshop is in English; reducing titles to ASCII would leave some of
            // them empty and this one misspelt.
            Assert.Equal("Zero´s Trashman - Contentpack [Reupload]", items[1].Title);
        }

        [Fact]
        public void TabsBetweenTheColumnsAreNotTheTitleEither()
        {
            var items = GmodTools.ParseList(RealOutput);

            Assert.Equal("[Meowy Roleplay] Weaponry", items[3].Title);
            Assert.Equal("Fixed M9K Specialties", items[4].Title);
        }

        [Fact]
        public void AnIdInTheMiddleOfALineIsNotAnItem() =>
            Assert.Empty(GmodTools.ParseList(
                "SteamInternal_SetMinidumpSteamID:  Caching Steam ID:  76561198115990249 [API loaded no]\n"));

        [Fact]
        public void AnUnquotedTitleStillReadsAfterTheSize()
        {
            var items = GmodTools.ParseList("\t3790485925\t50.4 KB Some Addon Without Quotes\n");

            Assert.Single(items);
            Assert.Equal("Some Addon Without Quotes", items[0].Title);
        }

        [Fact]
        public void AnItemWithNothingAfterTheIdIsStillAnItem()
        {
            var items = GmodTools.ParseList("\t3790485925\t\n");

            Assert.Single(items);
            Assert.Equal("", items[0].Title);
        }

        [Fact]
        public void TheSameItemTwiceIsOneItem() =>
            Assert.Single(GmodTools.ParseList("\t3790485925\t1 KB \"Atlas\"\n\t3790485925\t1 KB \"Atlas\"\n"));

        [Fact]
        public void AnErrorInsteadOfAListIsNoItems() =>
            Assert.Empty(GmodTools.ParseList(
                "Garry's Mod Workshop Publisher 1.2\nError:\n\nCouldn't initialize Steam!\nMake sure it is running!\n"));

        [Fact]
        public void TheBannerIsNotAnItem() =>
            Assert.Empty(GmodTools.ParseList("Garry's Mod Workshop Publisher 1.2\n[Compiled Jun 15 2026 - 16:07:09]\n"));
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

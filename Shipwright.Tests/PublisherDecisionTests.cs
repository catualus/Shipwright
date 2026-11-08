using System;
using System.Collections.Generic;
using Xunit;

namespace Shipwright.Tests
{
    /// <summary>
    /// Reading what a create left behind.
    ///
    /// Every gmpublish line here is one of gmpublish 1.2's own format strings, read out of the
    /// binary, with the banner and Steam's startup lines from a real capture in front of them. The
    /// runs themselves are assembled rather than captured: a create that fails halfway cannot be
    /// produced on purpose without leaving an empty item on somebody's account.
    /// </summary>
    public class CreateOutcomeTests
    {
        private const ulong NewItem = 3811223344;

        /// <summary>
        /// The start of every run, from the capture in <see cref="PublishedListTests"/>. The second
        /// line is the trap: a SteamID is as long as a Workshop ID.
        /// </summary>
        private const string Preamble =
            "Garry's Mod Workshop Publisher 1.2\n" +
            "[Compiled Jun 15 2026 - 16:07:09]\n" +
            "\n" +
            "Setting breakpad minidump AppID = 4000\n" +
            "SteamInternal_SetMinidumpSteamID:  Caching Steam ID:  76561198115990249 [API loaded no]\n";

        /// <summary>Steam has made the item; gmpublish is about to give it a title, icon, tags and file.</summary>
        private const string Announced =
            Preamble +
            "\n" +
            "Beginning creation process...\n" +
            "Requesting a new Workshop Item ID...\n" +
            "Success! Your new item ID is 3811223344!\n" +
            "Starting Update...\n" +
            "Updating title...\n" +
            "Updating icon...\n";

        /// <summary>
        /// The rest of a create that worked. The path has a long digit run in it, which is what a
        /// staging folder under a numbered build directory looks like.
        /// </summary>
        private const string Finished =
            Announced +
            "Updating Tags...\n" +
            "Updating Addon File...\n" +
            "SetContentFile( C:\\maps\\build_20261108\\gm_harbour.gma )\n" +
            "Submitting update.. (this can take a long time)\n" +
            "Uploading content...\n" +
            "\t100% ( 74.8 MB / 74.8 MB )\n" +
            "Success!\n" +
            "\n" +
            "Addon creation finished.\n" +
            "It is located at http://steamcommunity.com/sharedfiles/filedetails/?id=3811223344\n";

        [Fact]
        public void ACreateThatWorkedIsCreatedWithItsId()
        {
            var reading = Publisher.ReadCreate(new ToolResult(0, Finished));

            Assert.Equal(Publisher.CreateOutcome.Created, reading.Outcome);
            Assert.Equal(NewItem, reading.Id);
        }

        [Theory]
        [InlineData("Image update failed!\n")]
        [InlineData("Failed to set title!\n")]
        [InlineData("Updating Tags...\nUpdating Addon File...\nFailed to update addon file!\n")]
        [InlineData("Updating Tags...\nFailed to update addon tags!\n")]
        [InlineData("Updating Tags...\nUpdating Addon File...\nSubmitting update.. (this can take a long time)\nFailed to submit item update!\n")]
        [InlineData("Updating Tags...\nUpdating Addon File...\nSubmitting update.. (this can take a long time)\nUpdate failed! Generic failure\n")]
        public void AnUploadThatFailedAfterTheItemWasMadeStillYieldsItsId(string failure)
        {
            var reading = Publisher.ReadCreate(new ToolResult(1, Announced + failure));

            Assert.Equal(Publisher.CreateOutcome.CreatedEmpty, reading.Outcome);
            Assert.Equal(NewItem, reading.Id);
        }

        [Fact]
        public void TheLegalAgreementAfterTheItemWasMadeStillYieldsItsId()
        {
            var reading = Publisher.ReadCreate(new ToolResult(1, Announced +
                "Updating Tags...\nUpdating Addon File...\nSubmitting update.. (this can take a long time)\n" +
                "Update failed! You must agree to Workshop ToS before you can publish content to workshop!\n"));

            Assert.Equal(Publisher.CreateOutcome.CreatedEmpty, reading.Outcome);
            Assert.Equal(NewItem, reading.Id);
        }

        [Fact]
        public void TheLegalAgreementInsteadOfAnIdIsItsOwnOutcome()
        {
            var reading = Publisher.ReadCreate(new ToolResult(1, Preamble +
                "\nBeginning creation process...\nRequesting a new Workshop Item ID...\n" +
                "Item creation failed! You must agree to Workshop ToS before you can publish content to workshop!\n" +
                "Please visit http://steamcommunity.com/sharedfiles/workshoplegalagreement and accept the agreement!\n"));

            Assert.Equal(Publisher.CreateOutcome.AgreementNeeded, reading.Outcome);
            Assert.Null(reading.Id);
        }

        [Fact]
        public void AFailedCreateIsNeverBoundToTheAccountsSteamId()
        {
            // The one long number in this output is the SteamID. Reading "the last long number"
            // here, as a successful create does, would bind the map to the account.
            var reading = Publisher.ReadCreate(new ToolResult(1, Preamble +
                "\nBeginning creation process...\nRequesting a new Workshop Item ID...\n" +
                "Creation failed! Generic failure\n"));

            Assert.Equal(Publisher.CreateOutcome.Failed, reading.Outcome);
            Assert.Null(reading.Id);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        public void NotReachingSteamCreatedNothingWhateverTheExitCode(int exitCode)
        {
            var reading = Publisher.ReadCreate(new ToolResult(exitCode,
                "Garry's Mod Workshop Publisher 1.2\n[Compiled Jun 15 2026 - 16:07:09]\n\n" +
                "Couldn't initialize Steam!\nMake sure it is running and is not in compatibility/administrator mode!\n\n"));

            Assert.Equal(Publisher.CreateOutcome.NoSteam, reading.Outcome);
            Assert.Null(reading.Id);
        }

        [Fact]
        public void TheAnnouncementIsReadPastTheSteamIdAndThePath() =>
            Assert.Equal(NewItem, GmodTools.ParseAnnouncedId(Finished));

        [Fact]
        public void NoAnnouncementIsNoId() =>
            Assert.Null(GmodTools.ParseAnnouncedId(Preamble + "SetContentFile( C:\\maps\\build_20261108\\gm_harbour.gma )\n"));
    }

    /// <summary>
    /// Whether the item a map is bound to may be replaced by it.
    ///
    /// The case this exists for is a content pack bound by mistake - its title reads like the map's
    /// ("[Server] Map Content") and pasting its link used to bind it without a word.
    /// </summary>
    public class TargetCheckTests
    {
        private const ulong Id = 3706313781;

        private static ItemDetails Item(params string[] tags) =>
            new(true, "found.", "[Meowy Roleplay] Map Content", WorkshopLookup.GarrysModAppId, Id: Id, Tags: tags);

        [Fact]
        public void AMapIsReplacedWithoutComment()
        {
            var check = Publisher.CheckTarget(Id, Item("Fun", "Roleplay", "Addon", "map"), allowNonMap: false);

            Assert.Equal(Publisher.TargetVerdict.Proceed, check.Verdict);
        }

        [Fact]
        public void AContentPackIsRefusedAndNamed()
        {
            var check = Publisher.CheckTarget(Id, Item("Addon", "ServerContent", "Roleplay"), allowNonMap: false);

            Assert.Equal(Publisher.TargetVerdict.Refuse, check.Verdict);
            Assert.Contains("ServerContent", check.Message);
            Assert.Contains(Id.ToString(), check.Message);
            Assert.Contains("-allownonmap", check.Message);
        }

        [Fact]
        public void ARealWeaponPackIsRefused()
        {
            // A real response: the Weaponry item from the fixture in ItemTagTests.
            var details = WorkshopLookup.ParseResponse("""
            {"response":{"result":1,"resultcount":1,"publishedfiledetails":[
              {"publishedfileid":"3657646236","result":1,"consumer_app_id":4000,"file_size":"1181116006",
               "title":"[Meowy Roleplay] Weaponry","time_updated":"1754000000",
               "tags":[{"tag":"Roleplay"},{"tag":"Scenic"},{"tag":"Realism"},{"tag":"Addon"},{"tag":"Weapon"}]}
            ]}}
            """);

            var check = Publisher.CheckTarget(3657646236, details, allowNonMap: false);

            Assert.Equal(Publisher.TargetVerdict.Refuse, check.Verdict);
            Assert.Contains("Weapon", check.Message);
        }

        [Fact]
        public void AllowNonMapLetsANonMapThroughButStillSaysSo()
        {
            var check = Publisher.CheckTarget(Id, Item("Addon", "ServerContent"), allowNonMap: true);

            Assert.Equal(Publisher.TargetVerdict.Warn, check.Verdict);
            Assert.Contains("ServerContent", check.Message);
        }

        [Fact]
        public void TagsWithNoTypeAmongThemAreStillNotAMap()
        {
            var check = Publisher.CheckTarget(Id, Item("Fun", "Roleplay"), allowNonMap: false);

            Assert.Equal(Publisher.TargetVerdict.Refuse, check.Verdict);
            Assert.Contains("Fun, Roleplay", check.Message);
        }

        [Fact]
        public void AnUntaggedItemWarnsRatherThanRefuses()
        {
            // What a create that failed after making the item leaves behind: no tags yet.
            var check = Publisher.CheckTarget(Id, Item(), allowNonMap: false);

            Assert.Equal(Publisher.TargetVerdict.Warn, check.Verdict);
        }

        [Fact]
        public void AnItemTheLookupCouldNotSeeWarnsRatherThanRefuses()
        {
            // A new item is hidden from the public lookup until its legal agreement is accepted.
            var check = Publisher.CheckTarget(Id,
                new ItemDetails(false, "no public item has that ID. It may be deleted, or private."), allowNonMap: false);

            Assert.Equal(Publisher.TargetVerdict.Warn, check.Verdict);
        }

        [Fact]
        public void AnotherGamesItemIsRefusedEvenWithAllowNonMap()
        {
            var details = new ItemDetails(true, "found.", "Some CS2 map", 730, Id: Id, Tags: new[] { "Map" });

            Assert.Equal(Publisher.TargetVerdict.Refuse, Publisher.CheckTarget(Id, details, allowNonMap: true).Verdict);
        }

        /// <summary>
        /// -force is what people leave on for re-uploads. Had it also let a non-map through, the
        /// check that stops a content pack being replaced by a map would be off for exactly them.
        /// </summary>
        [Fact]
        public void ForceAloneDoesNotAllowANonMap()
        {
            var forced = Options.Parse(new[] { "publish", "gm_test.bsp", "-force" }, 1);
            var allowed = Options.Parse(new[] { "publish", "gm_test.bsp", "-allownonmap" }, 1);

            Assert.True(forced.Force);
            Assert.False(forced.AllowNonMap);
            Assert.True(allowed.AllowNonMap);
            Assert.False(allowed.Force);
        }
    }

    /// <summary>Recording an item that was created but never received the map.</summary>
    public class EmptyItemRecordTests
    {
        [Fact]
        public void TheBindingIsRecordedAndNothingThatWouldSkipTheNextUpload()
        {
            using var temp = new TempDir();
            string statePath = temp.File("gm_harbour.workshop.json");

            // An unbound map can still carry these from an item it was bound to before.
            var state = new WorkshopState
            {
                GmaSha256 = "0123abcd",
                LastPublished = DateTimeOffset.UtcNow,
                Publish = true,
                AllowCreate = true,
            };

            var options = new Options { BspPath = temp.File("gm_harbour.bsp"), StatePath = statePath };

            var lines = new List<string>();
            Log.Sink = lines.Add;

            bool recorded;

            try
            {
                recorded = Publisher.RecordEmptyItem(options, state, 3811223344);
            }
            finally
            {
                Log.Sink = null;
            }

            Assert.True(recorded);

            var saved = WorkshopState.Load(statePath);

            Assert.Equal("3811223344", saved.WorkshopId);
            Assert.Null(saved.GmaSha256);
            Assert.Null(saved.LastPublished);
            Assert.Equal("gm_harbour", saved.Title);

            // The map's own switches are left as they were.
            Assert.True(saved.Publish);
            Assert.True(saved.AllowCreate);
        }
    }
}

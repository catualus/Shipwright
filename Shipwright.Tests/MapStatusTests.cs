using System.IO;
using System.Text.Json;
using Xunit;

namespace Shipwright.Tests
{
    /// <summary>
    /// What the queue row says, and - the part that matters - when it stops a compile from starting.
    /// </summary>
    public class MapStatusTests
    {
        private static string StateWith(TempDir temp, WorkshopState state)
        {
            string path = temp.File("gm_test.workshop.json");
            state.Save(path);
            return path;
        }

        [Fact]
        public void AnUnboundMapOnAPublishingPresetStopsTheRun()
        {
            using var temp = new TempDir();

            var status = MapStatusReporter.Describe(
                temp.File("nothing-here.workshop.json"), "-publish", stepEnabled: true);

            Assert.Equal(StatusSeverity.Blocking, status.Severity);
            Assert.True(status.Confirm);
            Assert.Contains("publish nothing", status.Detail);
        }

        [Fact]
        public void AnUnboundMapIsOnlyAChipWhenNothingWouldBePublished()
        {
            using var temp = new TempDir();

            var status = MapStatusReporter.Describe(
                temp.File("nothing-here.workshop.json"), "-mininterval 5", stepEnabled: true);

            Assert.Equal(StatusSeverity.Ok, status.Severity);
            Assert.False(status.Confirm);
        }

        [Fact]
        public void ADisabledStepNeverBlocks()
        {
            using var temp = new TempDir();

            var status = MapStatusReporter.Describe(
                temp.File("nothing-here.workshop.json"), "-publish", stepEnabled: false);

            Assert.Equal(StatusSeverity.Ok, status.Severity);
        }

        [Fact]
        public void AllowingCreationTurnsTheBlockIntoAWarning()
        {
            using var temp = new TempDir();

            var status = MapStatusReporter.Describe(
                temp.File("nothing-here.workshop.json"), "-publish -allowcreate", stepEnabled: true);

            Assert.Equal(StatusSeverity.Warn, status.Severity);
            Assert.True(status.Confirm);
            Assert.Contains("Creates a new", status.Detail);
        }

        [Fact]
        public void ABoundMapNamesTheItemItWouldReplace()
        {
            using var temp = new TempDir();
            string path = StateWith(temp, new WorkshopState
            {
                WorkshopId = "3211445566",
                Title = "Atlas RP | Downtown",
            });

            var status = MapStatusReporter.Describe(path, "-publish", stepEnabled: true);

            Assert.Equal("Atlas RP | Downtown", status.Label);
            Assert.Contains("3211445566", status.Detail);
            Assert.Contains("everyone subscribed", status.Detail);
            Assert.True(status.Confirm);
        }

        [Fact]
        public void AStrippedMapSaysWhatTheServersNeed()
        {
            using var temp = new TempDir();
            string path = StateWith(temp, new WorkshopState
            {
                WorkshopId = "3211445566",
                Title = "Atlas RP | Downtown",
                EntitiesStripped = true,
            });

            var status = MapStatusReporter.Describe(path, "-publish", stepEnabled: true);

            Assert.Contains(".lmp", status.Detail);
        }

        [Fact]
        public void ABoundMapOnANonPublishingPresetAsksForNoConfirmation()
        {
            using var temp = new TempDir();
            string path = StateWith(temp, new WorkshopState { WorkshopId = "3211445566", Title = "Atlas" });

            var status = MapStatusReporter.Describe(path, "", stepEnabled: true);

            Assert.Equal(StatusSeverity.Ok, status.Severity);
            Assert.False(status.Confirm);
            Assert.Equal("Atlas", status.Label);
        }

        [Fact]
        public void AnItemWithNoRecordedTitleIsNamedByItsId()
        {
            using var temp = new TempDir();
            string path = StateWith(temp, new WorkshopState { WorkshopId = "3211445566" });

            Assert.Equal("3211445566", MapStatusReporter.Describe(path, "-publish", true).Label);
        }

        [Theory]
        [InlineData("-publishnothing", false)]
        [InlineData("-changes describing -publish behaviour", true)]
        [InlineData("-changes I would -publishx this", false)]
        [InlineData("-title my-publish-map", false)]
        public void TheFlagIsOnlyFoundWhenItIsTheWholeWord(string args, bool shouldBlock)
        {
            using var temp = new TempDir();

            var status = MapStatusReporter.Describe(
                temp.File("nothing-here.workshop.json"), args, stepEnabled: true);

            Assert.Equal(shouldBlock ? StatusSeverity.Blocking : StatusSeverity.Ok, status.Severity);
        }

        [Fact]
        public void TheJsonCarriesTheFourFieldsTheHostReads()
        {
            var json = new MapStatus("Atlas", "detail here", StatusSeverity.Info, true).ToJson();

            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            Assert.Equal("Atlas", root.GetProperty("label").GetString());
            Assert.Equal("detail here", root.GetProperty("detail").GetString());
            Assert.Equal("info", root.GetProperty("severity").GetString());
            Assert.True(root.GetProperty("confirm").GetBoolean());
        }
    }
}

using Xunit;

namespace Shipwright.Tests
{
    /// <summary>
    /// The settings that moved out of the compile parameters and into the map's own file.
    ///
    /// A preset is shared by every map in the queue, so anything that differs per map could not live
    /// in one: arming one map armed all of them, and one Workshop ID in a preset pointed every queued
    /// map at the same item. The window writes these per map instead; the flags still work, for
    /// anyone driving the step from a script.
    /// </summary>
    public class PerMapSettingsTests
    {
        private static Options Parse(params string[] flags)
        {
            var args = new string[flags.Length + 2];
            args[0] = "publish";
            args[1] = "gm_test.bsp";
            flags.CopyTo(args, 2);

            return Options.Parse(args, 1);
        }

        [Fact]
        public void NothingPublishesUnlessSomethingSaysSo()
        {
            Assert.False(Parse().ResolvePublish(new WorkshopState()));
            Assert.False(Parse().ResolvePublish(null));
        }

        [Fact]
        public void TheMapsOwnSwitchArmsIt() =>
            Assert.True(Parse().ResolvePublish(new WorkshopState { Publish = true }));

        [Fact]
        public void TheFlagArmsItToo() =>
            Assert.True(Parse("-publish").ResolvePublish(new WorkshopState()));

        [Fact]
        public void NeitherSourceCanDisarmTheOther()
        {
            // OR rather than override, so nothing can quietly turn off a publish somebody armed -
            // and, more importantly, nothing can arm one that neither of them did.
            Assert.True(Parse("-publish").ResolvePublish(new WorkshopState { Publish = false }));
            Assert.True(Parse().ResolvePublish(new WorkshopState { Publish = true }));
        }

        [Fact]
        public void CreatingAnItemWorksTheSameWay()
        {
            Assert.False(Parse().ResolveAllowCreate(new WorkshopState()));
            Assert.True(Parse().ResolveAllowCreate(new WorkshopState { AllowCreate = true }));
            Assert.True(Parse("-allowcreate").ResolveAllowCreate(new WorkshopState()));
        }

        [Fact]
        public void WhatTravelsWithTheMapComesFromTheMap()
        {
            var state = new WorkshopState { IncludeLump = true, IncludeNav = true };

            Assert.True(Parse().ResolveIncludeLump(state));
            Assert.True(Parse().ResolveIncludeNav(state));
            Assert.False(Parse().ResolveIncludeAin(state));
        }

        [Fact]
        public void AMapThatNeverAnsweredKeepsTheFlagsAnswer()
        {
            // The nullable is what makes "never chosen" different from "chosen no" - a map published
            // before the window existed keeps behaving the way its parameters said.
            var untouched = new WorkshopState();

            Assert.True(Parse("-includelump").ResolveIncludeLump(untouched));
            Assert.False(Parse().ResolveIncludeLump(untouched));
        }

        [Fact]
        public void TheChangeNoteComesFromTheMapUnlessOneWasTyped()
        {
            var state = new WorkshopState { ChangeNote = "from the window" };

            Assert.Equal("from the window", Parse().ResolveChangeNote(state));
            Assert.Equal("from the flag", Parse("-changes", "from the flag").ResolveChangeNote(state));
        }

        [Fact]
        public void TheIntervalIsTheMapsUnlessTheFlagWasGiven()
        {
            var state = new WorkshopState { MinIntervalMinutes = 60 };

            Assert.Equal(60, Parse().ResolveMinInterval(state));
            Assert.Equal(0, Parse("-mininterval", "0").ResolveMinInterval(state));

            // A map that has never said anything falls back to the step's own default.
            Assert.Equal(5, Parse().ResolveMinInterval(new WorkshopState()));
        }

        [Fact]
        public void TheQueueChipFollowsTheMapsSwitch()
        {
            using var temp = new TempDir();
            string path = temp.File("gm_test.workshop.json");

            new WorkshopState { Publish = true }.Save(path);

            var status = MapStatusReporter.Describe(path, stepArgs: "", stepEnabled: true);

            // Set to publish, bound to nothing, creating not allowed: the run must not start.
            Assert.Equal(StatusSeverity.Blocking, status.Severity);
        }

        [Fact]
        public void AMapWithPublishingOffIsNeverBlocking()
        {
            using var temp = new TempDir();
            string path = temp.File("gm_test.workshop.json");

            new WorkshopState { Publish = false }.Save(path);

            Assert.Equal(StatusSeverity.Ok, MapStatusReporter.Describe(path, "", true).Severity);
        }
    }
}

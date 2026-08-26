using System;
using System.IO;
using System.Linq;
using Xunit;

namespace Shipwright.Tests
{
    /// <summary>
    /// The settings window's half of the contract: what someone pastes, and what the state file
    /// carries between the window and the compile step.
    /// </summary>
    public class WorkshopLinkTests
    {
        [Theory]
        [InlineData("https://steamcommunity.com/sharedfiles/filedetails/?id=3211445566", 3211445566UL)]
        [InlineData("http://steamcommunity.com/sharedfiles/filedetails/?id=3211445566", 3211445566UL)]
        [InlineData("steamcommunity.com/sharedfiles/filedetails/?id=3211445566&searchtext=atlas", 3211445566UL)]
        [InlineData("https://steamcommunity.com/workshop/filedetails/?id=3211445566", 3211445566UL)]
        [InlineData("steam://url/CommunityFilePage/3211445566", 3211445566UL)]
        [InlineData("3211445566", 3211445566UL)]
        [InlineData("  3211445566  ", 3211445566UL)]
        public void ReadsWhatPeopleActuallyPaste(string pasted, ulong expected)
        {
            // steam:// has no id= parameter, so it is only recognised when it happens to be the bare
            // number - which is what this asserts about, one way or the other.
            bool parsed = WorkshopLink.TryParse(pasted, out ulong id);

            if (pasted.StartsWith("steam://", StringComparison.Ordinal))
            {
                Assert.False(parsed);
                return;
            }

            Assert.True(parsed);
            Assert.Equal(expected, id);
        }

        [Fact]
        public void TakesTheIdParameterAndNotTheFirstNumberInTheUrl()
        {
            // appid comes first in the string. Taking it would resolve to a real but wrong item.
            Assert.True(WorkshopLink.TryParse(
                "https://steamcommunity.com/workshop/browse/?appid=4000&id=3211445566", out ulong id));

            Assert.Equal(3211445566UL, id);
        }

        [Theory]
        [InlineData("")]
        [InlineData(null)]
        [InlineData("gm_atlas_dev")]
        [InlineData("https://steamcommunity.com/sharedfiles/filedetails/")]
        [InlineData("id=abc")]
        public void RefusesWhatItCannotUnderstand(string? pasted) =>
            Assert.False(WorkshopLink.TryParse(pasted, out _));

        [Fact]
        public void BuildsThePageAddress() =>
            Assert.Equal("https://steamcommunity.com/sharedfiles/filedetails/?id=3211445566",
                WorkshopLink.UrlFor(3211445566UL));
    }

    public class NewItemStateTests
    {
        [Fact]
        public void TitleTagsAndIconSurviveARoundTrip()
        {
            using var temp = new TempDir();
            string path = temp.File("gm_test.workshop.json");

            new WorkshopState
            {
                Title = "Atlas RP | Nightside",
                Tags = new[] { "roleplay", "scenic" },
                IconPath = @"C:\icons\nightside.jpg",
            }.Save(path);

            var loaded = WorkshopState.Load(path);

            Assert.Equal("Atlas RP | Nightside", loaded.Title);
            Assert.Equal(new[] { "roleplay", "scenic" }, loaded.Tags);
            Assert.Equal(@"C:\icons\nightside.jpg", loaded.IconPath);
        }

        [Fact]
        public void TheParameterBeatsTheStateFile()
        {
            var options = Options.Parse(new[] { "publish", "gm_test.bsp", "-title", "FromParameter" }, 1);
            var state = new WorkshopState { Title = "FromStateFile" };

            Assert.Equal("FromParameter", options.ResolveTitle(state));
        }

        [Fact]
        public void TheStateFileBeatsTheMapName()
        {
            var options = Options.Parse(new[] { "publish", "gm_test.bsp" }, 1);
            var state = new WorkshopState { Title = "Atlas RP | Nightside" };

            Assert.Equal("Atlas RP | Nightside", options.ResolveTitle(state));
        }

        [Fact]
        public void WithNeitherItIsTheMapName()
        {
            var options = Options.Parse(new[] { "publish", Path.Combine("C:", "maps", "gm_test.bsp") }, 1);

            Assert.Equal("gm_test", options.ResolveTitle(new WorkshopState()));
        }

        [Fact]
        public void TagsComeFromTheStateFileWhenTheParameterIsEmpty()
        {
            var options = Options.Parse(new[] { "publish", "gm_test.bsp" }, 1);
            var state = new WorkshopState { Tags = new[] { "scenic" } };

            Assert.Equal(new[] { "scenic" }, options.ResolveTags(state).ToArray());
        }

        [Fact]
        public void TagsFromTheParameterWinWholesale()
        {
            var options = Options.Parse(new[] { "publish", "gm_test.bsp", "-tags", "build" }, 1);
            var state = new WorkshopState { Tags = new[] { "scenic", "roleplay" } };

            Assert.Equal(new[] { "build" }, options.ResolveTags(state).ToArray());
        }
    }
}

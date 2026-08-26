using System.Linq;
using Xunit;

namespace Shipwright.Tests
{
    /// <summary>
    /// Telling maps from everything else.
    ///
    /// Garry's Mod records an addon's type as a Workshop tag, so the tag is the only honest source -
    /// and the fixtures here are real responses, including the one that shows why the title cannot
    /// be used instead: an item called "Weaponry" carrying the map tag, next to another called
    /// "Weaponry" that does not.
    /// </summary>
    public class ItemTagTests
    {
        private const string TwoItems = """
        {"response":{"result":1,"resultcount":2,"publishedfiledetails":[
          {"publishedfileid":"3706313781","result":1,"consumer_app_id":4000,"file_size":"78432000",
           "title":"[Meowy Roleplay] Map","time_updated":"1754230000",
           "tags":[{"tag":"Fun"},{"tag":"Roleplay"},{"tag":"Scenic"},{"tag":"Addon"},{"tag":"map"}]},
          {"publishedfileid":"3657646236","result":1,"consumer_app_id":4000,"file_size":"1181116006",
           "title":"[Meowy Roleplay] Weaponry","time_updated":"1754000000",
           "tags":[{"tag":"Roleplay"},{"tag":"Scenic"},{"tag":"Realism"},{"tag":"Addon"},{"tag":"Weapon"}]}
        ]}}
        """;

        [Fact]
        public void ReadsEveryItemInABatch()
        {
            var items = WorkshopLookup.ParseAll(TwoItems);

            Assert.Equal(2, items.Count);
            Assert.Equal(3706313781UL, items[0].Id);
            Assert.Equal(3657646236UL, items[1].Id);
        }

        [Fact]
        public void TheMapTagIsWhatMakesAMap()
        {
            var items = WorkshopLookup.ParseAll(TwoItems);

            Assert.True(items[0].IsMap);
            Assert.False(items[1].IsMap);
        }

        [Fact]
        public void TheTagIsReadWhateverCaseItIsWritten()
        {
            // The real data has it lowercase; gmad's own type list writes it "map"; the Workshop
            // shows it as "Map".
            var items = WorkshopLookup.ParseAll(
                """{"response":{"publishedfiledetails":[{"publishedfileid":"1","result":1,"tags":[{"tag":"MAP"}]}]}}""");

            Assert.True(items[0].IsMap);
        }

        [Fact]
        public void AnItemWithNoTagsIsNotAMap()
        {
            var items = WorkshopLookup.ParseAll(
                """{"response":{"publishedfiledetails":[{"publishedfileid":"1","result":1,"title":"x"}]}}""");

            Assert.Single(items);
            Assert.False(items[0].IsMap);
        }

        [Fact]
        public void TagsSentAsPlainStringsAreReadToo()
        {
            var items = WorkshopLookup.ParseAll(
                """{"response":{"publishedfiledetails":[{"publishedfileid":"1","result":1,"tags":["map","Fun"]}]}}""");

            Assert.True(items[0].IsMap);
            Assert.Equal(new[] { "map", "Fun" }, items[0].Tags!.ToArray());
        }

        [Fact]
        public void ItemsThatCouldNotBeDescribedAreLeftOut()
        {
            var items = WorkshopLookup.ParseAll("""
            {"response":{"publishedfiledetails":[
              {"publishedfileid":"1","result":9},
              {"publishedfileid":"3706313781","result":1,"title":"[Meowy Roleplay] Map","tags":[{"tag":"map"}]}
            ]}}
            """);

            Assert.Single(items);
            Assert.Equal(3706313781UL, items[0].Id);
        }

        [Fact]
        public void AResponseWithNothingInItIsNoItems() =>
            Assert.Empty(WorkshopLookup.ParseAll("""{"response":{"result":1,"resultcount":0}}"""));
    }
}

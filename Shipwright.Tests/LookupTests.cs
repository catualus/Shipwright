using Xunit;

namespace Shipwright.Tests
{
    /// <summary>
    /// Reading what Steam actually sends.
    ///
    /// The first real lookup this tool ever made failed - not on the network, not on the item, but
    /// on file_size arriving as "1360" with quotes around it while consumer_app_id arrived as a bare
    /// number. Asking a string element for an integer throws, and the message named neither the
    /// field nor the endpoint. These fixtures are that response's shape.
    /// </summary>
    public class LookupParsingTests
    {
        private const string NumbersAsStrings = """
        {"response":{"result":1,"resultcount":1,"publishedfiledetails":[{
          "publishedfileid":"3550189821","result":1,"creator":"76561198000000000",
          "creator_app_id":4000,"consumer_app_id":"4000","filename":"","file_size":"1360",
          "preview_url":"https://images.steamusercontent.com/ugc/preview.jpg",
          "title":"gm_construct","time_created":"1750000000","time_updated":"1754230000",
          "visibility":0,"banned":0,"subscriptions":41,"favorited":0,"tags":[{"tag":"Map"}]
        }]}}
        """;

        private const string NumbersAsNumbers = """
        {"response":{"publishedfiledetails":[{
          "publishedfileid":"3550189821","result":1,"consumer_app_id":4000,"file_size":1360,
          "title":"gm_construct","time_updated":1754230000,
          "preview_url":"https://images.steamusercontent.com/ugc/preview.jpg"
        }]}}
        """;

        [Fact]
        public void NumbersSentAsStringsAreStillNumbers()
        {
            var details = WorkshopLookup.ParseResponse(NumbersAsStrings);

            Assert.True(details.Found);
            Assert.Equal("gm_construct", details.Title);
            Assert.Equal(4000, details.ConsumerAppId);
            Assert.Equal(1360, details.SizeBytes);
            Assert.NotNull(details.Updated);
        }

        [Fact]
        public void NumbersSentAsNumbersReadTheSame()
        {
            var details = WorkshopLookup.ParseResponse(NumbersAsNumbers);

            Assert.True(details.Found);
            Assert.Equal(4000, details.ConsumerAppId);
            Assert.Equal(1360, details.SizeBytes);
        }

        [Fact]
        public void AnItemThatIsNotThereIsReportedAsMissing()
        {
            var details = WorkshopLookup.ParseResponse(
                """{"response":{"publishedfiledetails":[{"publishedfileid":"1","result":9}]}}""");

            Assert.False(details.Found);
            Assert.Contains("no public item", details.Message);
        }

        [Fact]
        public void AnEmptyResponseIsNotAnItem() =>
            Assert.False(WorkshopLookup.ParseResponse("""{"response":{"publishedfiledetails":[]}}""").Found);

        [Fact]
        public void APreviewOnSomewhereThatIsNotSteamIsDropped()
        {
            var details = WorkshopLookup.ParseResponse("""
            {"response":{"publishedfiledetails":[{"result":1,"consumer_app_id":4000,
              "title":"gm_test","preview_url":"https://someone-elses-host.example/track.jpg"}]}}
            """);

            Assert.True(details.Found);
            Assert.Equal("", details.PreviewUrl);
        }

        [Fact]
        public void APlainHttpPreviewIsDropped()
        {
            var details = WorkshopLookup.ParseResponse("""
            {"response":{"publishedfiledetails":[{"result":1,"consumer_app_id":4000,
              "title":"gm_test","preview_url":"http://images.steamusercontent.com/ugc/preview.jpg"}]}}
            """);

            Assert.Equal("", details.PreviewUrl);
        }

        [Fact]
        public void ASteamPreviewSurvives()
        {
            var details = WorkshopLookup.ParseResponse(NumbersAsStrings);

            Assert.Equal("https://images.steamusercontent.com/ugc/preview.jpg", details.PreviewUrl);
        }

        [Fact]
        public void TheTitleIsSanitisedOnTheWayIn()
        {
            // Item titles are written by other people and end up in a log line and a window.
            var details = WorkshopLookup.ParseResponse("""
            {"response":{"publishedfiledetails":[{"result":1,"consumer_app_id":4000,
              "title":"gm_bell\ttab\nnewline"}]}}
            """);

            Assert.Equal("gm_bell tab newline", details.Title);
        }
    }
}

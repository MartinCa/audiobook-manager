using AudiobookManager.Api.Controllers;
using AudiobookManager.Api.Dtos;
using AudiobookManager.Scraping.Models;
using AudiobookManager.Services;
using Microsoft.Extensions.Logging;
using Moq;

namespace AudiobookManager.Test.Controllers;

/// <summary>
/// POST metadata-search/multi: one query for every source, unless the dialog still holds the default
/// query, which is written per source.
/// </summary>
[TestClass]
public class MetadataSearchControllerMultiTests
{
    private readonly Mock<IScrapingService> _scraping = new();

    private MetadataSearchController CreateController() =>
        new(_scraping.Object, Mock.Of<IHttpClientFactory>(), Mock.Of<ISettingsService>(), Mock.Of<ILogger<MetadataSearchController>>());

    [TestMethod]
    public async Task SearchMultiple_NoPerSourceQueries_SearchesEverySourceWithTheOneQuery()
    {
        _scraping.Setup(s => s.SearchMultiple(It.IsAny<IEnumerable<string>>(), It.IsAny<string>()))
            .ReturnsAsync(new MetadataMultiSourceSearchResult());

        await CreateController().SearchMultiple(new MetadataMultiSearchDto
        {
            Sources = new List<string> { "Audible", "Hardcover" },
            Q = "typed by the user",
        });

        _scraping.Verify(s => s.SearchMultiple(
            It.Is<IEnumerable<string>>(src => src.SequenceEqual(new[] { "Audible", "Hardcover" })), "typed by the user"), Times.Once);
        _scraping.Verify(s => s.SearchMultiple(It.IsAny<IReadOnlyDictionary<string, string>>()), Times.Never);
    }

    [TestMethod]
    public async Task SearchMultiple_PerSourceQueries_EachChosenSourceGetsItsOwnAndOthersTheTypedOne()
    {
        IReadOnlyDictionary<string, string>? sent = null;
        _scraping.Setup(s => s.SearchMultiple(It.IsAny<IReadOnlyDictionary<string, string>>()))
            .Callback<IReadOnlyDictionary<string, string>>(q => sent = q)
            .ReturnsAsync(new MetadataMultiSourceSearchResult());

        await CreateController().SearchMultiple(new MetadataMultiSearchDto
        {
            Sources = new List<string> { "Audible", "Hardcover", "Goodreads" },
            Q = "plain",
            QueriesBySource = new Dictionary<string, string>
            {
                ["hardcover"] = "George R.R. Martin - Book",
                ["Unselected"] = "never used",
            },
        });

        Assert.IsNotNull(sent);
        Assert.AreEqual(3, sent.Count);
        Assert.AreEqual("George R.R. Martin - Book", sent["Hardcover"]);
        Assert.AreEqual("plain", sent["Audible"]);
        Assert.AreEqual("plain", sent["Goodreads"]);
        Assert.IsFalse(sent.ContainsKey("Unselected"), "a source the user did not choose must not be searched");
    }

    [TestMethod]
    public async Task SearchMultiple_EmptyPerSourceMap_IsTheSameAsNone()
    {
        _scraping.Setup(s => s.SearchMultiple(It.IsAny<IEnumerable<string>>(), It.IsAny<string>()))
            .ReturnsAsync(new MetadataMultiSourceSearchResult());

        await CreateController().SearchMultiple(new MetadataMultiSearchDto
        {
            Sources = new List<string> { "Audible" },
            Q = "q",
            QueriesBySource = new Dictionary<string, string>(),
        });

        _scraping.Verify(s => s.SearchMultiple(It.IsAny<IEnumerable<string>>(), "q"), Times.Once);
    }
}

using AudiobookManager.Api.Controllers;
using AudiobookManager.Api.Dtos;
using AudiobookManager.Domain;
using AudiobookManager.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using AudiobookManager.Scraping;

namespace AudiobookManager.Test.Controllers;

/// <summary>
/// POST metadata-search/default-query seeds the manual search dialog. It must apply the library's
/// search-initials handling through the same builder the bulk search uses, so the client carries
/// no copy of the rules.
/// </summary>
[TestClass]
public class MetadataSearchControllerDefaultQueryTests
{
    private static MetadataSearchController CreateController(SearchInitialsHandling handling)
    {
        var settings = new Mock<ISettingsService>();
        settings.Setup(s => s.GetLibrarySettings())
            .ReturnsAsync(new LibrarySettings { SearchInitialsHandling = handling });

        return new MetadataSearchController(
            new Mock<IScrapingService>().Object,
            new Mock<IHttpClientFactory>().Object,
            settings.Object,
            new Mock<ILogger<MetadataSearchController>>().Object)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };
    }

    private static async Task<string> QueryFor(SearchInitialsHandling handling, DefaultSearchQueryRequestDto request)
    {
        var result = await CreateController(handling).GetDefaultQuery(request);
        var ok = Assert.IsInstanceOfType<OkObjectResult>(result.Result);
        return Assert.IsInstanceOfType<DefaultSearchQueryDto>(ok.Value).Query;
    }

    [TestMethod]
    [DataRow(SearchInitialsHandling.AsStored, "George R. R. Martin - A Knight of the Seven Kingdoms")]
    [DataRow(SearchInitialsHandling.Compact, "George R.R. Martin - A Knight of the Seven Kingdoms")]
    [DataRow(SearchInitialsHandling.Spaced, "George R. R. Martin - A Knight of the Seven Kingdoms")]
    public async Task GetDefaultQuery_AppliesTheLibrarySetting(SearchInitialsHandling handling, string expected)
    {
        var query = await QueryFor(handling, new DefaultSearchQueryRequestDto(
            new[] { "George R. R. Martin" }, "A Knight of the Seven Kingdoms", "file.m4b"));

        Assert.AreEqual(expected, query);
    }

    [TestMethod]
    public async Task GetDefaultQuery_NoAuthors_FallsBackToTheBookName()
    {
        var query = await QueryFor(SearchInitialsHandling.Compact, new DefaultSearchQueryRequestDto(null, "Some Book", "file.m4b"));

        Assert.AreEqual("Some Book", query);
    }

    [TestMethod]
    public async Task GetDefaultQuery_NullBody_ReturnsAnEmptyQuery()
    {
        var query = await QueryFor(SearchInitialsHandling.Compact, null!);

        Assert.AreEqual(string.Empty, query);
    }
}

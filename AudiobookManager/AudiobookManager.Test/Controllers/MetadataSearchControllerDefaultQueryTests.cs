using AudiobookManager.Api.Controllers;
using AudiobookManager.Api.Dtos;
using AudiobookManager.Domain;
using AudiobookManager.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using AudiobookManager.Scraping;
using AudiobookManager.Scraping.Models;

namespace AudiobookManager.Test.Controllers;

/// <summary>
/// POST metadata-search/default-query seeds the manual search dialog. It must apply the library's
/// search-initials handling through the same builder the bulk search uses, so the client carries
/// no copy of the rules.
/// </summary>
[TestClass]
public class MetadataSearchControllerDefaultQueryTests
{
    private static MetadataSearchController CreateController(SearchInitialsHandling handling) =>
        CreateController(new LibrarySettings { SearchInitialsHandling = handling });

    private static MetadataSearchController CreateController(LibrarySettings librarySettings)
    {
        var settings = new Mock<ISettingsService>();
        settings.Setup(s => s.GetLibrarySettings()).ReturnsAsync(librarySettings);

        var scraping = new Mock<IScrapingService>();
        scraping.Setup(s => s.GetSearchServiceInfo()).ReturnsAsync(new List<MetadataSearchServiceInfo>
        {
            new("Audible", true), new("Hardcover", true),
        });

        return new MetadataSearchController(
            scraping.Object,
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

    private static async Task<DefaultSearchQueryDto> Dto(LibrarySettings settings, DefaultSearchQueryRequestDto request)
    {
        var result = await CreateController(settings).GetDefaultQuery(request);
        return Assert.IsInstanceOfType<DefaultSearchQueryDto>(Assert.IsInstanceOfType<OkObjectResult>(result.Result).Value);
    }

    [TestMethod]
    public async Task GetDefaultQuery_MainAuthor_IsTheOnlyAuthorInTheQuery()
    {
        var dto = await Dto(
            new LibrarySettings(),
            new DefaultSearchQueryRequestDto(
                new[] { "Brandon Sanderson", "Janci Patterson" }, "Steelheart", "f.m4b", MainAuthor: "Brandon Sanderson"));

        Assert.AreEqual("Brandon Sanderson - Steelheart", dto.Query);
        Assert.AreEqual("Brandon Sanderson - Steelheart", dto.QueriesBySource!["Audible"]);
    }

    [TestMethod]
    public async Task GetDefaultQuery_NoMainAuthor_KeepsEveryAuthor()
    {
        var dto = await Dto(
            new LibrarySettings(),
            new DefaultSearchQueryRequestDto(new[] { "A One", "B Two" }, "Book", "f.m4b"));

        Assert.AreEqual("A One, B Two - Book", dto.Query);
    }

    [TestMethod]
    public async Task GetDefaultQuery_SourceWithItsOwnHandling_GetsItsOwnQuery()
    {
        var settings = new LibrarySettings { SearchInitialsHandling = SearchInitialsHandling.AsStored };
        settings.SearchInitialsHandlingBySource["Hardcover"] = SearchInitialsHandling.Compact;

        var dto = await Dto(
            settings,
            new DefaultSearchQueryRequestDto(new[] { "George R. R. Martin" }, "A Game of Thrones", "f.m4b"));

        // The plain query follows the library-wide setting; each source follows its own rule.
        Assert.AreEqual("George R. R. Martin - A Game of Thrones", dto.Query);
        Assert.AreEqual("George R. R. Martin - A Game of Thrones", dto.QueriesBySource!["Audible"]);
        Assert.AreEqual("George R.R. Martin - A Game of Thrones", dto.QueriesBySource["Hardcover"]);
    }

    [TestMethod]
    public async Task GetDefaultQuery_SourceNamesGiven_OnlyThoseGetAQuery()
    {
        var dto = await Dto(
            new LibrarySettings(),
            new DefaultSearchQueryRequestDto(null, "Book", "f.m4b", SourceNames: new[] { "Audible" }));

        CollectionAssert.AreEqual(new[] { "Audible" }, dto.QueriesBySource!.Keys.ToArray());
    }
}

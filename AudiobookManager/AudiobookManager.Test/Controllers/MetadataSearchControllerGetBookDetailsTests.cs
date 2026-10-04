using AudiobookManager.Api.Controllers;
using AudiobookManager.Api.Dtos;
using AudiobookManager.Scraping.Models;
using AudiobookManager.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;

namespace AudiobookManager.Test.Controllers;

/// <summary>
/// GetBookDetails backs BookSearchDialog's "paste a book URL" entry point (AGENTS.md's "Adding a
/// metadata source scraper" section). A caller-supplied URL that no configured scraper recognizes
/// must come back as a problem-details 400 the client can show the user, not a bare-message
/// exception that AddProblemDetails/UseExceptionHandler turns into an opaque 500 (see
/// ProblemResults.cs's "4xx detail *is* relayed" rule).
/// </summary>
[TestClass]
public class MetadataSearchControllerGetBookDetailsTests
{
    private static MetadataSearchController CreateController(Mock<IScrapingService> scrapingService)
    {
        return new MetadataSearchController(
            scrapingService.Object,
            new Mock<IHttpClientFactory>().Object,
            new Mock<AudiobookManager.Services.ISettingsService>().Object,
            new Mock<ILogger<MetadataSearchController>>().Object)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };
    }

    [TestMethod]
    public async Task GetBookDetails_ScraperSupportsUrl_ReturnsItsDetails()
    {
        const string url = "https://hardcover.app/books/1984";
        var scrapingService = new Mock<IScrapingService>();
        scrapingService.Setup(s => s.GetBookDetails(url))
            .ReturnsAsync(new MetadataSearchResult(url, "1984") { Source = "Hardcover" });

        var controller = CreateController(scrapingService);

        var response = await controller.GetBookDetails(new PathDto { Path = url });

        Assert.AreEqual("Hardcover", response.Value?.Source);
    }

    [TestMethod]
    public async Task GetBookDetails_NoScraperSupportsUrl_ReturnsProblemDetails400()
    {
        const string url = "https://unsupported-source.example/books/1";
        var scrapingService = new Mock<IScrapingService>();
        scrapingService.Setup(s => s.GetBookDetails(url))
            .ThrowsAsync(new ArgumentException($"No configured metadata source supports the URL '{url}'."));

        var controller = CreateController(scrapingService);

        var response = await controller.GetBookDetails(new PathDto { Path = url });

        var objectResult = response.Result as ObjectResult;
        Assert.IsNotNull(objectResult, "An unsupported URL must come back as a problem-details response, not an unhandled exception.");
        Assert.AreEqual(StatusCodes.Status400BadRequest, objectResult.StatusCode);
        var problemDetails = objectResult.Value as ProblemDetails;
        Assert.IsNotNull(problemDetails);
        StringAssert.Contains(problemDetails.Detail, url);
    }

    [TestMethod]
    public async Task GetBookDetails_UnexpectedFailure_ReturnsGenericProblemDetails500WithoutExceptionMessage()
    {
        const string url = "https://hardcover.app/books/1984";
        var scrapingService = new Mock<IScrapingService>();
        scrapingService.Setup(s => s.GetBookDetails(url))
            .ThrowsAsync(new InvalidOperationException("/data/library/secret-container-path leaked here"));

        var controller = CreateController(scrapingService);

        var response = await controller.GetBookDetails(new PathDto { Path = url });

        var objectResult = response.Result as ObjectResult;
        Assert.IsNotNull(objectResult);
        Assert.AreEqual(StatusCodes.Status500InternalServerError, objectResult.StatusCode);
        var problemDetails = objectResult.Value as ProblemDetails;
        Assert.IsNotNull(problemDetails);
        StringAssert.DoesNotMatch(problemDetails.Detail, new System.Text.RegularExpressions.Regex("secret-container-path"));
    }
}

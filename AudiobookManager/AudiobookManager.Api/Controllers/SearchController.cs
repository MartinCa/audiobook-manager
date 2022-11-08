using AudiobookManager.Scraping.Models;
using AudiobookManager.Services;
using Microsoft.AspNetCore.Mvc;

namespace AudiobookManager.Api.Controllers;
[Route("api/[controller]")]
[ApiController]
public class SearchController : ControllerBase
{
    private readonly IScrapingService _scrapingService;

    public SearchController(IScrapingService scrapingService)
    {
        _scrapingService = scrapingService;
    }

    [HttpGet("audible")]
    public async Task<IList<BookSearchResult>> SearchAudible([FromQuery] string q)
    {
        return await _scrapingService.SearchAudible(q);
    }
}

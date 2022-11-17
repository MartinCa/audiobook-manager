using AudiobookManager.Api.Dtos;
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

    [HttpGet("{sourceName}")]
    public async Task<IList<BookSearchResult>> Search(string sourceName, [FromQuery] string q)
    {
        return await _scrapingService.Search(sourceName, q);
    }

    [HttpPost("details")]
    public async Task<BookSearchResult> GetBookDetails([FromBody] PathDto dto)
    {
        return await _scrapingService.GetBookDetails(dto.Path);
    }

    [HttpGet("services")]
    public IList<string> GetSearchServices()
    {
        return _scrapingService.GetListOfScrapingServices();
    }
}

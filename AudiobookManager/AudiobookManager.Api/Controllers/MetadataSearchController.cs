using AudiobookManager.Api.Dtos;
using AudiobookManager.Scraping.Models;
using AudiobookManager.Services;
using Microsoft.AspNetCore.Mvc;

namespace AudiobookManager.Api.Controllers;
[Route("api/[controller]")]
[ApiController]
public class MetadataSearchController : ControllerBase
{
    private readonly IScrapingService _scrapingService;
    private readonly IHttpClientFactory _httpClientFactory;

    public MetadataSearchController(IScrapingService scrapingService, IHttpClientFactory httpClientFactory)
    {
        _scrapingService = scrapingService;
        _httpClientFactory = httpClientFactory;
    }

    [HttpGet("{sourceName}")]
    public async Task<IList<MetadataSearchResult>> Search(string sourceName, [FromQuery] string q)
    {
        return await _scrapingService.Search(sourceName, q);
    }

    [HttpPost("multi")]
    public async Task<MetadataMultiSourceSearchResult> SearchMultiple([FromBody] MetadataMultiSearchDto dto)
    {
        return await _scrapingService.SearchMultiple(dto.Sources, dto.Q);
    }

    [HttpPost("details")]
    public async Task<MetadataSearchResult> GetBookDetails([FromBody] PathDto dto)
    {
        return await _scrapingService.GetBookDetails(dto.Path);
    }

    [HttpGet("services")]
    public IList<MetadataSearchServiceInfo> GetSearchServices()
    {
        return _scrapingService.GetSearchServiceInfo();
    }

    [HttpGet("proxy-image")]
    public async Task<IActionResult> ProxyImage([FromQuery] string url)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || (uri.Scheme != "https" && uri.Scheme != "http"))
        {
            return BadRequest("Invalid image URL");
        }

        var client = _httpClientFactory.CreateClient();
        var response = await client.GetAsync(uri);
        if (!response.IsSuccessStatusCode)
            return StatusCode((int)response.StatusCode);

        var contentType = response.Content.Headers.ContentType?.MediaType ?? "image/jpeg";
        var bytes = await response.Content.ReadAsByteArrayAsync();
        return File(bytes, contentType);
    }
}

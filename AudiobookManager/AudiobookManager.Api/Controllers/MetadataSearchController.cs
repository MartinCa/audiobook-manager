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

        // Stream the response through instead of buffering the whole image, and honour the
        // client's cancellation so an abandoned request doesn't keep fetching upstream.
        var cancellationToken = HttpContext.RequestAborted;
        var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var statusCode = (int)response.StatusCode;
            response.Dispose();
            return StatusCode(statusCode);
        }

        var contentType = response.Content.Headers.ContentType?.MediaType ?? "image/jpeg";
        var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        HttpContext.Response.RegisterForDispose(response);
        return File(stream, contentType);
    }
}

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

    /// <summary>
    /// Fetches a cover image server-side so the browser can display one from a source that
    /// blocks hotlinking or serves it over http.
    ///
    /// KNOWN LIMITATION, accepted for now: this forwards to any http(s) URL the caller supplies,
    /// with no allowlist of scraper hosts and no block on private/link-local addresses - so
    /// anyone who can reach this API can use it to make the server issue requests on their
    /// behalf, including to hosts only the server can reach. The application has no
    /// authentication of its own and is meant to run on a trusted network, which is why it is
    /// tolerable today; if that ever changes, restrict this to the domains the registered
    /// scrapers use and reject resolved addresses outside public ranges.
    ///
    /// What is *not* tolerable, and is no longer done, is reflecting the upstream's Content-Type
    /// back to the browser. That let the supplied URL choose what this application's own origin
    /// serves - an attacker's text/html document rendered as though it came from here - which
    /// escalates the forwarding above into stored XSS. The response is now pinned to an image
    /// type and sent with nosniff; the forwarding itself is the part still accepted.
    /// </summary>
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

        // Never reflect the upstream's Content-Type. Doing so let an arbitrary URL decide what
        // this application's own origin serves: point it at a text/html document and the browser
        // renders attacker markup as though it came from here, which turns the forwarding above
        // from a request-forgery issue into stored XSS against this app. Only image types are
        // forwarded, and anything else is refused rather than guessed at.
        var upstreamContentType = response.Content.Headers.ContentType?.MediaType;
        if (!IsImageContentType(upstreamContentType))
        {
            response.Dispose();
            return StatusCode(
                StatusCodes.Status502BadGateway,
                $"The URL returned '{upstreamContentType ?? "no content type"}' rather than an image.");
        }

        var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        HttpContext.Response.RegisterForDispose(response);

        // Belt and braces: even with the check above, stop the browser sniffing a different type
        // out of the bytes than the one declared.
        HttpContext.Response.Headers.XContentTypeOptions = "nosniff";

        return File(stream, upstreamContentType!);
    }

    /// <summary>
    /// Whether a media type is an image this endpoint is willing to pass through. Deliberately the
    /// whole <c>image/</c> family rather than a fixed list - the cover sources legitimately serve
    /// jpeg, png, webp and avif - but <c>image/svg+xml</c> is excluded, because SVG is a document
    /// that can carry script and would execute on this origin.
    /// </summary>
    private static bool IsImageContentType(string? mediaType)
    {
        if (string.IsNullOrWhiteSpace(mediaType))
        {
            return false;
        }

        if (mediaType.Equals("image/svg+xml", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);
    }
}

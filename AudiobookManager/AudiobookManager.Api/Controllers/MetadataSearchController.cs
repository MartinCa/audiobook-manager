using AudiobookManager.Api.Dtos;
using AudiobookManager.Scraping;
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
    /// Still open by design: it forwards to any http(s) URL the caller supplies, with no allowlist
    /// of scraper hosts - cover art legitimately lives on every CDN and image host the user
    /// pastes, so a list of "trusted domains" would break the feature. What is no longer open is
    /// the reach into the private network: the "proxy-image" HttpClient validates the address it
    /// actually connects to (hostname resolution and every redirect hop) and refuses anything in
    /// a private, loopback, link-local or otherwise non-public range - including the cloud
    /// metadata service at 169.254.169.254 (see ProxyImageConnectGuard). Anyone who can reach this
    /// API can still make the server fetch from public hosts it may not otherwise visit; they
    /// cannot make it fetch from hosts only the server can reach.
    ///
    /// What is *not* tolerable, and is no longer done, is reflecting the upstream's Content-Type
    /// back to the browser. That let the supplied URL choose what this application's own origin
    /// serves - an attacker's text/html document rendered as though it came from here - which
    /// escalates the forwarding above into stored XSS. The response is now pinned to an image
    /// type and sent with nosniff.
    /// </summary>
    [HttpGet("proxy-image")]
    public async Task<IActionResult> ProxyImage([FromQuery] string url)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || (uri.Scheme != "https" && uri.Scheme != "http"))
        {
            return this.InvalidRequest("Invalid image URL.");
        }

        var client = _httpClientFactory.CreateClient("proxy-image");

        // Stream the response through instead of buffering the whole image, and honour the
        // client's cancellation so an abandoned request doesn't keep fetching upstream.
        var cancellationToken = HttpContext.RequestAborted;
        HttpResponseMessage response;
        try
        {
            response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (HttpRequestException ex) when (IsNonPublicAddress(ex))
        {
            // The guard refused the destination: it resolves to a private, loopback, link-local
            // or otherwise non-public address, and no amount of retrying will make a cloud
            // metadata endpoint or a LAN host public. A 4xx - the URL supplied is not something
            // this endpoint may fetch - not a 500.
            return this.InvalidRequest(
                "The URL was refused: it points at a private, loopback or otherwise non-public address.");
        }

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
    /// Whether a content type is an image this endpoint is willing to pass through. Deliberately the
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

    /// <summary>
    /// Whether a failure surfaced by the HttpClient is the proxy-image guard refusing the
    /// destination. The guard throws <see cref="NonPublicAddressException"/> from inside the
    /// connect callback, and SocketsHttpHandler wraps that in an <see cref="HttpRequestException"/>
    /// with the original as its inner exception - walk the chain so the controller can answer the
    /// one case that is a caller error rather than an upstream failure.
    /// </summary>
    private static bool IsNonPublicAddress(HttpRequestException ex)
    {
        for (Exception? inner = ex; inner is not null; inner = inner.InnerException)
        {
            if (inner is NonPublicAddressException)
            {
                return true;
            }
        }

        return false;
    }
}

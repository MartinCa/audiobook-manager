using System.Text.Json;

namespace AudiobookManager.Api.Security;

/// <summary>
/// Refuses state-changing API requests that do not carry <see cref="RequiredHeader"/>.
///
/// The application has no authentication of its own and is meant to run on a trusted network, but
/// "trusted network" does not cover the operator's own browser: a page on any site they happen to
/// visit can submit a form cross-origin to this API, and the browser sends it. CORS does not stop
/// that - it governs whether the *response* can be read, not whether the request runs - and a form
/// post is a CORS "simple request", so it is not preflighted and the side effect happens before
/// any CORS check would apply. That reached every parameterless POST here, including
/// <c>consistency/orphan-directories/resolve-all</c> (recursive directory deletion) and
/// <c>consistency/issues/resolve-by-type/MissingMediaFile</c> (deletes library records).
///
/// A header a simple request cannot set is what closes it: setting it forces the browser to
/// preflight, and the preflight fails against this application's (nonexistent) CORS policy before
/// the real request is ever sent. The frontend sets it on every request from
/// <c>client/src/lib/api.ts</c>.
///
/// Deliberately a header check rather than a Content-Type check: many endpoints here legitimately
/// take no request body at all (every "start this background operation" POST, and every DELETE),
/// so those requests carry no Content-Type to check.
/// </summary>
public class CrossSiteRequestGuardMiddleware
{
    public const string RequiredHeader = "X-Requested-With";
    public const string RequiredHeaderValue = "XMLHttpRequest";

    private const string GuardedPathPrefix = "/api";

    private static readonly HashSet<string> StateChangingMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        HttpMethods.Post,
        HttpMethods.Put,
        HttpMethods.Patch,
        HttpMethods.Delete,
    };

    private readonly RequestDelegate _next;
    private readonly ILogger<CrossSiteRequestGuardMiddleware> _logger;

    public CrossSiteRequestGuardMiddleware(RequestDelegate next, ILogger<CrossSiteRequestGuardMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!RequiresGuard(context.Request))
        {
            await _next(context);
            return;
        }

        if (HasRequiredHeader(context.Request))
        {
            await _next(context);
            return;
        }

        _logger.LogWarning(
            "Refused {Method} {Path}: missing the {Header} header. A request from this application always carries it, "
            + "so this is either a cross-site request or a client that needs to set it.",
            context.Request.Method, context.Request.Path, RequiredHeader);

        await WriteForbiddenAsync(context);
    }

    /// <summary>
    /// Only API writes are guarded. The SignalR hub is left alone deliberately: its negotiate POST
    /// is issued by the SignalR client rather than this application's fetch wrapper, and all it
    /// returns is a connection token the caller cannot read cross-origin.
    /// </summary>
    private static bool RequiresGuard(HttpRequest request) =>
        StateChangingMethods.Contains(request.Method)
        && request.Path.StartsWithSegments(GuardedPathPrefix, StringComparison.OrdinalIgnoreCase);

    private static bool HasRequiredHeader(HttpRequest request) =>
        request.Headers.TryGetValue(RequiredHeader, out var values)
        && values.Any(value => string.Equals(value, RequiredHeaderValue, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Written by hand rather than through the MVC problem-details pipeline: this runs as
    /// middleware, before routing has picked an action, so there is no result pipeline to hand a
    /// <c>ProblemDetails</c> to yet.
    /// </summary>
    private static async Task WriteForbiddenAsync(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        context.Response.ContentType = "application/problem+json";

        var problem = new
        {
            type = "https://developer.mozilla.org/en-US/docs/Web/HTTP/Status/403",
            title = "Forbidden",
            status = StatusCodes.Status403Forbidden,
            detail = $"State-changing requests must send the '{RequiredHeader}: {RequiredHeaderValue}' header.",
            instance = context.Request.Path.Value,
        };

        await context.Response.WriteAsync(JsonSerializer.Serialize(problem));
    }
}

public static class CrossSiteRequestGuardMiddlewareExtensions
{
    public static IApplicationBuilder UseCrossSiteRequestGuard(this IApplicationBuilder app) =>
        app.UseMiddleware<CrossSiteRequestGuardMiddleware>();
}

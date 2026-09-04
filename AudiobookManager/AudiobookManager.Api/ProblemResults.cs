using Microsoft.AspNetCore.Mvc;

namespace AudiobookManager.Api;

/// <summary>
/// Shorthand for the error shapes the controllers return, so the whole API answers in RFC 9457
/// <c>application/problem+json</c> and the client's parser (<c>client/src/lib/api.ts</c>) can
/// actually read the message.
///
/// A bare <c>BadRequest("...")</c> or <c>StatusCode(500, ex.Message)</c> serializes the string as
/// <c>text/plain</c>, and the client reads a body only when the content type says json - so every
/// hand-written message was silently dropped and rendered as "Request failed with status 400".
/// These helpers exist to make the right thing shorter than the wrong one.
/// </summary>
public static class ProblemResults
{
    /// <summary>
    /// What a caller is told when an unhandled exception reaches a controller's catch block.
    ///
    /// Deliberately says nothing about what failed. The exception messages this replaces carried
    /// absolute container paths, filesystem layout and .NET type detail straight to the caller;
    /// on a trusted LAN that is close to harmless, but it is free to stop doing and it is the same
    /// surface as the cross-site and SSRF work. The detail stays in the log, and the traceId in
    /// the response body is what ties the two together.
    /// </summary>
    public const string UnexpectedErrorDetail =
        "Something went wrong while handling this request. The details are in the application log - "
        + "search it for the traceId in this response.";

    /// <summary>The caller sent something this endpoint cannot act on. The reason is safe to relay.</summary>
    public static ObjectResult InvalidRequest(this ControllerBase controller, string detail, string title = "Invalid request")
        => controller.Problem(detail: detail, statusCode: StatusCodes.Status400BadRequest, title: title);

    /// <summary>
    /// The request is well-formed but the application is not in a state to carry it out - a save
    /// already in flight, a library that is not mounted, a queue entry that already exists.
    /// </summary>
    public static ObjectResult ConflictingState(this ControllerBase controller, string detail, string title)
        => controller.Problem(detail: detail, statusCode: StatusCodes.Status409Conflict, title: title);

    /// <summary>The path or resource is outside what this application is configured to touch.</summary>
    public static ObjectResult AccessDenied(this ControllerBase controller, string detail)
        => controller.Problem(detail: detail, statusCode: StatusCodes.Status403Forbidden, title: "Access denied");

    /// <summary>
    /// Something the caller cannot do anything about. Log the exception with its context before
    /// calling this - the response deliberately does not carry it.
    /// </summary>
    public static ObjectResult UnexpectedError(this ControllerBase controller, string title = "Unexpected error")
        => controller.Problem(
            detail: UnexpectedErrorDetail,
            statusCode: StatusCodes.Status500InternalServerError,
            title: title);
}

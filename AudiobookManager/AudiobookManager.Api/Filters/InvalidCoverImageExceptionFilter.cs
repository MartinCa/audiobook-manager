using AudiobookManager.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace AudiobookManager.Api.Filters;

/// <summary>
/// Turns a rejected cover into a 400 the client can show.
///
/// A filter rather than a try/catch in each action because the four endpoints that accept a cover
/// (organize, save, and the two path previews) return four different types - <c>string</c>,
/// <c>Task&lt;string&gt;</c>, a DTO - and wrapping each in an ActionResult purely to carry this one
/// failure would change every one of their signatures for it.
/// </summary>
public class InvalidCoverImageExceptionFilter : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        if (context.Exception is not InvalidCoverImageException exception)
        {
            return;
        }

        // The caller sent it, so the caller can act on it: the message names what is wrong with
        // the image rather than describing a server fault.
        context.Result = new ObjectResult(new ProblemDetails
        {
            Title = "Invalid cover image",
            Detail = exception.Message,
            Status = StatusCodes.Status400BadRequest,
        })
        {
            StatusCode = StatusCodes.Status400BadRequest,
        };

        context.ExceptionHandled = true;
    }
}

using Microsoft.AspNetCore.Mvc;

namespace AudiobookManager.Test.Controllers;

/// <summary>
/// Reads an error response as what it now is: RFC 9457 problem details.
///
/// Before, the controllers returned bare strings, so tests asserted on
/// <c>BadRequestObjectResult.Value</c> being a particular string. That assertion passed while the
/// client silently discarded the message - it only parses a body whose content type says json,
/// and a string body is text/plain. Asserting the problem shape is what actually pins the contract
/// the client reads.
/// </summary>
internal static class ProblemAssert
{
    /// <summary>Asserts the result is a problem response with the given status, and returns it.</summary>
    public static ProblemDetails HasStatus(IActionResult? result, int expectedStatus)
    {
        var objectResult = result as ObjectResult;
        Assert.IsNotNull(objectResult, $"Expected an ObjectResult carrying ProblemDetails, got {result?.GetType().Name ?? "null"}.");

        var problem = objectResult.Value as ProblemDetails;
        Assert.IsNotNull(problem, $"Expected a ProblemDetails body, got {objectResult.Value?.GetType().Name ?? "null"}.");

        Assert.AreEqual(expectedStatus, objectResult.StatusCode);

        // The status belongs in the body too - the client reads ApiError.status from the response,
        // but anything logging the payload alone should not have to guess.
        Assert.AreEqual(expectedStatus, problem.Status);
        return problem;
    }

    /// <summary>Asserts the status and that the message the user will see is the expected one.</summary>
    public static void HasDetail(IActionResult? result, int expectedStatus, string expectedDetail)
    {
        var problem = HasStatus(result, expectedStatus);
        Assert.AreEqual(expectedDetail, problem.Detail);
    }
}

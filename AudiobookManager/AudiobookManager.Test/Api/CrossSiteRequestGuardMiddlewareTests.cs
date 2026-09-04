using System.Text;
using AudiobookManager.Api.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace AudiobookManager.Test.Api;

[TestClass]
public class CrossSiteRequestGuardMiddlewareTests
{
    private static async Task<(int StatusCode, bool ReachedNext, string Body)> InvokeAsync(
        string method,
        string path,
        bool withHeader,
        string? headerValue = null)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.Path = path;
        if (withHeader)
        {
            context.Request.Headers[CrossSiteRequestGuardMiddleware.RequiredHeader] =
                headerValue ?? CrossSiteRequestGuardMiddleware.RequiredHeaderValue;
        }

        var responseBody = new MemoryStream();
        context.Response.Body = responseBody;

        var reachedNext = false;
        var middleware = new CrossSiteRequestGuardMiddleware(
            _ =>
            {
                reachedNext = true;
                return Task.CompletedTask;
            },
            NullLogger<CrossSiteRequestGuardMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        responseBody.Position = 0;
        var body = Encoding.UTF8.GetString(responseBody.ToArray());

        return (context.Response.StatusCode, reachedNext, body);
    }

    [TestMethod]
    [DataRow("POST")]
    [DataRow("PUT")]
    [DataRow("PATCH")]
    [DataRow("DELETE")]
    public async Task StateChangingApiRequest_WithoutHeader_IsRefused(string method)
    {
        var (statusCode, reachedNext, _) = await InvokeAsync(method, "/api/consistency/check", withHeader: false);

        Assert.AreEqual(StatusCodes.Status403Forbidden, statusCode);
        Assert.IsFalse(reachedNext, "The request must not reach the endpoint.");
    }

    [TestMethod]
    [DataRow("POST")]
    [DataRow("PUT")]
    [DataRow("PATCH")]
    [DataRow("DELETE")]
    public async Task StateChangingApiRequest_WithHeader_IsAllowed(string method)
    {
        var (_, reachedNext, _) = await InvokeAsync(method, "/api/consistency/check", withHeader: true);

        Assert.IsTrue(reachedNext);
    }

    [TestMethod]
    public async Task HeaderValue_IsMatchedCaseInsensitively()
    {
        var (_, reachedNext, _) = await InvokeAsync("POST", "/api/library/scan", withHeader: true, headerValue: "xmlhttprequest");

        Assert.IsTrue(reachedNext);
    }

    [TestMethod]
    public async Task HeaderWithUnexpectedValue_IsRefused()
    {
        var (statusCode, reachedNext, _) = await InvokeAsync("POST", "/api/library/scan", withHeader: true, headerValue: "something-else");

        Assert.AreEqual(StatusCodes.Status403Forbidden, statusCode);
        Assert.IsFalse(reachedNext);
    }

    [TestMethod]
    public async Task GetRequest_WithoutHeader_IsAllowed()
    {
        // Reads are not the concern here, and a browser issues plenty of them without the header.
        var (_, reachedNext, _) = await InvokeAsync("GET", "/api/consistency/issues", withHeader: false);

        Assert.IsTrue(reachedNext);
    }

    [TestMethod]
    public async Task NonApiPost_WithoutHeader_IsAllowed()
    {
        // The SignalR negotiate POST is issued by the SignalR client, not the app's fetch wrapper.
        var (_, reachedNext, _) = await InvokeAsync("POST", "/hubs/organize/negotiate", withHeader: false);

        Assert.IsTrue(reachedNext);
    }

    [TestMethod]
    public async Task PathPrefixIsMatchedOnSegments_NotAsBareString()
    {
        // "/apixyz" is not inside "/api" and must not be guarded as though it were - the same
        // boundary reasoning as PathStartsWith.
        var (_, reachedNext, _) = await InvokeAsync("POST", "/apixyz/thing", withHeader: false);

        Assert.IsTrue(reachedNext);
    }

    [TestMethod]
    public async Task RefusedRequest_RespondsWithProblemJson()
    {
        var (_, _, body) = await InvokeAsync(
            "POST", "/api/consistency/orphan-directories/resolve-all", withHeader: false);

        StringAssert.Contains(body, "\"status\":403");
        StringAssert.Contains(body, CrossSiteRequestGuardMiddleware.RequiredHeader);
    }
}

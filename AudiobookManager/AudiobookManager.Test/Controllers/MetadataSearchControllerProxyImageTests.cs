using System.Net;
using AudiobookManager.Api.Controllers;
using AudiobookManager.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace AudiobookManager.Test.Controllers;

/// <summary>
/// The proxy forwards to any http(s) URL the caller supplies - a limitation the endpoint
/// documents and accepts for a trusted-network deployment. What it must not do is let that URL
/// decide what this application's own origin serves: reflecting the upstream Content-Type meant
/// an attacker's text/html document rendered as though it came from here.
/// </summary>
[TestClass]
public class MetadataSearchControllerProxyImageTests
{
    private class StubHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string? _contentType;

        public StubHandler(HttpStatusCode status, string? contentType)
        {
            _status = status;
            _contentType = contentType;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var content = new ByteArrayContent(new byte[] { 1, 2, 3, 4 });
            content.Headers.Remove("Content-Type");
            if (_contentType is not null)
            {
                content.Headers.TryAddWithoutValidation("Content-Type", _contentType);
            }

            return Task.FromResult(new HttpResponseMessage(_status) { Content = content });
        }
    }

    private static MetadataSearchController CreateController(HttpStatusCode status, string? contentType)
    {
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(new HttpClient(new StubHandler(status, contentType)));

        return new MetadataSearchController(new Mock<IScrapingService>().Object, factory.Object)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };
    }

    [TestMethod]
    [DataRow("image/jpeg")]
    [DataRow("image/png")]
    [DataRow("image/webp")]
    [DataRow("image/avif")]
    public async Task ProxyImage_ImageContentType_IsForwardedUnchanged(string contentType)
    {
        var controller = CreateController(HttpStatusCode.OK, contentType);

        var result = await controller.ProxyImage("https://example.com/cover.jpg");

        var fileResult = (FileStreamResult)result;
        Assert.AreEqual(contentType, fileResult.ContentType);
        Assert.AreEqual("nosniff", controller.HttpContext.Response.Headers.XContentTypeOptions.ToString());
    }

    [TestMethod]
    [DataRow("text/html")]
    [DataRow("application/javascript")]
    [DataRow("text/plain")]
    [DataRow("application/octet-stream")]
    public async Task ProxyImage_NonImageContentType_IsRefused(string contentType)
    {
        var controller = CreateController(HttpStatusCode.OK, contentType);

        var result = await controller.ProxyImage("https://evil.example/payload");

        var statusResult = (ObjectResult)result;
        Assert.AreEqual(StatusCodes.Status502BadGateway, statusResult.StatusCode);
    }

    [TestMethod]
    public async Task ProxyImage_SvgContentType_IsRefused()
    {
        // SVG is in the image/ family but is a document that can carry script, so it would
        // execute on this origin.
        var controller = CreateController(HttpStatusCode.OK, "image/svg+xml");

        var result = await controller.ProxyImage("https://evil.example/payload.svg");

        Assert.AreEqual(StatusCodes.Status502BadGateway, ((ObjectResult)result).StatusCode);
    }

    [TestMethod]
    public async Task ProxyImage_NoContentType_IsRefused()
    {
        // Previously this defaulted to image/jpeg and forwarded the bytes anyway.
        var controller = CreateController(HttpStatusCode.OK, contentType: null);

        var result = await controller.ProxyImage("https://example.com/mystery");

        Assert.AreEqual(StatusCodes.Status502BadGateway, ((ObjectResult)result).StatusCode);
    }

    [TestMethod]
    [DataRow("javascript:alert(1)")]
    [DataRow("file:///etc/passwd")]
    [DataRow("not a url")]
    [DataRow("")]
    public async Task ProxyImage_NonHttpUrl_IsRejected(string url)
    {
        var controller = CreateController(HttpStatusCode.OK, "image/jpeg");

        var result = await controller.ProxyImage(url);

        Assert.IsInstanceOfType<BadRequestObjectResult>(result);
    }
}

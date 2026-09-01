using System.Collections;
using AudiobookManager.Api.Controllers;
using AudiobookManager.Api.Dtos;
using AudiobookManager.Domain;
using AudiobookManager.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace AudiobookManager.Test.Controllers;

[TestClass]
public class FilesControllerTests
{
    private Mock<IFileService> _fileService = null!;
    private FilesController _controller = null!;

    [TestInitialize]
    public void Setup()
    {
        _fileService = new Mock<IFileService>();
        _controller = new FilesController(_fileService.Object);
    }

    [TestMethod]
    public void GetDirectoryContents_ValidPath_ReturnsOkWithContents()
    {
        var expected = new List<AudiobookFileInfo>
        {
            new("/path/book.m4b", "book.m4b", 1000)
        };
        _fileService.Setup(s => s.GetDirectoryContents("/path")).Returns(expected);

        var result = _controller.GetDirectoryContents(new PathDto { Path = "/path" });

        var okResult = result.Result as OkObjectResult;
        Assert.IsNotNull(okResult);
        Assert.AreEqual(StatusCodes.Status200OK, okResult.StatusCode);
        CollectionAssert.AreEqual(expected, (ICollection)okResult.Value!);
    }

    [TestMethod]
    public void GetDirectoryContents_UnauthorizedPath_Returns403Forbidden()
    {
        _fileService.Setup(s => s.GetDirectoryContents("/unauthorized"))
            .Throws(new UnauthorizedAccessException("Access not allowed"));

        var result = _controller.GetDirectoryContents(new PathDto { Path = "/unauthorized" });

        var objResult = result.Result as ObjectResult;
        Assert.IsNotNull(objResult);
        Assert.AreEqual(StatusCodes.Status403Forbidden, objResult.StatusCode);
        Assert.AreEqual("Access not allowed", objResult.Value);
    }

    [TestMethod]
    public void GetDirectoryContents_InvalidOperation_Returns400BadRequest()
    {
        _fileService.Setup(s => s.GetDirectoryContents("/root"))
            .Throws(new InvalidOperationException("Cannot inspect root"));

        var result = _controller.GetDirectoryContents(new PathDto { Path = "/root" });

        var badRequest = result.Result as BadRequestObjectResult;
        Assert.IsNotNull(badRequest);
        Assert.AreEqual(StatusCodes.Status400BadRequest, badRequest.StatusCode);
        Assert.AreEqual("Cannot inspect root", badRequest.Value);
    }

    [TestMethod]
    public void DeleteDirectory_ValidPath_ReturnsOk()
    {
        _fileService.Setup(s => s.DeleteDirectory("/path"));

        var result = _controller.DeleteDirectory(new PathDto { Path = "/path" });

        var okResult = result as OkResult;
        Assert.IsNotNull(okResult);
        Assert.AreEqual(StatusCodes.Status200OK, okResult.StatusCode);
    }

    [TestMethod]
    public void DeleteDirectory_UnauthorizedPath_Returns403Forbidden()
    {
        _fileService.Setup(s => s.DeleteDirectory("/unauthorized"))
            .Throws(new UnauthorizedAccessException("Access not allowed"));

        var result = _controller.DeleteDirectory(new PathDto { Path = "/unauthorized" });

        var objResult = result as ObjectResult;
        Assert.IsNotNull(objResult);
        Assert.AreEqual(StatusCodes.Status403Forbidden, objResult.StatusCode);
        Assert.AreEqual("Access not allowed", objResult.Value);
    }

    [TestMethod]
    public void DeleteDirectory_InvalidOperation_Returns400BadRequest()
    {
        _fileService.Setup(s => s.DeleteDirectory("/root"))
            .Throws(new InvalidOperationException("Cannot delete root"));

        var result = _controller.DeleteDirectory(new PathDto { Path = "/root" });

        var badRequest = result as BadRequestObjectResult;
        Assert.IsNotNull(badRequest);
        Assert.AreEqual(StatusCodes.Status400BadRequest, badRequest.StatusCode);
        Assert.AreEqual("Cannot delete root", badRequest.Value);
    }
}

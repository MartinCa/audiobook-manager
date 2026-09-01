using System.Collections;
using AudiobookManager.Api.Controllers;
using AudiobookManager.Api.Dtos;
using AudiobookManager.Domain;
using AudiobookManager.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AudiobookManager.Test.Controllers;

[TestClass]
public class FilesControllerTests
{
    private Mock<IFileService> _fileService = null!;
    private Mock<ILogger<FilesController>> _logger = null!;
    private FilesController _controller = null!;

    [TestInitialize]
    public void Setup()
    {
        _fileService = new Mock<IFileService>();
        _logger = new Mock<ILogger<FilesController>>();
        _controller = new FilesController(_fileService.Object, _logger.Object)
        {
            // GetCover sets a response header, which needs a real HttpContext behind it -
            // ControllerBase.Response is null without one.
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };
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
    public void GetCover_ExistingFile_ReturnsPhysicalFileResult()
    {
        var tempDir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var coverPath = Path.Combine(tempDir, "cover.jpg");
            File.WriteAllBytes(coverPath, [0xFF, 0xD8, 0xFF]);
            _fileService.Setup(s => s.GetCoverPath("/discovered/book.m4b")).Returns(coverPath);

            var result = _controller.GetCover("/discovered/book.m4b");

            var fileResult = result as PhysicalFileResult;
            Assert.IsNotNull(fileResult);
            Assert.AreEqual("image/jpeg", fileResult.ContentType);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [TestMethod]
    public void GetCover_NoCoverFound_Returns404NotFound()
    {
        _fileService.Setup(s => s.GetCoverPath("/discovered/book.m4b")).Returns((string?)null);

        var result = _controller.GetCover("/discovered/book.m4b");

        Assert.IsInstanceOfType<NotFoundResult>(result);
    }

    [TestMethod]
    public void GetCover_CoverFileNoLongerOnDisk_Returns404NotFound()
    {
        // GetCoverPath resolved a path (the file existed when it checked), but the file was
        // deleted between that check and this request - must not throw serving a stale path.
        _fileService.Setup(s => s.GetCoverPath("/discovered/book.m4b"))
            .Returns("/discovered/cover.jpg");

        var result = _controller.GetCover("/discovered/book.m4b");

        Assert.IsInstanceOfType<NotFoundResult>(result);
    }

    [TestMethod]
    public void GetCover_UnauthorizedPath_Returns403Forbidden()
    {
        _fileService.Setup(s => s.GetCoverPath("/unauthorized/book.m4b"))
            .Throws(new UnauthorizedAccessException("Access not allowed"));

        var result = _controller.GetCover("/unauthorized/book.m4b");

        var objResult = result as ObjectResult;
        Assert.IsNotNull(objResult);
        Assert.AreEqual(StatusCodes.Status403Forbidden, objResult.StatusCode);
        Assert.AreEqual("Access not allowed", objResult.Value);
    }

    [TestMethod]
    public void DeleteDirectory_ValidPath_ReturnsOk()
    {
        _fileService.Setup(s => s.DeleteDirectory("/path"));

        var result = _controller.DeleteDirectory(new PathDto { Path = "/path" });

        var okResult = result as OkResult;
        Assert.IsNotNull(okResult);
        Assert.AreEqual(StatusCodes.Status200OK, okResult.StatusCode);
        _logger.Verify(
            l => l.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Delete directory requested for path '/path'")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
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

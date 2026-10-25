using AudiobookManager.Api.Controllers;
using AudiobookManager.Api.Dtos;
using AudiobookManager.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace AudiobookManager.Test.Controllers;

[TestClass]
public class SettingsControllerMetadataSourceOrderTests
{
    private readonly Mock<ISettingsService> _service = new();

    private SettingsController Controller() =>
        new(_service.Object, Mock.Of<IScheduledTaskService>(), Mock.Of<IQualifierIndicatorService>());

    [TestMethod]
    public async Task UpdateMetadataSourceOrder_Valid_SavesAndReturnsTheStoredOrder()
    {
        _service.Setup(s => s.UpdateMetadataSourceOrder(It.IsAny<IReadOnlyList<string>>()))
            .ReturnsAsync(new[] { "Hardcover", "Audible" });

        var result = await Controller().UpdateMetadataSourceOrder(new MetadataSourceOrderDto(new[] { "Hardcover", "Audible" }));

        var dto = (result.Result as OkObjectResult)?.Value as MetadataSourceOrderDto;
        Assert.IsNotNull(dto);
        CollectionAssert.AreEqual(new[] { "Hardcover", "Audible" }, dto.Sources.ToList());
    }

    [TestMethod]
    public async Task UpdateMetadataSourceOrder_MissingList_IsA400()
    {
        var result = await Controller().UpdateMetadataSourceOrder(new MetadataSourceOrderDto(null!));

        Assert.AreEqual(StatusCodes.Status400BadRequest, (result.Result as ObjectResult)?.StatusCode);
        _service.Verify(s => s.UpdateMetadataSourceOrder(It.IsAny<IReadOnlyList<string>>()), Times.Never);
    }

    [TestMethod]
    public async Task UpdateMetadataSourceOrder_RefusedByTheService_RelaysItsMessageAs400()
    {
        _service.Setup(s => s.UpdateMetadataSourceOrder(It.IsAny<IReadOnlyList<string>>()))
            .ThrowsAsync(new ArgumentException("A source order can list at most 50 sources."));

        var result = await Controller().UpdateMetadataSourceOrder(new MetadataSourceOrderDto(new[] { "x" }));

        var problem = result.Result as ObjectResult;
        Assert.AreEqual(StatusCodes.Status400BadRequest, problem?.StatusCode);
        StringAssert.Contains(((ProblemDetails)problem!.Value!).Detail!, "at most 50");
    }
}

using AudiobookManager.Api.Controllers;
using AudiobookManager.Api.Dtos;
using AudiobookManager.Services;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace AudiobookManager.Test.Controllers;

[TestClass]
public class SettingsControllerNarratorInPathTests
{
    private static (SettingsController Controller, Mock<ISettingsService> Service) Make(bool stored)
    {
        var service = new Mock<ISettingsService>();
        service.Setup(s => s.GetLibrarySettings())
            .ReturnsAsync(new Domain.LibrarySettings { IncludeNarratorInPath = stored });
        service.Setup(s => s.UpdateLibrarySettings(It.IsAny<Domain.LibrarySettings>()))
            .ReturnsAsync((Domain.LibrarySettings s) => s);
        return (new SettingsController(service.Object, Mock.Of<IScheduledTaskService>(), Mock.Of<IQualifierIndicatorService>()), service);
    }

    [TestMethod]
    public async Task GetLibrarySettings_ReportsTheStoredValue()
    {
        var (controller, _) = Make(stored: true);

        var ok = Assert.IsInstanceOfType<OkObjectResult>((await controller.GetLibrarySettings()).Result);

        Assert.IsTrue(Assert.IsInstanceOfType<LibrarySettingsDto>(ok.Value).IncludeNarratorInPath);
    }

    [TestMethod]
    public async Task UpdateLibrarySettings_SavesTheRequestedValue()
    {
        var (controller, service) = Make(stored: false);

        await controller.UpdateLibrarySettings(
            new UpdateLibrarySettingsDto("Spaced", null, 1000, null, null, null, null, true));

        service.Verify(s => s.UpdateLibrarySettings(
            It.Is<Domain.LibrarySettings>(v => v.IncludeNarratorInPath)), Times.Once);
    }

    [TestMethod]
    public async Task UpdateLibrarySettings_OmittedValue_KeepsTheStoredOne_SoAnOlderClientCannotMoveTheLibrary()
    {
        var (controller, service) = Make(stored: true);

        await controller.UpdateLibrarySettings(new UpdateLibrarySettingsDto("Spaced", null, 1000, null, null, null));

        service.Verify(s => s.UpdateLibrarySettings(
            It.Is<Domain.LibrarySettings>(v => v.IncludeNarratorInPath)), Times.Once);
    }

    [TestMethod]
    public async Task UpdateLibrarySettings_ExplicitFalse_TurnsItOff()
    {
        var (controller, service) = Make(stored: true);

        await controller.UpdateLibrarySettings(
            new UpdateLibrarySettingsDto("Spaced", null, 1000, null, null, null, null, false));

        service.Verify(s => s.UpdateLibrarySettings(
            It.Is<Domain.LibrarySettings>(v => !v.IncludeNarratorInPath)), Times.Once);
    }
}

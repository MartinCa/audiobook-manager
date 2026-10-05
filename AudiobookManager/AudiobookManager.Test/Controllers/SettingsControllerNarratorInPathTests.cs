using AudiobookManager.Api.Controllers;
using AudiobookManager.Api.Dtos;
using AudiobookManager.Services;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace AudiobookManager.Test.Controllers;

[TestClass]
public class SettingsControllerNarratorInPathTests
{
    private static (SettingsController Controller, Mock<ISettingsService> Service) Make(bool stored, int storedMax = 3)
    {
        var service = new Mock<ISettingsService>();
        service.Setup(s => s.GetLibrarySettings())
            .ReturnsAsync(new Domain.LibrarySettings { IncludeNarratorInPath = stored, MaxNarratorsInPath = storedMax });
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

    [TestMethod]
    public async Task GetLibrarySettings_ReportsTheStoredNarratorLimit()
    {
        var (controller, _) = Make(stored: true, storedMax: 2);

        var ok = Assert.IsInstanceOfType<OkObjectResult>((await controller.GetLibrarySettings()).Result);

        Assert.AreEqual(2, Assert.IsInstanceOfType<LibrarySettingsDto>(ok.Value).MaxNarratorsInPath);
    }

    [TestMethod]
    public async Task UpdateLibrarySettings_SavesTheRequestedNarratorLimit()
    {
        var (controller, service) = Make(stored: true);

        await controller.UpdateLibrarySettings(
            new UpdateLibrarySettingsDto("Spaced", null, 1000, null, null, null, null, null, 5));

        service.Verify(s => s.UpdateLibrarySettings(
            It.Is<Domain.LibrarySettings>(v => v.MaxNarratorsInPath == 5)), Times.Once);
    }

    [TestMethod]
    public async Task UpdateLibrarySettings_OmittedNarratorLimit_KeepsTheStoredOne()
    {
        var (controller, service) = Make(stored: true, storedMax: 7);

        await controller.UpdateLibrarySettings(new UpdateLibrarySettingsDto("Spaced", null, 1000, null, null, null));

        service.Verify(s => s.UpdateLibrarySettings(
            It.Is<Domain.LibrarySettings>(v => v.MaxNarratorsInPath == 7)), Times.Once);
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(-1)]
    [DataRow(11)]
    public async Task UpdateLibrarySettings_NarratorLimitOutsideOneToTen_IsRefusedWithoutSaving(int limit)
    {
        var (controller, service) = Make(stored: true);

        var result = await controller.UpdateLibrarySettings(
            new UpdateLibrarySettingsDto("Spaced", null, 1000, null, null, null, null, null, limit));

        var problem = Assert.IsInstanceOfType<ObjectResult>(result.Result);
        Assert.AreEqual(400, problem.StatusCode);
        service.Verify(s => s.UpdateLibrarySettings(It.IsAny<Domain.LibrarySettings>()), Times.Never);
    }

    [TestMethod]
    [DataRow(1)]
    [DataRow(10)]
    public async Task UpdateLibrarySettings_NarratorLimitAtTheBounds_IsAccepted(int limit)
    {
        var (controller, service) = Make(stored: true);

        await controller.UpdateLibrarySettings(
            new UpdateLibrarySettingsDto("Spaced", null, 1000, null, null, null, null, null, limit));

        service.Verify(s => s.UpdateLibrarySettings(
            It.Is<Domain.LibrarySettings>(v => v.MaxNarratorsInPath == limit)), Times.Once);
    }

    [TestMethod]
    public void NarratorsInPath_IsZeroWhenOff_AndTheLimitWhenOn()
    {
        Assert.AreEqual(0, new Domain.LibrarySettings { IncludeNarratorInPath = false, MaxNarratorsInPath = 4 }.NarratorsInPath);
        Assert.AreEqual(4, new Domain.LibrarySettings { IncludeNarratorInPath = true, MaxNarratorsInPath = 4 }.NarratorsInPath);
        Assert.AreEqual(1, new Domain.LibrarySettings().MaxNarratorsInPath);
    }
}

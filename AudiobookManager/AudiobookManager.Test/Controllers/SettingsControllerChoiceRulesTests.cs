using AudiobookManager.Api.Controllers;
using AudiobookManager.Api.Dtos;
using AudiobookManager.Domain;
using AudiobookManager.Services;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace AudiobookManager.Test.Controllers;

/// <summary>GET/PUT api/settings/metadata-choice-rules.</summary>
[TestClass]
public class SettingsControllerChoiceRulesTests
{
    private static (SettingsController Controller, Mock<ISettingsService> Service) Make(MetadataChoiceRuleSet? stored = null)
    {
        var service = new Mock<ISettingsService>();
        service.Setup(s => s.GetMetadataChoiceRules()).ReturnsAsync(stored ?? MetadataChoiceRuleSet.Defaults);
        service.Setup(s => s.UpdateMetadataChoiceRules(It.IsAny<IReadOnlyDictionary<string, ChoiceRule>>()))
            .ReturnsAsync((IReadOnlyDictionary<string, ChoiceRule> r) => MetadataChoiceRuleSet.From(r));
        return (new SettingsController(service.Object, Mock.Of<IScheduledTaskService>(), Mock.Of<IQualifierIndicatorService>()), service);
    }

    [TestMethod]
    public async Task Get_ServesEveryChoiceWithItsOptionsAndWarning_SoTheClientHoldsNoList()
    {
        var (controller, _) = Make();

        var ok = Assert.IsInstanceOfType<OkObjectResult>((await controller.GetMetadataChoiceRules()).Result);
        var dto = Assert.IsInstanceOfType<MetadataChoiceRulesDto>(ok.Value);

        CollectionAssert.AreEqual(
            new[] { MetadataChoiceKinds.MainAuthor, MetadataChoiceKinds.PrimarySeries }, dto.Choices.Select(c => c.Choice).ToArray());
        var author = dto.Choices[0];
        Assert.AreEqual("KeepCurrent", author.WhenSourceNamesMain);
        Assert.AreEqual("KeepCurrent", author.WhenSourceSilent);
        CollectionAssert.AreEqual(
            new[] { "KeepCurrent", "UseSources", "AskMe" }, author.NamesMainOptions.Select(o => o.Key).ToArray());
        CollectionAssert.AreEqual(
            new[] { "KeepCurrent", "FirstListed", "None", "AskMe" }, author.SilentOptions.Select(o => o.Key).ToArray());
        CollectionAssert.AreEqual(
            new[] { "KeepCurrent", "FirstListed", "AskMe" }, dto.Choices[1].SilentOptions.Select(o => o.Key).ToArray());
        Assert.IsNotNull(dto.Choices[1].Warning);
    }

    [TestMethod]
    public async Task Put_SavesTheRulesAndReturnsTheResult()
    {
        var (controller, service) = Make();

        var result = await controller.UpdateMetadataChoiceRules(new UpdateMetadataChoiceRulesDto(new[]
        {
            new MetadataChoiceRuleInputDto("MainAuthor", "UseSources", "FirstListed"),
        }));

        var dto = Assert.IsInstanceOfType<MetadataChoiceRulesDto>(Assert.IsInstanceOfType<OkObjectResult>(result.Result).Value);
        Assert.AreEqual("UseSources", dto.Choices[0].WhenSourceNamesMain);
        Assert.AreEqual("FirstListed", dto.Choices[0].WhenSourceSilent);
        service.Verify(s => s.UpdateMetadataChoiceRules(It.Is<IReadOnlyDictionary<string, ChoiceRule>>(r =>
            r[MetadataChoiceKinds.MainAuthor] == new ChoiceRule(SourceNamesMainRule.UseSources, SourceSilentRule.FirstListed))), Times.Once);
    }

    [TestMethod]
    [DataRow("Sometimes", "KeepCurrent")]
    [DataRow("KeepCurrent", "Sometimes")]
    [DataRow("usesources", "KeepCurrent")]
    public async Task Put_UnknownOption_Is400WithoutSaving(string named, string silent)
    {
        var (controller, service) = Make();

        var result = await controller.UpdateMetadataChoiceRules(new UpdateMetadataChoiceRulesDto(new[]
        {
            new MetadataChoiceRuleInputDto("MainAuthor", named, silent),
        }));

        Assert.AreEqual(400, Assert.IsInstanceOfType<ObjectResult>(result.Result).StatusCode);
        service.Verify(s => s.UpdateMetadataChoiceRules(It.IsAny<IReadOnlyDictionary<string, ChoiceRule>>()), Times.Never);
    }

    [TestMethod]
    public async Task Put_ARepeatedChoice_Is400()
    {
        var (controller, _) = Make();

        var result = await controller.UpdateMetadataChoiceRules(new UpdateMetadataChoiceRulesDto(new[]
        {
            new MetadataChoiceRuleInputDto("MainAuthor", "KeepCurrent", "KeepCurrent"),
            new MetadataChoiceRuleInputDto("MainAuthor", "UseSources", "KeepCurrent"),
        }));

        Assert.AreEqual(400, Assert.IsInstanceOfType<ObjectResult>(result.Result).StatusCode);
    }

    [TestMethod]
    public async Task Put_AnOptionTheChoiceCannotTake_ComesBackFromTheServiceAs400()
    {
        var (controller, service) = Make();
        service.Setup(s => s.UpdateMetadataChoiceRules(It.IsAny<IReadOnlyDictionary<string, ChoiceRule>>()))
            .ThrowsAsync(new ArgumentException("Primary series cannot use None."));

        var result = await controller.UpdateMetadataChoiceRules(new UpdateMetadataChoiceRulesDto(new[]
        {
            new MetadataChoiceRuleInputDto("PrimarySeries", "KeepCurrent", "None"),
        }));

        Assert.AreEqual(400, Assert.IsInstanceOfType<ObjectResult>(result.Result).StatusCode);
    }

    [TestMethod]
    public async Task Put_NullBodyOrNullRule_Is400()
    {
        var (controller, _) = Make();

        Assert.AreEqual(400, Assert.IsInstanceOfType<ObjectResult>((await controller.UpdateMetadataChoiceRules(null!)).Result).StatusCode);
        Assert.AreEqual(
            400,
            Assert.IsInstanceOfType<ObjectResult>(
                (await controller.UpdateMetadataChoiceRules(new UpdateMetadataChoiceRulesDto(new MetadataChoiceRuleInputDto[] { null! }))).Result).StatusCode);
    }
}

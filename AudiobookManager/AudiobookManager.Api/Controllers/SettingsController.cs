using System.Reflection;
using System.Runtime.InteropServices;
using AudiobookManager.Api.Dtos;
using AudiobookManager.Domain;
using AudiobookManager.Services;
using Microsoft.AspNetCore.Mvc;

namespace AudiobookManager.Api.Controllers;
[Route("api/[controller]")]
[ApiController]
public class SettingsController : ControllerBase
{
    private readonly ISettingsService _settingsService;

    public SettingsController(ISettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    [HttpGet("system_info")]
    public SystemInfoDto GetSystemInfo()
    {
        var assembly = typeof(Program).Assembly;
        var informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        string version = "dev";
        string? commitHash = null;

        if (!string.IsNullOrWhiteSpace(informationalVersion))
        {
            var parts = informationalVersion.Split('+');
            version = parts[0];
            if (parts.Length > 1 && !string.IsNullOrWhiteSpace(parts[1]))
            {
                commitHash = parts[1];
            }
        }
        else
        {
            version = assembly.GetName().Version?.ToString() ?? "dev";
        }

        var dotNetVersion = RuntimeInformation.FrameworkDescription;
        return new SystemInfoDto(version, commitHash, dotNetVersion);
    }

    /// <summary>
    /// The languages a book may be tagged with, and the default a newly added book starts on.
    /// The client fetches this rather than holding its own copy of the list.
    /// </summary>
    [HttpGet("languages")]
    public LanguageOptionsDto GetLanguages()
    {
        return new LanguageOptionsDto(
            Languages.Supported
                .Select(l => new LanguageOptionDto(l.Code, l.DisplayName, Languages.AliasesFor(l.Code)))
                .ToList(),
            Languages.DefaultCode);
    }

    [HttpGet("series_mappings")]
    public async Task<IList<SeriesMapping>> GetSeriesMappings()
    {
        return await _settingsService.GetSeriesMappings();
    }

    [HttpPost("series_mappings")]
    public async Task<SeriesMapping> CreateSeriesMapping([FromBody] SeriesMapping dto)
    {
        if (dto.Id is not null && dto.Id != default(long))
        {
            throw new Exception("Frontend is not allowed to specify id");
        }
        return await _settingsService.CreateSeriesMapping(dto);
    }

    [HttpPut("series_mappings/{mappingId}")]
    public async Task<SeriesMapping> UpdateSeriesMappingAsync([FromBody] SeriesMapping dto, long mappingId)
    {
        dto.Id = mappingId;
        return await _settingsService.UpdateSeriesMapping(dto);
    }

    [HttpDelete("series_mappings/{mappingId}")]
    public async Task DeleteSeriesMappingAsync(long mappingId)
    {
        await _settingsService.DeleteSeriesMapping(mappingId);
    }

    /// <summary>
    /// The UI-editable library-wide settings. The enum is carried as its name string ("Spaced"/
    /// "Unspaced") so the wire format stays legible and an out-of-range value is a 400 rather
    /// than a silent numeric cast.
    /// </summary>
    [HttpGet("library")]
    public async Task<ActionResult<LibrarySettingsDto>> GetLibrarySettings()
    {
        var settings = await _settingsService.GetLibrarySettings();
        return Ok(ToDto(settings));
    }

    [HttpPut("library")]
    public async Task<ActionResult<LibrarySettingsDto>> UpdateLibrarySettings([FromBody] UpdateLibrarySettingsDto dto)
    {
        if (dto?.InitialsSpacing is null ||
            !Enum.TryParse<InitialsSpacing>(dto.InitialsSpacing, ignoreCase: true, out var parsed))
        {
            return this.InvalidRequest(
                $"'{dto?.InitialsSpacing}' is not a known initials spacing. Use one of: " +
                $"{string.Join(", ", Enum.GetNames<InitialsSpacing>())}.");
        }

        var updated = await _settingsService.UpdateLibrarySettings(
            new Domain.LibrarySettings { InitialsSpacing = parsed });
        return Ok(ToDto(updated));
    }

    private static LibrarySettingsDto ToDto(Domain.LibrarySettings settings) =>
        new(settings.InitialsSpacing.ToString());
}

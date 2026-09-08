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

    /// <summary>
    /// The Settings page's list: mappings pre-grouped by target series name (the client used to
    /// group a flat table itself), optionally narrowed by an accent-insensitive search over both
    /// the pattern and the target. Besides <c>total</c> there is no explicit bound - the table is
    /// operator-curated, so the grouped shape plus server-side search is the list's bound. The
    /// flat <c>GET series_mappings</c> that returned the whole table without bounds was removed.
    /// </summary>
    [HttpGet("series_mappings/grouped")]
    public async Task<SeriesMappingGroupsDto> GetSeriesMappingGroups([FromQuery] string? search = null)
    {
        var groups = await _settingsService.GetSeriesMappingGroupsAsync(
            string.IsNullOrWhiteSpace(search) ? null : search!.Trim());
        return new SeriesMappingGroupsDto(
            groups.Items.Select(g => new SeriesMappingGroupDto(g.MappedSeries, g.Mappings)).ToList(),
            groups.Total);
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

        // Omitted keeps the stored value rather than resetting it: the settings page sends the
        // whole object, but the DTO deliberately made the new field optional so an older client
        // (or a narrow PUT) does not silently zero the delay.
        var delayMs = dto.MetadataRefreshDelayMs ?? (await _settingsService.GetLibrarySettings()).MetadataRefreshDelayMs;
        if (delayMs < 0 || delayMs > 60_000)
        {
            return this.InvalidRequest("MetadataRefreshDelayMs must be between 0 and 60000 milliseconds.");
        }

        var updated = await _settingsService.UpdateLibrarySettings(
            new Domain.LibrarySettings { InitialsSpacing = parsed, MetadataRefreshDelayMs = delayMs });
        return Ok(ToDto(updated));
    }

    private static LibrarySettingsDto ToDto(Domain.LibrarySettings settings) =>
        new(settings.InitialsSpacing.ToString(), settings.MetadataRefreshDelayMs);
}

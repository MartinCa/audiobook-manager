using System.Reflection;
using System.Runtime.InteropServices;
using AudiobookManager.Api.Dtos;
using AudiobookManager.Domain;
using AudiobookManager.Services;
using Cronos;
using Microsoft.AspNetCore.Mvc;

namespace AudiobookManager.Api.Controllers;
[Route("api/[controller]")]
[ApiController]
public class SettingsController : ControllerBase
{
    private readonly ISettingsService _settingsService;
    private readonly IScheduledTaskService _scheduledTaskService;

    public SettingsController(ISettingsService settingsService, IScheduledTaskService scheduledTaskService)
    {
        _settingsService = settingsService;
        _scheduledTaskService = scheduledTaskService;
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
            !Enum.TryParse<InitialsSpacing>(dto.InitialsSpacing, ignoreCase: true, out var parsed) ||
            !Enum.IsDefined(parsed))
        {
            return this.InvalidRequest(
                $"'{dto?.InitialsSpacing}' is not a known initials spacing. Use one of: " +
                $"{string.Join(", ", Enum.GetNames<InitialsSpacing>())}.");
        }

        // Omitted keeps the stored value rather than resetting it: the settings page sends the
        // whole object, but the DTO deliberately made these fields optional so an older client
        // (or a narrow PUT) does not silently zero/disable them.
        var current = await _settingsService.GetLibrarySettings();

        InitialsPunctuation parsedPunctuation;
        if (dto.InitialsPunctuation is null)
        {
            parsedPunctuation = current.InitialsPunctuation;
        }
        else if (!Enum.TryParse(dto.InitialsPunctuation, ignoreCase: true, out parsedPunctuation) ||
            !Enum.IsDefined(parsedPunctuation))
        {
            return this.InvalidRequest(
                $"'{dto.InitialsPunctuation}' is not a known initials punctuation. Use one of: " +
                $"{string.Join(", ", Enum.GetNames<InitialsPunctuation>())}.");
        }

        var delayMs = dto.MetadataRefreshDelayMs ?? current.MetadataRefreshDelayMs;
        if (delayMs < 0 || delayMs > 60_000)
        {
            return this.InvalidRequest("MetadataRefreshDelayMs must be between 0 and 60000 milliseconds.");
        }

        var upcomingReleasesEnabled = dto.UpcomingReleasesEnabled ?? current.UpcomingReleasesEnabled;
        var upcomingReleasesCronSchedule = dto.UpcomingReleasesCronSchedule ?? current.UpcomingReleasesCronSchedule;
        if (!CronExpression.TryParse(upcomingReleasesCronSchedule, CronFormat.Standard, out _))
        {
            return this.InvalidRequest(
                $"'{upcomingReleasesCronSchedule}' is not a valid standard 5-field cron expression " +
                "(minute hour day month weekday).");
        }

        var updated = await _settingsService.UpdateLibrarySettings(new Domain.LibrarySettings
        {
            InitialsSpacing = parsed,
            InitialsPunctuation = parsedPunctuation,
            MetadataRefreshDelayMs = delayMs,
            UpcomingReleasesEnabled = upcomingReleasesEnabled,
            UpcomingReleasesCronSchedule = upcomingReleasesCronSchedule,
        });
        return Ok(ToDto(updated));
    }

    /// <summary>Every registered scheduled task, for the Settings "Tasks" page.</summary>
    [HttpGet("tasks")]
    public async Task<ActionResult<List<ScheduledTaskDto>>> GetScheduledTasks()
    {
        var tasks = await _scheduledTaskService.GetScheduledTasksAsync();
        return Ok(tasks.Select(t => new ScheduledTaskDto(
            t.Key, t.Name, t.CronSchedule, t.Enabled, t.LastRunAt, t.LastRunDurationMs, t.LastRunStatus, t.NextRunAt)).ToList());
    }

    private static LibrarySettingsDto ToDto(Domain.LibrarySettings settings) =>
        new(
            settings.InitialsSpacing.ToString(),
            settings.InitialsPunctuation.ToString(),
            settings.MetadataRefreshDelayMs,
            settings.UpcomingReleasesEnabled,
            settings.UpcomingReleasesCronSchedule);
}

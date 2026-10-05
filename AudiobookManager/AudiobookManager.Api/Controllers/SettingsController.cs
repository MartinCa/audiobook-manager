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
    /// <summary>
    /// The only rows-per-page values the client's page size dropdown offers. Kept here (rather
    /// than a shared constant with the frontend) because the frontend has no equivalent
    /// hardcoded-list ban for this value - unlike languages/scrapers, page size is a fixed,
    /// small, UI-only choice, not a growing catalog.
    /// </summary>
    private static readonly int[] AllowedPageSizes = [20, 50, 100];

    private readonly ISettingsService _settingsService;
    private readonly IScheduledTaskService _scheduledTaskService;
    private readonly IQualifierIndicatorService _qualifierIndicatorService;

    public SettingsController(
        ISettingsService settingsService,
        IScheduledTaskService scheduledTaskService,
        IQualifierIndicatorService qualifierIndicatorService)
    {
        _settingsService = settingsService;
        _scheduledTaskService = scheduledTaskService;
        _qualifierIndicatorService = qualifierIndicatorService;
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
    /// The book qualifiers (abridged, dramatized, ...) a book can carry, served from
    /// <see cref="BookQualifiers"/> so the client fetches its list rather than holding its own.
    /// Adding a qualifier to that one list makes it appear in the edit form and every badge.
    /// </summary>
    [HttpGet("book-qualifiers")]
    public BookQualifierOptionsDto GetBookQualifiers() =>
        new(BookQualifiers.All.Select(q => new BookQualifierDto(q.Key, q.Label, q.Suffix)).ToList());

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

        var defaultPageSize = dto.DefaultPageSize ?? current.DefaultPageSize;
        if (!AllowedPageSizes.Contains(defaultPageSize))
        {
            return this.InvalidRequest(
                $"'{defaultPageSize}' is not a supported default page size. Use one of: " +
                $"{string.Join(", ", AllowedPageSizes)}.");
        }

        SearchInitialsHandling searchInitialsHandling;
        if (dto.SearchInitialsHandling is null)
        {
            searchInitialsHandling = current.SearchInitialsHandling;
        }
        else if (!Enum.GetNames<SearchInitialsHandling>().Contains(dto.SearchInitialsHandling, StringComparer.OrdinalIgnoreCase) ||
            !Enum.TryParse(dto.SearchInitialsHandling, ignoreCase: true, out searchInitialsHandling))
        {
            return this.InvalidRequest(
                $"'{dto.SearchInitialsHandling}' is not a known search initials handling. Use one of: " +
                $"{string.Join(", ", Enum.GetNames<SearchInitialsHandling>())}.");
        }

        var maxNarratorsInPath = dto.MaxNarratorsInPath ?? current.MaxNarratorsInPath;
        if (maxNarratorsInPath < 1 || maxNarratorsInPath > Domain.LibrarySettings.MaxNarratorsInPathLimit)
        {
            return this.InvalidRequest(
                $"MaxNarratorsInPath must be between 1 and {Domain.LibrarySettings.MaxNarratorsInPathLimit}.");
        }

        var updated = await _settingsService.UpdateLibrarySettings(new Domain.LibrarySettings
        {
            InitialsSpacing = parsed,
            InitialsPunctuation = parsedPunctuation,
            MetadataRefreshDelayMs = delayMs,
            UpcomingReleasesEnabled = upcomingReleasesEnabled,
            UpcomingReleasesCronSchedule = upcomingReleasesCronSchedule,
            DefaultPageSize = defaultPageSize,
            SearchInitialsHandling = searchInitialsHandling,
            IncludeNarratorInPath = dto.IncludeNarratorInPath ?? current.IncludeNarratorInPath,
            MaxNarratorsInPath = maxNarratorsInPath,
        });
        return Ok(ToDto(updated));
    }

    /// <summary>
    /// How online metadata is applied, per field: whether a review starts with the field ticked
    /// (interactive) and what an unattended run does with it (automated). Served together with the
    /// option lists and the guard rails so the client holds none of them.
    /// </summary>
    [HttpGet("metadata-apply-rules")]
    public async Task<ActionResult<MetadataApplyRulesDto>> GetMetadataApplyRules() =>
        Ok(ToDto(await _settingsService.GetMetadataApplyRules()));

    /// <summary>
    /// Saves the rules for the fields in the body; fields left out keep their stored rules. A field
    /// the rules do not cover, an unknown option, or "Always overwrite" on a required field
    /// (author, book name, year) is refused.
    /// </summary>
    [HttpPut("metadata-apply-rules")]
    public async Task<ActionResult<MetadataApplyRulesDto>> UpdateMetadataApplyRules([FromBody] UpdateMetadataApplyRulesDto dto)
    {
        if (dto?.Rules is null || dto.Rules.Any(r => r is null))
        {
            return this.InvalidRequest("Rules must be a list of per-field rules.");
        }

        var proposed = new Dictionary<string, FieldApplyRule>();
        foreach (var rule in dto.Rules)
        {
            if (!Enum.TryParse<InteractiveApplyRule>(rule.Interactive, ignoreCase: false, out var interactive) ||
                !Enum.IsDefined(interactive))
            {
                return this.InvalidRequest(
                    $"'{rule.Interactive}' is not a known \"when reviewing\" rule. Use one of: " +
                    $"{string.Join(", ", Enum.GetNames<InteractiveApplyRule>())}.");
            }

            if (!Enum.TryParse<AutomatedApplyRule>(rule.Automated, ignoreCase: false, out var automated) ||
                !Enum.IsDefined(automated))
            {
                return this.InvalidRequest(
                    $"'{rule.Automated}' is not a known \"when automated\" rule. Use one of: " +
                    $"{string.Join(", ", Enum.GetNames<AutomatedApplyRule>())}.");
            }

            if (rule.Field is null || !proposed.TryAdd(rule.Field, new FieldApplyRule(interactive, automated)))
            {
                return this.InvalidRequest($"Each field may be given once; '{rule.Field}' is missing or repeated.");
            }
        }

        try
        {
            return Ok(ToDto(await _settingsService.UpdateMetadataApplyRules(proposed)));
        }
        catch (ArgumentException ex)
        {
            // Raised only with messages written for the caller (an unknown field, a forbidden option).
            return this.InvalidRequest(ex.Message);
        }
    }

    private static MetadataApplyRulesDto ToDto(MetadataApplyRuleSet rules) =>
        new(
            MetadataApplyRuleSet.Fields.Select(f =>
            {
                var rule = rules.Get(f.Key);
                return new MetadataApplyFieldDto(
                    f.Key, f.Label, rule.Interactive.ToString(), rule.Automated.ToString(),
                    f.AlwaysOverwriteAllowed, f.AlwaysOverwriteWarning);
            }).ToList(),
            MetadataApplyRuleSet.InteractiveOptions.Select(o => new MetadataApplyOptionDto(o.Key, o.Label, o.Description)).ToList(),
            MetadataApplyRuleSet.AutomatedOptions.Select(o => new MetadataApplyOptionDto(o.Key, o.Label, o.Description)).ToList());

    /// <summary>
    /// The per-source wordings that stand for a book qualifier ("[Dramatized Adaptation]" on
    /// Audible means dramatized). Applied to scraped titles, which then arrive clean with the
    /// qualifier set alongside - see <c>QualifierIndicators</c>.
    /// </summary>
    [HttpGet("qualifier-indicators")]
    public async Task<ActionResult<QualifierIndicatorsDto>> GetQualifierIndicators() =>
        Ok(ToDto(await _qualifierIndicatorService.GetRulesAsync()));

    /// <summary>Replaces the whole rule set.</summary>
    [HttpPut("qualifier-indicators")]
    public async Task<ActionResult<QualifierIndicatorsDto>> UpdateQualifierIndicators([FromBody] QualifierIndicatorsDto dto)
    {
        if (dto?.Indicators is null || dto.Indicators.Any(i => i is null))
        {
            return this.InvalidRequest("Indicators must be a list of rules.");
        }

        try
        {
            var saved = await _qualifierIndicatorService.ReplaceRulesAsync(
                dto.Indicators.Select(i => new QualifierIndicatorRule(i.Source, i.Indicator, i.QualifierKey)).ToList());
            return Ok(ToDto(saved));
        }
        catch (ArgumentException ex)
        {
            // Raised only with messages written for the caller (an unknown source or qualifier, a
            // blank indicator, too many rules), so relaying it is what tells them what to fix.
            return this.InvalidRequest(ex.Message);
        }
    }

    private static QualifierIndicatorsDto ToDto(IReadOnlyList<QualifierIndicatorRule> rules) =>
        new(rules.Select(r => new QualifierIndicatorDto(r.Source, r.Indicator, r.QualifierKey)).ToList());

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
            settings.UpcomingReleasesCronSchedule,
            settings.DefaultPageSize,
            settings.SearchInitialsHandling.ToString(),
            settings.IncludeNarratorInPath,
            settings.MaxNarratorsInPath);
}

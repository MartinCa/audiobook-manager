using System.Diagnostics;
using AudiobookManager.Database.Repositories;
using AudiobookManager.Domain;
using AudiobookManager.FileManager;
using AudiobookManager.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using AudiobookDb = AudiobookManager.Database.Models.Audiobook;

namespace AudiobookManager.Services;
public class AudiobookService : IAudiobookService
{
    private readonly IAudiobookTagHandler _tagHandler;
    private readonly IAudiobookFileHandler _fileHandler;
    private readonly IFileOperations _fileOperations;
    private readonly AudiobookManagerSettings _settings;
    private readonly ILogger<AudiobookService> _logger;

    private readonly IAudiobookRepository _audiobookRepository;
    private readonly IPersonRepository _personRepository;
    private readonly IGenreRepository _genreRepository;
    private readonly IConsistencyIssueRepository _issueRepository;

    public AudiobookService(
        IAudiobookTagHandler tagHandler,
        IAudiobookFileHandler fileHandler,
        IFileOperations fileOperations,
        IOptions<AudiobookManagerSettings> settings,
        IAudiobookRepository audiobookRepository,
        IPersonRepository personRepository,
        IGenreRepository genreRepository,
        IConsistencyIssueRepository issueRepository,
        ILogger<AudiobookService> logger)
    {
        _tagHandler = tagHandler;
        _fileHandler = fileHandler;
        _fileOperations = fileOperations;
        _settings = settings.Value;
        _audiobookRepository = audiobookRepository;
        _personRepository = personRepository;
        _genreRepository = genreRepository;
        _issueRepository = issueRepository;
        _logger = logger;
    }

    public Audiobook ParseAudiobook(string filePath, bool includeCoverData = true)
    {
        var fileInfo = new FileInfo(filePath);

        return _tagHandler.ParseAudiobook(fileInfo, includeCoverData);
    }

    public string GenerateLibraryPath(Audiobook audiobook)
    {
        var newRelativePath = AudiobookFileHandler.GenerateRelativeAudiobookPath(audiobook);
        return AudiobookFileHandler.JoinLibraryPath(_settings.AudiobookLibraryPath, newRelativePath);
    }

    /// <summary>
    /// Checks whether a file already occupies the audiobook's generated library path, so a
    /// duplicate can be caught and resolved before organizing attempts the move and fails.
    /// When the path is occupied by a tracked library book, its size/duration are returned for
    /// comparison; when it's an untracked file (e.g. an orphan), only its size is available.
    /// </summary>
    public async Task<TargetPathCollisionResult> CheckTargetPathCollision(Audiobook audiobook)
    {
        var targetPath = GenerateLibraryPath(audiobook);

        if (!File.Exists(targetPath))
        {
            return new TargetPathCollisionResult { TargetPath = targetPath, Exists = false };
        }

        // OS-aware comparison, not a raw string match: on a case-insensitive volume the stored
        // path can differ in case from the freshly generated target (an author's casing edited
        // in the tags since import), and a string match would then miss the tracked book and
        // report the collision as an untracked file with nothing but a byte size to go on.
        var existingAudiobook = await _audiobookRepository.GetByFullPathAsync(
            targetPath, AudiobookFileHandler.PathsEqual);
        if (existingAudiobook is not null)
        {
            return new TargetPathCollisionResult
            {
                TargetPath = targetPath,
                Exists = true,
                ExistingAudiobookId = existingAudiobook.Id,
                ExistingSizeInBytes = existingAudiobook.FileInfoSizeInBytes,
                ExistingDurationInSeconds = existingAudiobook.DurationInSeconds
            };
        }

        var fileInfo = new FileInfo(targetPath);
        return new TargetPathCollisionResult
        {
            TargetPath = targetPath,
            Exists = true,
            ExistingSizeInBytes = fileInfo.Length
        };
    }

    public async Task<Audiobook> OrganizeAudiobook(Audiobook audiobook, Func<string, int, Task> progressAction)
    {
        var oldDirectory = Path.GetDirectoryName(audiobook.FileInfo.FullPath);

        var newParsed = await WriteTagsRelocateAndWriteSidecarsAsync(audiobook, oldDirectory, progressAction);

        await InsertAudiobook(newParsed);

        await progressAction("Done", 100);

        return newParsed;
    }

    /// <summary>
    /// Shared "write tags -> verify round-trip -> relocate -> write sidecars" pipeline used by
    /// both <see cref="OrganizeAudiobook"/> and <see cref="UpdateAudiobook"/>. Keeping this in one
    /// place means the two flows can't silently diverge in behavior (as happened previously, e.g.
    /// when only one of them verified the tag round-trip) - only the DB insert-vs-update step is
    /// left to the caller. Reports progress through the same phase names/percentages for both
    /// callers so their UIs stay in sync.
    /// </summary>
    private async Task<Audiobook> WriteTagsRelocateAndWriteSidecarsAsync(Audiobook audiobook, string? oldDirectory, Func<string, int, Task> progressAction)
    {
        var sw = new Stopwatch();
        sw.Start();

        await progressAction("Started", 0);
        const int afterTagsProgress = 70;
        int lastProgressNotified = 0;

        // ATL calls this synchronously from the save, so the async progressAction cannot be
        // awaited here. Observe the returned Task explicitly rather than discarding it: a hub
        // failure would otherwise surface only as an unobserved task exception.
        Action<float> saveTagsProgressAction = (float progress) =>
        {
            var modifiedProgress = (int)(afterTagsProgress * progress);
            if (modifiedProgress - lastProgressNotified >= 10)
            {
                lastProgressNotified = modifiedProgress;
                _logger.LogInformation("({audiobookFile}) saving tags progress {progress}, full progress {modifiedProgress}", audiobook.FileInfo.FullPath, progress, modifiedProgress);

                _ = progressAction("Saving tags", modifiedProgress)
                    .ContinueWith(
                        t => _logger.LogWarning(t.Exception, "({audiobookFile}) Failed to report tag-save progress", audiobook.FileInfo.FullPath),
                        CancellationToken.None,
                        TaskContinuationOptions.OnlyOnFaulted,
                        TaskScheduler.Default);
            }
        };

        try
        {
            _tagHandler.SaveAudiobookTagsToFile(audiobook, saveTagsProgressAction);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw WrapPermissionException(ex, audiobook.FileInfo.FullPath);
        }

        _logger.LogInformation("({audiobookFile}) Saving tags to file took {timeTakenInMs} ms", audiobook.FileInfo.FullPath, sw.ElapsedMilliseconds);

        await progressAction("Saved tags", afterTagsProgress);

        // Verify the tags we just asked to be written actually round-tripped, before relocating
        // the file or touching the DB. Writing tags can silently fail to persist a subset of
        // fields for some m4b files - most commonly a file with non-contiguous QuickTime chapters,
        // which ATL cannot rewrite in place (look for an "ignoring Quicktime chapters" ATL warning
        // in the log around this time; remuxing the file, e.g. `ffmpeg -i in.m4b -c copy
        // -map_metadata 0 out.m4b`, resolves it) - which would otherwise leave the DB record - and
        // the file's new library path, generated from these same fields - out of sync with what's
        // really on disk. Checking here, at the original location and before any move, turns a
        // silent desync into a visible save failure instead of leaving a relocated file with stale
        // tags for the consistency check to discover later.
        // The verification compares text tags only, so skip encoding the cover we just wrote.
        var savedTags = ParseAudiobook(audiobook.FileInfo.FullPath, includeCoverData: false);
        var mismatches = TagConsistencyChecker.FindMismatches(audiobook, savedTags);
        if (mismatches.Count > 0)
        {
            foreach (var (field, expected, actual) in mismatches)
            {
                _logger.LogWarning(
                    "({audiobookFile}) Tag round-trip mismatch on field {field}: requested '{expected}', file has '{actual}' after save",
                    audiobook.FileInfo.FullPath, field, expected, actual);
            }

            throw new Exception(
                $"Saved tags did not match the requested metadata for '{audiobook.FileInfo.FullPath}': " +
                $"{string.Join(", ", mismatches.Select(m => m.Field))}. This can happen when the file has " +
                "non-contiguous QuickTime chapters that ATL cannot rewrite in place (check the app log around " +
                "this time for an ATL warning about \"ignoring Quicktime chapters\"); remuxing the file " +
                "(e.g. `ffmpeg -i in.m4b -c copy -map_metadata 0 out.m4b`) resolves it in that case.");
        }

        var newFullPath = GenerateLibraryPath(audiobook);

        try
        {
            newFullPath = await RelocateIfPathChangedAsync(audiobook, newFullPath, oldDirectory, sw, progressAction, relocatingProgress: 75, relocatedProgress: 80);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw WrapPermissionException(ex, newFullPath);
        }

        var newParsed = ParseAudiobook(newFullPath);

        await progressAction("Reparsed", 85);

        _fileHandler.WriteMetadata(newParsed);

        await progressAction("Written metadata files", 90);

        newParsed.CoverFilePath = _fileHandler.WriteCover(newParsed);

        await progressAction("Written cover", 95);

        _logger.LogInformation("({audiobookFile}) Writing metadata files took {timeTakenInMs} ms", audiobook.FileInfo.FullPath, sw.ElapsedMilliseconds);

        return newParsed;
    }

    /// <summary>
    /// Wraps a permission-denied failure from a tag write or file relocation with a clearer,
    /// actionable message. Permission drift on the library's files (a different owner/group than
    /// the one this application runs as) is a common source of confusing raw .NET exception text,
    /// particularly on setups like unRAID where the mover process, other containers, or manual
    /// host-side changes can leave files owned by someone other than this app's configured user.
    /// </summary>
    private static Exception WrapPermissionException(UnauthorizedAccessException ex, string path)
    {
        return new Exception(
            $"Permission denied writing to '{path}'. This usually means the file (or its directory) " +
            "is owned by a different user/group than the one this application runs as - check the " +
            "file's ownership and permissions match what the container expects.", ex);
    }

    /// <summary>
    /// Moves the audiobook's file to <paramref name="newFullPath"/> if it differs from its current path,
    /// throwing if a file already occupies the destination, and cleans up sidecar files left behind in
    /// <paramref name="oldDirectory"/> when it moved to a different directory. No-ops (no exists-check,
    /// no move, no logging/progress) when the path is unchanged. Returns the resulting path.
    /// </summary>
    private async Task<string> RelocateIfPathChangedAsync(
        Audiobook audiobook,
        string newFullPath,
        string? oldDirectory,
        Stopwatch? sw = null,
        Func<string, int, Task>? progressAction = null,
        int relocatingProgress = 0,
        int relocatedProgress = 0)
    {
        if (AudiobookFileHandler.PathsEqual(newFullPath, audiobook.FileInfo.FullPath))
        {
            return newFullPath;
        }

        if (File.Exists(newFullPath))
        {
            if (audiobook.ReplaceExisting == true)
            {
                var existingAudiobook = await _audiobookRepository.GetByFullPathAsync(
                    newFullPath, AudiobookFileHandler.PathsEqual);
                if (existingAudiobook is not null)
                {
                    _logger.LogInformation(
                        "Replacing existing audiobook {AudiobookId} ('{Title}') at '{TargetPath}'",
                        existingAudiobook.Id, existingAudiobook.BookName, newFullPath);
                    await _issueRepository.DeleteByAudiobookIdAsync(existingAudiobook.Id);
                    await _audiobookRepository.DeleteAudiobookAsync(existingAudiobook.Id);
                }
            }
            else
            {
                throw new Exception($"'{newFullPath}' already exists");
            }
        }

        if (progressAction is not null)
        {
            await progressAction("Generated new path, relocating", relocatingProgress);
        }

        sw?.Restart();

        _fileHandler.RelocateAudiobook(audiobook, newFullPath, overwrite: audiobook.ReplaceExisting == true);

        if (sw is not null)
        {
            _logger.LogInformation("({audiobookFile}) Relocating to {newFullPath} took {timeTakenInMs} ms", audiobook.FileInfo.FullPath, newFullPath, sw.ElapsedMilliseconds);
            sw.Restart();
        }

        if (progressAction is not null)
        {
            await progressAction("Relocated", relocatedProgress);
        }

        // Compare the directories the way the file system does, not as raw strings. Defensive
        // rather than a live bug fix: both the generated directory and the generated file name
        // derive from the same fields, so equal directories imply an equal full path and the
        // PathsEqual short-circuit above has already returned. Should that ever stop holding, a
        // textual comparison here would delete the sidecars out of the directory the file just
        // moved into - and reader.txt is only rewritten when the book has narrators, so it would
        // be lost outright. Keeping the two comparisons consistent removes that trap.
        var newDirectory = Path.GetDirectoryName(newFullPath);
        if (oldDirectory is not null && newDirectory is not null && !AudiobookFileHandler.PathsEqual(oldDirectory, newDirectory))
        {
            _fileHandler.MigrateSidecarFiles(oldDirectory, newDirectory);
            _fileHandler.RemoveSidecarFiles(oldDirectory);
            _fileHandler.RemoveDirIfEmpty(oldDirectory);
        }

        return newFullPath;
    }

    private async Task<(List<Database.Models.Person> Authors, List<Database.Models.Person> Narrators, List<Database.Models.Genre> Genres)> GetOrCreateAuthorsNarratorsGenres(Audiobook audiobook)
    {
        // Authors and narrators both live in the Persons table, so resolve their names together
        // in one round-trip rather than one query (+ possible insert) per name.
        var authorNames = audiobook.Authors.Select(a => a.Name).ToList();
        var narratorNames = audiobook.Narrators.Select(n => n.Name).ToList();
        var personsByName = await _personRepository.GetOrCreatePersons(authorNames.Concat(narratorNames));

        var authors = authorNames.Select(name => personsByName[name]).ToList();
        var narrators = narratorNames.Select(name => personsByName[name]).ToList();

        var genresByName = await _genreRepository.GetOrCreateGenres(audiobook.Genres);
        var genres = audiobook.Genres.Select(name => genresByName[name]).ToList();

        return (authors, narrators, genres);
    }

    public async Task<Audiobook> InsertAudiobook(Audiobook audiobook)
    {
        var (authors, narrators, genres) = await GetOrCreateAuthorsNarratorsGenres(audiobook);

        AudiobookDb dbAudiobook = new AudiobookDb(
            audiobook.Id ?? default,
            audiobook.BookName ?? string.Empty,
            audiobook.Subtitle,
            audiobook.Series,
            audiobook.SeriesPart,
            audiobook.Year ?? 0,
            audiobook.Description,
            audiobook.Copyright,
            audiobook.Publisher,
            audiobook.Language,
            audiobook.Rating,
            audiobook.Asin,
            audiobook.Www,
            audiobook.CoverFilePath,
            audiobook.DurationInSeconds,
            audiobook.FileInfo.FullPath,
            audiobook.FileInfo.FileName,
            audiobook.FileInfo.SizeInBytes
            )
        {
            Authors = authors,
            Narrators = narrators,
            Genres = genres
        };

        var result = await _audiobookRepository.InsertAudiobook(dbAudiobook);
        _logger.LogInformation(
            "Added audiobook {AudiobookId} ('{Title}') to library at '{FilePath}'",
            result.Id, result.BookName, result.FileInfoFullPath);
        return FromDb(result);
    }

    public async Task<Audiobook?> GetAudiobookById(long id)
    {
        var dbAudiobook = await _audiobookRepository.GetByIdWithIncludesAsync(id);
        if (dbAudiobook == null) return null;

        return FromDb(dbAudiobook);
    }

    public async Task<Audiobook> UpdateAudiobook(long id, Audiobook audiobook, Func<string, int, Task>? progressAction = null)
    {
        progressAction ??= (_, _) => Task.CompletedTask;

        var existing = await _audiobookRepository.GetByIdWithIncludesAsync(id);
        if (existing == null)
            throw new Exception($"Audiobook with id {id} not found");

        var oldDirectory = Path.GetDirectoryName(existing.FileInfoFullPath);

        audiobook.FileInfo = new AudiobookFileInfo(existing.FileInfoFullPath, existing.FileInfoFileName, existing.FileInfoSizeInBytes);

        var newParsed = await WriteTagsRelocateAndWriteSidecarsAsync(audiobook, oldDirectory, progressAction);

        // Update DB record
        var (authors, narrators, genres) = await GetOrCreateAuthorsNarratorsGenres(audiobook);

        existing.BookName = audiobook.BookName ?? string.Empty;
        existing.Subtitle = audiobook.Subtitle;
        existing.Series = audiobook.Series;
        existing.SeriesPart = audiobook.SeriesPart;
        existing.Year = audiobook.Year ?? existing.Year;
        existing.Description = audiobook.Description;
        existing.Copyright = audiobook.Copyright;
        existing.Publisher = audiobook.Publisher;
        existing.Language = audiobook.Language;
        existing.Rating = audiobook.Rating;
        existing.Asin = audiobook.Asin;
        existing.Www = audiobook.Www;
        existing.CoverFilePath = newParsed.CoverFilePath;
        existing.DurationInSeconds = newParsed.DurationInSeconds;
        existing.FileInfoFullPath = newParsed.FileInfo.FullPath;
        existing.FileInfoFileName = newParsed.FileInfo.FileName;
        existing.FileInfoSizeInBytes = newParsed.FileInfo.SizeInBytes;
        existing.Authors = authors;
        existing.Narrators = narrators;
        existing.Genres = genres;

        await _audiobookRepository.UpdateAudiobookAsync(existing);
        _logger.LogInformation(
            "Updated audiobook {AudiobookId} ('{Title}') in library (path: '{FilePath}')",
            existing.Id, existing.BookName, existing.FileInfoFullPath);

        await progressAction("Done", 100);

        return FromDb(existing);
    }

    public async Task DeleteAudiobook(long id)
    {
        var existing = await _audiobookRepository.GetByIdWithIncludesAsync(id);
        if (existing == null)
        {
            return;
        }

        var directoryPath = Path.GetDirectoryName(existing.FileInfoFullPath);
        _logger.LogInformation(
            "Deleting audiobook {AudiobookId} ('{Title}') from library (path: '{FilePath}')",
            existing.Id, existing.BookName, existing.FileInfoFullPath);

        await _issueRepository.DeleteByAudiobookIdAsync(id);
        await _audiobookRepository.DeleteAudiobookAsync(id);

        if (File.Exists(existing.FileInfoFullPath))
        {
            if (directoryPath != null)
            {
                if ((string.IsNullOrEmpty(_settings.AudiobookLibraryPath) || !AudiobookFileHandler.PathsEqual(directoryPath, _settings.AudiobookLibraryPath)) &&
                    (string.IsNullOrEmpty(_settings.AudiobookImportPath) || !AudiobookFileHandler.PathsEqual(directoryPath, _settings.AudiobookImportPath)))
                {
                    _fileOperations.DeleteDirectory(directoryPath, true, $"audiobook {existing.Id} ('{existing.BookName}') deleted from library");
                    var parentDir = Path.GetDirectoryName(directoryPath);
                    if (parentDir != null)
                    {
                        _fileHandler.RemoveDirIfEmpty(parentDir);
                    }
                }
                else
                {
                    _fileOperations.DeleteFile(existing.FileInfoFullPath, $"audiobook {existing.Id} ('{existing.BookName}') deleted from library (root file)");
                }
            }
        }
    }

    public static Audiobook FromDb(AudiobookDb audiobookDb)
    {
        return new Audiobook(
            audiobookDb.Authors.Select(FromDbPerson).ToList(),
            audiobookDb.BookName,
            audiobookDb.Year,
            new AudiobookFileInfo(audiobookDb.FileInfoFullPath, audiobookDb.FileInfoFileName, audiobookDb.FileInfoSizeInBytes))
        {
            Id = audiobookDb.Id,
            Narrators = audiobookDb.Narrators.Select(FromDbPerson).ToList(),
            BookName = audiobookDb.BookName,
            Subtitle = audiobookDb.Subtitle,
            Series = audiobookDb.Series,
            SeriesPart = audiobookDb.SeriesPart,
            Genres = audiobookDb.Genres.Select(x => x.Name).ToList(),
            Description = audiobookDb.Description,
            Copyright = audiobookDb.Copyright,
            Publisher = audiobookDb.Publisher,
            Language = audiobookDb.Language,
            Rating = audiobookDb.Rating,
            Asin = audiobookDb.Asin,
            Www = audiobookDb.Www,
            CoverFilePath = audiobookDb.CoverFilePath,
            DurationInSeconds = audiobookDb.DurationInSeconds
        };
    }

    public static Person FromDbPerson(Database.Models.Person personDb)
    {
        return new Person(personDb.Name) { Id = personDb.Id };
    }
}

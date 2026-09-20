using AudiobookManager.Database.Models;
using AudiobookManager.FileManager;

namespace AudiobookManager.Services;

/// <summary>
/// The pre-loaded inputs the combined scan hands the consistency check: the tracked audiobook
/// graph (loaded once, shared with the scan's known-path derivation) and the library directory
/// walk the orphan sweep used to do for itself. Passing both in lets the consistency check run
/// on the already-performed walk and graph load instead of repeating either.
/// </summary>
public sealed record ConsistencyCheckInput(
    IReadOnlyList<Audiobook> Audiobooks,
    IReadOnlyList<LibraryDirectory> Directories);

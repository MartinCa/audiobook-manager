namespace AudiobookManager.Domain;

/// <summary>
/// The library settings that change what <c>AudiobookFileHandler.GenerateRelativeAudiobookPath</c>
/// produces. Passed as one value, and deliberately without a default, so that a caller cannot
/// forget one of them and compute a different path from the rest of the app. Built from
/// <see cref="LibrarySettings.PathOptions"/>.
/// </summary>
/// <param name="NarratorsInPath">
/// How many narrators the book folder names (<c>{Narrator}</c> suffix); 0 means the narrator is
/// not part of the path at all.
/// </param>
/// <param name="MainAuthorOnly">
/// Whether the author folder names only the book's main author (<see cref="Audiobook.MainAuthor"/>)
/// instead of every author. A book with no main author keeps its full author list either way.
/// </param>
public readonly record struct LibraryPathOptions(int NarratorsInPath, bool MainAuthorOnly)
{
    /// <summary>Neither setting on: every author in the author folder, no narrator suffix.</summary>
    public static LibraryPathOptions None => new(0, false);
}

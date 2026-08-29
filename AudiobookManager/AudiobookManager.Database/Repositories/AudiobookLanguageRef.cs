namespace AudiobookManager.Database.Repositories;

/// <summary>
/// The two columns the language backfill needs per book: which row to update, and which file to
/// read the embedded language tag from. Projected in SQL rather than materializing whole entity
/// graphs - a library-wide pass would otherwise pull every Description blob along with it.
/// </summary>
public record AudiobookLanguageRef(long Id, string FullPath);

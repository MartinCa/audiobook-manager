namespace AudiobookManager.Database.Repositories;

/// <summary>
/// One audiobook row narrowed to what the URL cleanup page needs, instead of the full entity
/// graph (Authors, Narrators, Genres, every column). Projected in SQL by
/// <see cref="AudiobookRepository.GetDirtyUrlPageAsync"/>.
/// </summary>
public record DirtyUrlRow(long AudiobookId, string BookName, List<string> Authors, string Www);

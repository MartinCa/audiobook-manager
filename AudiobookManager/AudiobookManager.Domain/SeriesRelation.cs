namespace AudiobookManager.Domain;

/// <summary>A series a book belongs to, with the book's optional part in it.</summary>
public record SeriesRelation(string Name, string? Part);

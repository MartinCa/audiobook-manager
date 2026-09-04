using AudiobookManager.Database.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace AudiobookManager.Database.Search;

/// <summary>
/// Keeps the accent-folded shadow columns (<c>Audiobook.BookNameFolded</c>/SubtitleFolded/
/// SeriesFolded/DescriptionFolded, <c>Person.NameFolded</c>) in step with their source columns on
/// every save, regardless of which repository - or which EF navigation-property cascade - put the
/// entity in the change tracker.
///
/// This has to be a save interceptor rather than logic in AudiobookRepository.InsertAudiobook/
/// UpdateAudiobookAsync and PersonRepository.GetOrCreatePerson(s): a new <c>Person</c> can enter
/// the change tracker without ever going through PersonRepository, by being attached to an
/// <c>Audiobook</c>'s Authors/Narrators collection and cascade-inserted alongside it. A fix that
/// lived in only those four repository methods would miss that path and leave the new author's
/// NameFolded null. Hooking SavingChanges instead catches every Added/Modified Audiobook and
/// Person in one place, however they got there (see #1303).
/// </summary>
public sealed class AccentFoldedColumnsInterceptor : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Sync(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Sync(eventData.Context);
        return ValueTask.FromResult(result);
    }

    private static void Sync(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        // ChangeTracker.Entries() runs DetectChanges first, so State already reflects whatever the
        // caller mutated on a tracked entity before calling SaveChanges - no separate "did the
        // source column change" check needed. Recomputing unconditionally for every Added/Modified
        // entity is simpler than tracking which specific property changed, and FoldPlain is cheap
        // (no I/O, an allocation no larger than the string itself).
        foreach (var entry in context.ChangeTracker.Entries<Audiobook>())
        {
            if (entry.State is EntityState.Added or EntityState.Modified)
            {
                entry.Entity.BookNameFolded = AccentFolding.FoldPlain(entry.Entity.BookName);
                entry.Entity.SubtitleFolded = AccentFolding.FoldPlain(entry.Entity.Subtitle);
                entry.Entity.SeriesFolded = AccentFolding.FoldPlain(entry.Entity.Series);
                entry.Entity.DescriptionFolded = AccentFolding.FoldPlain(entry.Entity.Description);
            }
        }

        foreach (var entry in context.ChangeTracker.Entries<Person>())
        {
            if (entry.State is EntityState.Added or EntityState.Modified)
            {
                entry.Entity.NameFolded = AccentFolding.FoldPlain(entry.Entity.Name);
            }
        }
    }
}

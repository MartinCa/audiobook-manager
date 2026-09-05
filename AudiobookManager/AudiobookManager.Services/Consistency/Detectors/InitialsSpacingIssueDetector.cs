using AudiobookManager.Database.Models;
using AudiobookManager.Domain;
using DbAudiobook = AudiobookManager.Database.Models.Audiobook;

namespace AudiobookManager.Services;

/// <summary>
/// Groups every author/narrator value in the library, one issue per distinct value that does not
/// follow the configured <see cref="InitialsSpacing"/>. The representative book is the first one
/// encountered carrying the value (books are loaded ordered by BookName, so it is stable), and the
/// description states how many books carry it, so the reader knows a resolve rewrites all of them.
/// </summary>
public class InitialsSpacingIssueDetector : IInitialsSpacingIssueDetector
{
    public IEnumerable<ConsistencyIssue> Detect(
        IReadOnlyList<DbAudiobook> audiobooks, Domain.InitialsSpacing spacing)
    {
        // name -> (representative audiobook id, book count)
        var persons = new Dictionary<string, (long AudiobookId, int Count)>(StringComparer.Ordinal);

        foreach (var book in audiobooks)
        {
            // A person can be both author and narrator of the same book; count the value once per
            // book so the "N books" description reflects books, not person-book-role links.
            var namesOnBook = new HashSet<string>(StringComparer.Ordinal);
            foreach (var person in book.Authors.Concat(book.Narrators))
            {
                if (!namesOnBook.Add(person.Name))
                {
                    continue;
                }

                if (persons.TryGetValue(person.Name, out var existing))
                {
                    persons[person.Name] = (existing.AudiobookId, existing.Count + 1);
                }
                else
                {
                    persons[person.Name] = (book.Id, 1);
                }
            }
        }

        foreach (var (name, (representativeAudiobookId, count)) in persons.OrderBy(p => p.Key, StringComparer.InvariantCulture))
        {
            var canonical = InitialsSpacingFormatter.Format(name, spacing);
            if (canonical == name)
            {
                continue; // already compliant
            }

            var spacingLabel = spacing == Domain.InitialsSpacing.Spaced ? "spaced" : "unspaced";
            yield return new ConsistencyIssue
            {
                AudiobookId = representativeAudiobookId,
                IssueType = ConsistencyIssueType.InitialsSpacingMismatch,
                Description =
                    $"'{name}' ({count} book{(count == 1 ? "" : "s")}) does not follow the configured "
                    + $"{spacingLabel} initials style. Resolving renames it to '{canonical}' on every book.",
                ExpectedValue = canonical,
                ActualValue = name,
                DetectedAt = DateTime.UtcNow
            };
        }
    }
}
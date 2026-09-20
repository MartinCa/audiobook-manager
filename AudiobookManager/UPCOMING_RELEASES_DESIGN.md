# Missing books, upcoming books, and Upcoming Releases

This note explains how the three concepts relate after this change, for whoever builds the
frontend half of this feature.

## The roster is one unified table now - one book, one row

A source roster entry lives in one table, `expected_books`
(`AudiobookManager.Database.Models.ExpectedBook`), regardless of which scope discovered it - an
author refresh and a series refresh can both learn about the same book, and instead of two
parallel rosters (a per-series table and a per-author table) the book is **one row**. It is
linked to its author(s) through `expected_book_authors` rows and - when the source places it in a
matched series - to a catalog `series` row. The `(source_name, source_book_id)` pair is the row's
dedup identity; the same book found through an author's bibliography and through its series' own
roster is never stored twice.

Reconciling that roster against the library's owned books already classified every roster entry
as "owned" or "not owned" (`SeriesReconciliationProvider` / `AuthorReconciliationProvider` mirror).
This change adds one more split on top of "not owned": is the book released yet?

- **Missing**: an unmatched roster entry whose release date has passed (or has no release date
  and no future `Year`). The source knows about a book the library doesn't have.
- **Upcoming**: an unmatched roster entry that isn't released yet. Functionally the same data as
  "Missing", just not actionable yet - nothing to buy or organize.
- Neither, once a roster entry matches an owned book.

The classification lives in one place, `AudiobookManager.Domain.ExpectedBookClassifier`, used by
both the series and author reconciliation providers:

```
IsUpcoming(releaseDate, year, today) =>
    releaseDate is not null ? releaseDate > today
                             : year is not null && year > today.Year
```

A precise `ReleaseDate` (nullable `DateOnly`) is preferred whenever the source has reported one;
existing rows (matched before the field existed) have it `null` and fall back to the `Year`
heuristic until their series/author is refreshed again - no forced backfill migration. That
refresh only happens on a manual "Refresh Online" (or bulk refresh-all) action today:
`UpcomingReleasesWorker`'s periodic tick only runs the legacy `UpcomingRelease` scrape (see
below), not a roster refresh, so it does not backfill `ReleaseDate` on its own.

Both reconciliations expose `Missing` and `Upcoming` as separate lists (`SeriesReconciliation`,
`AuthorReconciliation`), and the series/author detail DTOs (`SeriesDetailDto.UpcomingBooks`,
`AuthorDetailDto.UpcomingBooks`) carry both. **The per-series/per-author detail page shows
missing/upcoming regardless of follow status** - it's the same roster either way.

## The author roster is the whole bibliography - series books included

An author's roster is their full bibliography as the source reports it, **series books included**:
the scraper's author query is `IScraper.GetAuthorBooks`
(`HardcoverScraper.GetAuthorBooks`), and `UpcomingReleaseService.RefreshAuthorRosterCoreAsync`
upserts every returned book. A series book therefore appears in the author's roster too - the
unified row is the same one the series' own refresh writes, which is exactly why it is never
double-counted between the author scope and the series scope. The author refresh stores the
book's series placement on the shared row (`ExpectedBook.SourceSeriesId`/`SourceSeriesName`/
`SeriesPosition`, plus the `SeriesId` link when the source series is matched locally), so the
book's series scoping on the author side comes from the row itself, not from a "does this book
belong to a series" filter.

Because the roster carries series placement, the author reconciliation's matching is
series-aware (see `AuthorReconciliationProvider`):

- a standalone entry (no local series link and no source series) matches an owned book by title
  alone, over every owned book of the author;
- an entry linked to a local series matches an owned book of that same local series value by
  position/title - the full `SeriesRosterMatcher` semantics scoped to the series;
- an entry whose source series is not yet matched locally has no local series name to scope
  against, so it is matched best-effort by position/title over everything the author owns -
  documented as potentially incomplete until the series is matched (two different series can
  carry same-titled books, and the source position can disagree with a hand-typed part).

## The author detail page gains a Missing Series section

Because the bibliography carries series placement, an author whose library owns nothing in a
source-reported series shows that as a distinct, higher-level gap.
`AuthorDetailDto.MissingSeries` (`PaginatedResult<AuthorMissingSeriesDto>`, opt-in via the
`includeMissingSeries` query parameter plus `missingSeriesLimit`/`missingSeriesOffset`) groups
the author's roster by source-series identity `(source_name, source_series_id)` and reports the
groups whose local library owns zero books in the matched local series (or whose source series is
not matched locally at all): per-group expected/missing/upcoming/owned counts, plus the matched
local series when one exists. A source series with some owned books is NOT reported here - its
unmatched entries stay in the ordinary Missing/Upcoming lists and become reachable through the
series view once the series is matched. The groups are computed in
`AuthorReconciliationProvider.ComputeMissingSeries`, bounded before materialization
(`MaxAuthorSeriesGroups`) and only when the caller actually asks for the section.

## What happened to the "pending review" MissingBook change

Before this change, a series refresh diffed the fetched roster against owned books and surfaced
unmatched entries as a `SeriesRefreshChangeType.MissingBook` "pending change" the user reviewed
and applied one at a time (`PendingSeriesRefresh` / `SeriesRefreshDiffer`). That's now redundant:
the same information is always visible, live, via the roster/reconciliation's Missing and Upcoming
sections - no review step needed. `SeriesRefreshDiffer` no longer emits `MissingBook`; a refresh
that only finds unmatched roster entries (no part renumbering, no part removal, no series-name
adoption) now reports `hasChanges: false` and never creates a `PendingSeriesRefresh` row.
`SeriesRefreshChangeType.MissingBook` stays in the enum only so a `PayloadJson` row written before
this change still deserializes (`PendingSeriesRefreshPayload`'s hand-versioned contract needs its
old rows to keep reading, even though nothing writes that shape any more). Authors never had this
review step in the first place - `RefreshAuthorRosterAsync` writes the unified table directly
(upsert + prune), no `PendingSeriesRefresh`-equivalent table.

## The global Upcoming Releases page unions the legacy scrape table with the rosters

The global Upcoming Releases page (`/library/upcoming-releases`) has one requirement the roster
doesn't by itself satisfy: **only followed** authors/series, and a per-row "remove" action a user
expects to stick. The legacy scrape pipeline (`UpcomingReleasesWorker`, the `UpcomingRelease`
table) already scopes to followed-and-matched authors/series and gives real per-row removal, so it
stays - but `UpcomingReleaseService.GetUpcomingReleasesAsync` no longer reads it alone. It now
builds the union, in memory, per request:

- For every followed-and-matched series (`ISeriesFollowRepository.GetFollowedMatchedSeriesAsync`),
  its `SeriesReconciliationProvider.GetReconciliationAsync(...).Upcoming` entries.
- For every followed-and-matched author (`IAuthorFollowRepository.GetFollowedMatchedAuthorsAsync`),
  its `AuthorReconciliationProvider.GetReconciliationAsync(...).Upcoming` entries.
- Every row still in the legacy `UpcomingRelease` table (`IUpcomingReleaseRepository.GetAllAsync`).

Building the whole set before sorting/paging is the same tradeoff `RefreshUpcomingReleasesAsync`
already makes for the same input (a followed-and-matched author/series list) - it is bounded by how
many things a user follows, not by library size.

A legacy row and a roster entry can describe the exact same real-world book (the scrape poll and a
roster refresh both learned about it independently). They're de-duplicated per request on the
**unified identity**: the roster entry's `(source_name, source_book_id)` - the same key that keeps
one book to one row across the author and series scopes - suppresses the legacy duplicate; an
entry without a source id falls back to the scope + normalized-title key the legacy rows
themselves are matched on. The roster-derived entry wins the duplicate, since it is the one that
carries a working "remove" action (see below). The merged, deduplicated set is sorted by effective
release date (`UpcomingReleaseItem.SortDate`: the precise `ReleaseDate` when known, else January
1st of `Year`, else last) and paged in memory.

The dedup key set is built from each followed series'/author's roster entries classified
`Upcoming` **and** `Ignored` - not `Upcoming` alone. If only `Upcoming` entries suppressed the
legacy duplicate, dismissing a roster-derived "upcoming" entry (which moves it from `Upcoming` to
`Ignored`) would immediately resurrect the same book as a fresh "Legacy" row, since nothing would
suppress it any more. Consulting `Ignored` too keeps a dismissed roster entry suppressing its
legacy duplicate permanently, the same way dismissing it makes it disappear from the roster's own
view - an ignored entry never contributes a visible item, only a dedup key.

Each returned `UpcomingReleaseItem`/`UpcomingReleaseDto` carries a `Source` discriminator
(`"Legacy"` or `"Roster"`) telling the client which removal call applies:

- `"Legacy"` - a real `UpcomingRelease` row. Remove with the existing
  `DELETE api/UpcomingReleases/{id}` (`Id` is set).
- `"Roster"` - a series/author roster entry classified `Upcoming`. There is no row to delete, so
  "remove" instead sets `IsIgnored` on the unified expected-book row via
  `POST api/UpcomingReleases/dismiss-roster` (`ExpectedBookId` is set on the item and is the
  preferred addressing - see below).

### Dismissal addressing: stable id, then source identity, then the natural key

Every roster entry the merged view (or the author detail) can describe is dismissed by its least
ambiguous identity, in order:

1. **the stable expected-book row id** (`UpcomingReleaseDto.ExpectedBookId`, also carried by the
   author detail's own ignore/unignore routes as `AuthorExpectedBookRefDto.Id`) - unambiguous even
   for a person with two same-titled entries, because the unified rows keep their id across
   refreshes;
2. **the source identity** (`SourceName` + `SourceBookId` - the unified row's dedup key);
3. **the natural-key title path** (series name + position + title, or author id + title) kept as
   the compatibility fallback for callers that only carry what the source reported.

Dismissing the shared row hides the book from every scope (author, series, other authors) at once,
and the series-facing reconciliation cache is invalidated when the row is series-linked.

## The shared write gate

Rows on the unified table are mutated by BOTH scopes - the author refresh
(`IUpcomingReleaseService.RefreshAuthorRosterAsync` and its bulk/match-triggered siblings) and the
series side's match/refresh/pending-apply/delete - and a book's row serves both, so the
per-controller semaphores that used to guard each scope separately cannot stop one scope's
read-then-upsert-then-prune from racing the other's on the same rows. Every roster-REWRITE entry
point therefore takes one process-wide `IExpectedBookWriteGate` (a non-reentrant, never-awaited
try-acquire semaphore, registered as a singleton so every controller and background task shares one
instance): controllers acquire it (`TryAcquire`) and return `409` when it is busy; fire-and-forget
operations hand it to `BackgroundOperationRunner` so the gate is held for the whole background run
and released in its `finally`. The author endpoints that reach
`RefreshAuthorRosterCoreAsync` - the single refresh, the bulk sweep, and the match-triggered
refresh inside `MatchAuthor` - take this same gate as the series roster mutations. The single-row
ignore-flag dismissals (id-, source-, and natural-key-addressed) deliberately take NO gate: they
are set-based single-row `ExecuteUpdateAsync` statements, so - unlike a tracked read-modify-write -
a concurrent rewrite's prune/orphan-delete can never race them into a
`DbUpdateConcurrencyException`.

## The migration story: two tables to one

The feature shipped with two parallel roster tables (`series_expected_books` per matched series,
`author_expected_books` per matched author; the author side was scoped to standalone books only).
The unified `expected_books`/`expected_book_authors` migration (`AddUnifiedExpectedBooks`) is
additive: it copies every legacy row into the unified tables with a synthetic `source_book_id`
(`ExpectedBook.LegacyAuthorSyntheticPrefix`/`LegacySeriesSyntheticPrefix` plus the legacy row id -
the `legacy-` namespace is reserved so a real source id can never collide with it), and
author-side rows get their author link. Until a refresh adopts a copied row, the two sources of
truth coexist and report the same books.

Adoption happens on the first refresh: `ExpectedBookRepository.UpsertAsync` matches an incoming
row by its real source identity, and when no row carries it yet, a row whose `SourceBookId` is a
synthetic legacy id (or null - an id-less poll) and whose natural key matches is **adopted in
place** - `SourceBookId` is overwritten with the real id and the row's data refreshed, rather than
a duplicate being created; a row whose source id is already real is never adopted by natural key.
A book the source no longer reports is unlinked from its scopes on the next prune and, once it has
no author link and no series link left, deleted as an orphan.

Once the copy landed, the final migration (`DropLegacyExpectedBookTables`) drops the two legacy
tables and their indexes. Its `Down` recreates them **schema only**: SQLite cannot restore the
dropped rows, and the data lives on in the copied (and since adopted) unified rows anyway, so a
downgraded build reads an empty legacy roster rather than failing on missing tables. This is a
documented, accepted tradeoff - the drop is de facto irreversible.

## API surface summary (for the frontend)

- `SeriesDetailDto` (`GET api/Series/detail`) gained `UpcomingBooks` (`SeriesExpectedBookPageDto`,
  same shape as `MissingBooks`) and new `upcomingPage`/`upcomingPageSize` query parameters.
  `SeriesExpectedBookDto` gained `ReleaseDate` (nullable `DateOnly`, ISO date on the wire).
  `SeriesOverviewDto` gained `UpcomingBookCount` (int).
- `AuthorDetailDto` (`GET api/Browse/authors/{authorId}`) gained `LastRefreshedAt` (nullable
  `DateTime`), `MissingBooks`, `UpcomingBooks`, `IgnoredBooks` (`List<AuthorExpectedBookDto>` -
  unified row id, title, year, source url/identity and cover, series placement/link, ignored
  flag, release date) and `MissingSeries` (`PaginatedResult<AuthorMissingSeriesDto>`, opt-in via
  `includeMissingSeries`/`missingSeriesLimit`/`missingSeriesOffset`). Authors have the same
  ignore/unignore parity series have: `POST api/Browse/authors/{authorId}/expected-books/ignore`
  and `.../unignore` (body `{Id?, Title}`) - addressed by the stable expected-book row id when
  present, by title otherwise (the title route is the compatibility fallback, mirroring
  `SeriesController`'s pair on the SAME shared row, so dismissing here hides the book everywhere).
- New: `POST api/Browse/authors/{authorId}/refresh` -> `AuthorRefreshResultDto {Success,
  LastRefreshedAt}`. `POST api/Browse/authors/refresh-all` is fire-and-forget (mirroring
  `SeriesController`'s refresh-all rather than the single-author refresh): it returns immediately
  once accepted and the client polls `GET api/operations/author-roster-refresh-all/status`, since
  the synchronous form could run for minutes at the source's rate limit and would commonly hit a
  reverse proxy's or browser's request timeout. Both the single-author refresh and the sweep (and
  the refresh triggered by matching an author) take the shared `IExpectedBookWriteGate` above - a
  busy gate returns `409`.
- `GET api/UpcomingReleases` items are now `UpcomingReleaseDto {Source, Id?, Title, ReleaseDate?,
  Year?, AuthorId?, AuthorName?, SeriesId?, SeriesName?, SeriesPosition?, SourceName, SourceUrl?,
  ImageUrl?, ExpectedBookId?, SourceBookId?}` - `Id`/`ReleaseDate` are now nullable (a
  roster-derived row has neither a legacy id nor always a precise date); `ExpectedBookId` is the
  stable unified row id for `"Roster"` items. `DELETE api/UpcomingReleases/{id}` is unchanged, for
  `"Legacy"` rows. New: `POST api/UpcomingReleases/dismiss-roster` (body:
  `{ExpectedBookId?}` or `{SourceName, SourceBookId}` or `{SeriesName?, SeriesPosition?,
  AuthorId?, Title}`, exactly one of the three) for `"Roster"` rows.
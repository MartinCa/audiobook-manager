# Missing books, upcoming books, and Upcoming Releases

This note explains how the three concepts relate after this change, for whoever builds the
frontend half of this feature.

## The roster is now the single source of truth for Missing/Upcoming

A series' roster (`SeriesExpectedBook`) and an author's standalone-books roster
(`AuthorExpectedBook`, new in this change) each store what a metadata source says exists.
Reconciling that roster against the library's owned books already classified every roster entry
as "owned" or "not owned" (`SeriesReconciliationProvider` / the new `AuthorReconciliationProvider`
mirror). This change adds one more split on top of "not owned": is the book released yet?

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

A precise `ReleaseDate` (nullable `DateOnly`, new on both roster models) is preferred whenever the
source has reported one; existing rows (matched before this change) have it `null` and fall back
to the `Year` heuristic until their series/author is refreshed again - either by a user action or
by the existing periodic worker, no forced backfill migration.

Both reconciliations expose `Missing` and `Upcoming` as separate lists (`SeriesReconciliation`,
`AuthorReconciliation`), and the series/author detail DTOs (`SeriesDetailDto.UpcomingBooks`,
`AuthorDetailDto.UpcomingBooks`) carry both. **The per-series/per-author detail page shows
missing/upcoming regardless of follow status** - it's the same roster either way.

## The author roster covers standalone books only

An author's roster is scoped to books that belong to **no series** - series-scoped missing/upcoming
already comes from that series' own roster and is attributed to the author through the existing
series/author relationships (`AuthorDetail.tsx`'s existing "Series" section). The scraper's author
bibliography query (`IScraper.GetAuthorBooks`, `HardcoverScraper.GetAuthorBooks`) returns each
book's series membership; `UpcomingReleaseService.RefreshAuthorRosterCoreAsync` filters out
anything with a series before storing the roster, so a series book is never double-counted between
the author roster and its series' roster.

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
review step in the first place - `RefreshAuthorRosterAsync` replaces the roster directly, no
`PendingSeriesRefresh`-equivalent table.

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
roster refresh both learned about it independently). They're de-duplicated per request, scoped to
the same series/author and matched by normalized title; the roster-derived entry wins the
duplicate, since it is the one that carries a working "remove" action (see below). The merged,
deduplicated set is sorted by effective release date (`UpcomingReleaseItem.SortDate`: the precise
`ReleaseDate` when known, else January 1st of `Year`, else last) and paged in memory.

Each returned `UpcomingReleaseItem`/`UpcomingReleaseDto` carries a `Source` discriminator
(`"Legacy"` or `"Roster"`) telling the client which removal call applies:

- `"Legacy"` - a real `UpcomingRelease` row. Remove with the existing
  `DELETE api/UpcomingReleases/{id}` (`Id` is set).
- `"Roster"` - a series/author roster entry classified `Upcoming`. There is no row to delete, so
  "remove" instead sets `IsIgnored` on that roster entry (the exact mechanism the missing-books
  section already uses to dismiss an entry) via the new
  `POST api/UpcomingReleases/dismiss-roster`, addressed by `SeriesName`+`SeriesPosition`+`Title` or
  `AuthorId`+`Title` (`Id` is null for these rows - roster ids are not stable across a refresh).

## API surface summary (for the frontend)

- `SeriesDetailDto` (`GET api/Series/detail`) gained `UpcomingBooks` (`SeriesExpectedBookPageDto`,
  same shape as `MissingBooks`) and new `upcomingPage`/`upcomingPageSize` query parameters.
  `SeriesExpectedBookDto` gained `ReleaseDate` (nullable `DateOnly`, ISO date on the wire).
  `SeriesOverviewDto` gained `UpcomingBookCount` (int).
- `AuthorDetailDto` (`GET api/Browse/authors/{authorId}`) gained `LastRefreshedAt` (nullable
  `DateTime`), `MissingBooks` and `UpcomingBooks` (`List<AuthorExpectedBookDto>` - `Id`, `Title`,
  `Year`, `SourceUrl`, `IsIgnored`, `ReleaseDate`).
- New: `POST api/Browse/authors/{authorId}/refresh` -> `AuthorRefreshResultDto {Success,
  LastRefreshedAt}` and `POST api/Browse/authors/refresh-all` -> `AuthorRefreshAllResultDto
  {Processed, Succeeded, Failed}`. Both are synchronous (no SignalR progress stream, unlike
  `SeriesController`'s refresh-all) - an author refresh has no pending-review fan-out to report
  progress on.
- `GET api/UpcomingReleases` items are now `UpcomingReleaseDto {Source, Id?, Title, ReleaseDate?,
  Year?, AuthorId?, AuthorName?, SeriesId?, SeriesName?, SeriesPosition?, SourceName, SourceUrl?,
  ImageUrl?}` - `Id` and `ReleaseDate` are now nullable (a roster-derived row has neither a stable
  id nor always a precise date). `DELETE api/UpcomingReleases/{id}` is unchanged, for `"Legacy"`
  rows. New: `POST api/UpcomingReleases/dismiss-roster` (body:
  `{SeriesName?, SeriesPosition?, AuthorId?, Title}`, exactly one of `SeriesName`/`AuthorId` set)
  for `"Roster"` rows.

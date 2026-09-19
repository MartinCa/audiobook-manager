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

## The legacy `UpcomingRelease` table stays, scoped to the global page

The global Upcoming Releases page (`/library/upcoming-releases`) has one requirement the
roster doesn't by itself satisfy: **only followed** authors/series, and a per-row "remove" action
a user expects to stick. The existing scrape pipeline (`UpcomingReleaseService`,
`UpcomingReleasesWorker`, the `UpcomingRelease` table) already does exactly that - it only ever
polls followed-and-matched authors/series, and a user's removal is a real `DELETE` that the
worker's `UpsertAsync` dedupe (keyed on source name + source book id) never resurrects on its own.

Rather than replace that pipeline with a roster-union query (a materially bigger, riskier change:
the roster's classification is a live view with no per-user "I dismissed this" state, so a
roster-derived "remove" would need a new dismissal table anyway, at which point the existing table
already *is* that store), **this change keeps `UpcomingReleaseService`/`UpcomingReleasesWorker`/
`UpcomingRelease` exactly as they were** for the global page. `UpcomingReleasesController` is
unchanged.

This is a deliberate, documented scope decision, not an oversight: the frontend can keep consuming
`GET api/UpcomingReleases` exactly as today for the global page, while the series/author detail
pages switch to the new roster-derived `MissingBooks`/`UpcomingBooks` sections for their own
(follow-status-independent) view. The two surfaces answer two different questions - "what's
missing/upcoming for this roster" vs. "what's coming for what I follow, with a page I can curate" -
and they now share the same underlying classification helper and roster data model, even though
the global page's list is still populated by its own scrape-and-store poll rather than derived
live from the roster on every request.

**Future work**, not done here: teach `UpcomingReleaseService.GetUpcomingReleasesAsync` to union in
roster-derived entries for followed series/authors that the scrape poll hasn't (yet) discovered,
with removal for a roster-derived row implemented as `AuthorExpectedBook.IsIgnored` /
`SeriesExpectedBook.IsIgnored` rather than a `DELETE` (which only makes sense for a real
`UpcomingRelease` row). That is a bigger, separate change and is intentionally out of scope here.

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
- `UpcomingReleasesController`/`GET api/UpcomingReleases` is unchanged.

import type { MetadataSearchResult } from "@/types/MetadataSearchResult";
import type { components } from "@/lib/api-types";
import type { Audiobook } from "@/types/Audiobook";
import { cleanDescription, normalizeSeriesPart } from "@/helpers/organizeAudiobookInput";

/** The raw wire shape of the stored snapshot; every member is optional in api-types (see Require docs). */
type SnapshotWireShape = components["schemas"]["PendingRefreshSnapshotDto"];

/**
 * Converts the stored pending-refresh snapshot (a hand-versioned wire contract, see
 * MetadataRefreshDtos.cs) into the live MetadataSearchResult shape the diff/approval UI —
 * TagPreviewDialog and BookEditForm's apply flow — already knows how to render and apply.
 * The snapshot is deliberately missing two fields TagPreviewDialog reads: cleanUrl (the source
 * URL minus tracking params) and imageUrl. cleanUrl falls back to the snapshot's own url, which
 * is already the cleaned URL the scraper reported when it was stored (pending rows store
 * fetched.CleanUrl as SourceUrl); imageUrl is not stored at all, so the cover row is dropped
 * from the preview rather than guessed at.
 */
export function pendingSnapshotToSearchResult(snapshot: SnapshotWireShape): MetadataSearchResult {
  return {
    url: snapshot.url ?? "",
    cleanUrl: snapshot.url ?? "",
    source: snapshot.source ?? "",
    authors: (snapshot.authors ?? []).map((name) => ({ name })),
    narrators: (snapshot.narrators ?? []).map((name) => ({ name })),
    bookName: snapshot.bookName ?? "",
    subtitle: snapshot.subtitle ?? undefined,
    year: snapshot.year ?? undefined,
    genres: snapshot.genres ?? [],
    description: snapshot.description ?? undefined,
    language: snapshot.language ?? undefined,
    rating: snapshot.rating ? Number(snapshot.rating) : undefined,
    copyright: snapshot.copyright ?? undefined,
    publisher: snapshot.publisher ?? undefined,
    asin: snapshot.asin ?? undefined,
    series: snapshot.seriesName
      ? [
          {
            seriesName: snapshot.seriesName,
            seriesPart: snapshot.seriesPart ?? undefined,
          },
        ]
      : [],
  };
}

/**
 * Applies the fields the user ticked in the TagPreviewDialog onto a fresh Audiobook built from
 * the current book, the same field-for-field the interactive search flow applies (BookEditForm's
 * handleApplyPreviewedTags); the result is then saved through the normal save pipeline, so the
 * binding invariant (Author/Series/SeriesPart/Year/BookName only change via UpdateAudiobook)
 * holds and the metadataAppliedFromSearch timestamp is stamped by the backend.
 */
export function applyPendingRefreshSelection(
  result: MetadataSearchResult,
  selectedFields: Set<string>,
  current: Audiobook,
): Audiobook {
  const applied: Audiobook = {
    ...current,
  };

  if (selectedFields.has("bookName") && result.bookName) applied.bookName = result.bookName;
  if (selectedFields.has("subtitle") && result.subtitle) applied.subtitle = result.subtitle;
  if (selectedFields.has("authors") && result.authors && result.authors.length > 0) {
    applied.authors = result.authors.map((a) => ({ name: a.name }));
  }
  if (selectedFields.has("narrators") && result.narrators && result.narrators.length > 0) {
    applied.narrators = result.narrators.map((n) => ({ name: n.name }));
  }
  if (selectedFields.has("series")) {
    const firstSeries = result.series?.[0];
    if (firstSeries?.seriesName !== undefined) applied.series = firstSeries.seriesName || undefined;
    if (firstSeries?.seriesPart !== undefined) {
      applied.seriesPart = normalizeSeriesPart(firstSeries.seriesPart || "") || undefined;
    }
  }
  if (selectedFields.has("year") && result.year) applied.year = result.year;
  if (selectedFields.has("genres") && result.genres && result.genres.length > 0) {
    applied.genres = result.genres;
  }
  if (selectedFields.has("description") && result.description) {
    applied.description = cleanDescription(result.description);
  }
  if (selectedFields.has("copyright") && result.copyright) applied.copyright = result.copyright;
  if (selectedFields.has("publisher") && result.publisher) applied.publisher = result.publisher;
  if (selectedFields.has("language") && result.language) applied.language = result.language;
  if (selectedFields.has("rating") && result.rating) applied.rating = String(result.rating);
  if (selectedFields.has("asin") && result.asin) applied.asin = result.asin;
  if (selectedFields.has("www") && result.cleanUrl) applied.www = result.cleanUrl;

  return applied;
}

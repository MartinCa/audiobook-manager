import type { components } from "@/lib/api-types";
import type { MetadataSearchResult } from "@/types/MetadataSearchResult";

/** The raw wire shape of the stored snapshot; every member is optional in api-types (see Require docs). */
type SnapshotWireShape = components["schemas"]["PendingRefreshSnapshotDto"];

/**
 * Converts the stored pending-refresh snapshot (a hand-versioned wire contract, see
 * MetadataRefreshDtos.cs) into the live MetadataSearchResult shape the diff/approval UI —
 * TagPreviewDialog and BookEditForm's apply flow — already knows how to render and apply.
 * BookDetail hands the result of this straight to BookEditForm's pendingRefreshResult prop,
 * which renders its own TagPreviewDialog and routes "Apply" through the mounted form (see
 * BookEditForm's handleApplyPendingRefresh) rather than building and saving an Audiobook
 * directly — that is what keeps the displayed form in sync with what actually got saved.
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

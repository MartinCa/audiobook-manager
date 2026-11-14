import { useCallback, useEffect } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { metadataSearchApi, settingsApi } from "@/services/api";
import { queryKeys } from "@/lib/queryKeys";
import { buildDefaultMetadataSearchQuery } from "@/helpers/metadataSearchQuery";
import type { LibrarySettings } from "@/types/LibrarySettings";

/**
 * The query the interactive metadata search is seeded with. `query` is the text shown in the box.
 * `queriesBySource`, when present, is the query for each source, each written with the initials
 * handling that applies to that source - the search sends those instead of `query` while the box
 * still holds the unedited seed, and it is absent when every source would be asked the same thing.
 */
export interface ResolvedDefaultSearchQuery {
  query: string;
  queriesBySource?: Record<string, string>;
}

/**
 * Whether any source's default query can differ from the as-stored one: the library-wide initials
 * handling, or a per-source override, is something other than `AsStored`. Only then does the
 * server need to be asked; otherwise the as-stored query is exact for every source.
 */
function rewritesInitials(settings: LibrarySettings): boolean {
  return (
    settings.searchInitialsHandling !== "AsStored" ||
    Object.values(settings.searchInitialsHandlingBySource ?? {}).some((h) => h !== "AsStored")
  );
}

/** A stable text form of the settings that decide the query, for the query key. */
function handlingSignature(settings: LibrarySettings): string {
  const overrides = Object.entries(settings.searchInitialsHandlingBySource ?? {})
    .map(([source, handling]) => `${source}=${handling}`)
    .sort();
  return [settings.searchInitialsHandling, ...overrides].join("|");
}

/**
 * Returns a function that resolves the query the interactive metadata search is seeded with.
 * With no initials rewriting configured (library-wide handling `AsStored` and no per-source
 * override) that is just `buildDefaultMetadataSearchQuery`; otherwise the backend builds it (the
 * same `MetadataSearchQueryBuilder` the bulk search uses), so the client carries no copy of the
 * initials rules. A book's main author, when it has one, is the only author in the query either
 * way. If the settings or the server request fail, the as-stored query is used, so the dialog is
 * never seeded with nothing. The requests do not retry (the app's default policy would hold the
 * dialog back for the retries before the fallback applied), and the settings are prefetched on
 * mount so that opening the dialog is normally instant.
 *
 * It resolves *before* the search dialog opens, rather than being a value that changes after it:
 * `BookSearchDialog` re-seeds its input whenever `initialQuery` changes, so a seed that arrived
 * late would overwrite text the user had already typed.
 */
const SETTINGS_STALE_MS = 5 * 60 * 1000;

export function useResolveDefaultMetadataSearchQuery() {
  const queryClient = useQueryClient();

  useEffect(() => {
    void queryClient.prefetchQuery({
      queryKey: queryKeys.librarySettings(),
      queryFn: () => settingsApi.getLibrarySettings(),
      staleTime: SETTINGS_STALE_MS,
      retry: false,
    });
  }, [queryClient]);

  return useCallback(
    async (
      authors: readonly string[] | undefined,
      bookName: string | undefined,
      fileName: string | undefined,
      mainAuthor?: string,
    ): Promise<ResolvedDefaultSearchQuery> => {
      const asStored = buildDefaultMetadataSearchQuery(authors, bookName, fileName, mainAuthor);

      try {
        // fetchQuery serves a fresh cache entry and refetches an invalidated one, so a change
        // saved on the settings page is picked up on the next open.
        const settings = await queryClient.fetchQuery({
          queryKey: queryKeys.librarySettings(),
          queryFn: () => settingsApi.getLibrarySettings(),
          staleTime: SETTINGS_STALE_MS,
          retry: false,
        });
        if (!rewritesInitials(settings)) return { query: asStored };

        const result = await queryClient.fetchQuery({
          queryKey: queryKeys.defaultMetadataSearchQuery(
            authors ?? [],
            bookName ?? "",
            fileName ?? "",
            handlingSignature(settings),
            mainAuthor ?? "",
          ),
          queryFn: () =>
            metadataSearchApi.getDefaultQuery({ authors, bookName, fileName, mainAuthor }),
          staleTime: 60 * 1000,
          retry: false,
        });
        const queriesBySource = result.queriesBySource ?? undefined;
        const queries = Object.values(queriesBySource ?? {});
        const distinct = new Set(queries);
        // Every source agrees (even if that differs from the library-wide form, because each has
        // its own override): that one query is what the box should show, and nothing per-source
        // needs sending.
        const [only] = queries;
        if (distinct.size === 1 && only !== undefined) return { query: only };
        return {
          query: result.query ?? asStored,
          // Sources disagree: the box shows the library-wide form and the search sends each
          // source its own while the text is unedited.
          queriesBySource: distinct.size > 1 ? queriesBySource : undefined,
        };
      } catch {
        return { query: asStored };
      }
    },
    [queryClient],
  );
}

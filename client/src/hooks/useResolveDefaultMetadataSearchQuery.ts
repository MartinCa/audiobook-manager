import { useCallback, useEffect } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { metadataSearchApi, settingsApi } from "@/services/api";
import { queryKeys } from "@/lib/queryKeys";
import { buildDefaultMetadataSearchQuery } from "@/helpers/metadataSearchQuery";

/**
 * Returns a function that resolves the query the interactive metadata search is seeded with.
 * With the library's search-initials handling on `AsStored` that is just
 * `buildDefaultMetadataSearchQuery`; otherwise the backend builds it (the same
 * `MetadataSearchQueryBuilder` the bulk search uses), so the client carries no copy of the
 * initials rules. If the settings or the server request fail, the as-stored query is used, so the
 * dialog is never seeded with nothing. The requests do not retry (the app's default policy would
 * hold the dialog back for the retries before the fallback applied), and the settings are
 * prefetched on mount so that opening the dialog is normally instant.
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
    ): Promise<string> => {
      const asStored = buildDefaultMetadataSearchQuery(authors, bookName, fileName);

      try {
        // fetchQuery serves a fresh cache entry and refetches an invalidated one, so a change
        // saved on the settings page is picked up on the next open.
        const settings = await queryClient.fetchQuery({
          queryKey: queryKeys.librarySettings(),
          queryFn: () => settingsApi.getLibrarySettings(),
          staleTime: SETTINGS_STALE_MS,
          retry: false,
        });
        const handling = settings.searchInitialsHandling;
        if (handling === "AsStored") return asStored;

        const result = await queryClient.fetchQuery({
          queryKey: queryKeys.defaultMetadataSearchQuery(
            authors ?? [],
            bookName ?? "",
            fileName ?? "",
            handling,
          ),
          queryFn: () => metadataSearchApi.getDefaultQuery({ authors, bookName, fileName }),
          staleTime: 60 * 1000,
          retry: false,
        });
        return result.query ?? asStored;
      } catch {
        return asStored;
      }
    },
    [queryClient],
  );
}

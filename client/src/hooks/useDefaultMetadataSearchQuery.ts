import { useQuery } from "@tanstack/react-query";
import { metadataSearchApi, settingsApi } from "@/services/api";
import { queryKeys } from "@/lib/queryKeys";
import { buildDefaultMetadataSearchQuery } from "@/helpers/metadataSearchQuery";

/**
 * The query the interactive metadata search is seeded with. With the library's search-initials
 * handling on `AsStored` this is just `buildDefaultMetadataSearchQuery`; otherwise the backend
 * builds it (the same `MetadataSearchQueryBuilder` the bulk search uses), so the client carries no
 * copy of the initials rules. Until that answers - or if it fails - the as-stored query is used,
 * so the dialog is never seeded with nothing.
 *
 * Only fetches while `enabled` (the dialog is open): the inputs change on every keystroke in the
 * edit form, and the result is only read when the dialog opens.
 */
export function useDefaultMetadataSearchQuery(
  authors: readonly string[] | undefined,
  bookName: string | undefined,
  fileName: string | undefined,
  enabled: boolean,
): string {
  const asStored = buildDefaultMetadataSearchQuery(authors, bookName, fileName);

  const { data: settings } = useQuery({
    queryKey: queryKeys.librarySettings(),
    queryFn: () => settingsApi.getLibrarySettings(),
    staleTime: 5 * 60 * 1000,
  });
  const handling = settings?.searchInitialsHandling ?? "AsStored";

  const { data } = useQuery({
    queryKey: queryKeys.defaultMetadataSearchQuery(
      authors ?? [],
      bookName ?? "",
      fileName ?? "",
      handling,
    ),
    queryFn: () => metadataSearchApi.getDefaultQuery({ authors, bookName, fileName }),
    enabled: enabled && handling !== "AsStored",
    staleTime: 60 * 1000,
  });

  if (handling === "AsStored") return asStored;
  return data?.query ?? asStored;
}

import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { settingsApi } from "@/services/api";
import { queryKeys } from "@/lib/queryKeys";
import { PAGE_SIZE, type PageSizeOption } from "@/constants/paging";

/**
 * Rows-per-page for a paged list, defaulting to the library's `defaultPageSize` setting until the
 * user picks something else in the session (the override is local component state, not persisted -
 * only the *default* a fresh list starts on is a saved setting; see LibrarySettingsPage).
 *
 * Reads settings via the shared `librarySettings` query key, so every list on the page shares one
 * cached fetch rather than each list component re-requesting it.
 */
export function usePageSize(): [number, (size: PageSizeOption) => void] {
  const { data } = useQuery({
    queryKey: queryKeys.librarySettings(),
    queryFn: () => settingsApi.getLibrarySettings(),
    staleTime: 5 * 60 * 1000,
  });
  const [override, setOverride] = useState<PageSizeOption | null>(null);

  const pageSize = override ?? data?.defaultPageSize ?? PAGE_SIZE;
  return [pageSize, setOverride];
}

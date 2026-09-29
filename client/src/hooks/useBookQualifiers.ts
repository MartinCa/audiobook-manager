import { useQuery } from "@tanstack/react-query";
import { queryKeys } from "@/lib/queryKeys";
import { settingsApi } from "@/services/api";
import type { BookQualifierOption } from "@/types/BookQualifier";

const NO_OPTIONS: BookQualifierOption[] = [];

/**
 * The book qualifiers (abridged, dramatized, ...) the backend supports. The list is fixed for the
 * lifetime of a deployment, so it is fetched once and never goes stale; until it arrives
 * consumers see an empty list and fall back to the raw keys.
 */
export function useBookQualifiers(): BookQualifierOption[] {
  const { data } = useQuery({
    queryKey: queryKeys.bookQualifiers(),
    queryFn: () => settingsApi.getBookQualifiers(),
    staleTime: Infinity,
  });
  return data?.qualifiers ?? NO_OPTIONS;
}

import { useQuery } from "@tanstack/react-query";
import { queryKeys } from "@/lib/queryKeys";
import { settingsApi } from "@/services/api";
import type { MetadataApplyRules } from "@/types/MetadataApplyRules";

/**
 * The per-field rules for applying online metadata. `ready` flips once the request has settled
 * either way, so a review can wait for the rules before choosing what to tick but never hangs on a
 * failed request: without rules it falls back to ticking everything that changed, which is how
 * reviews behaved before the rules existed.
 */
export function useMetadataApplyRules(): { rules: MetadataApplyRules | undefined; ready: boolean } {
  const { data, isPending } = useQuery({
    queryKey: queryKeys.metadataApplyRules(),
    queryFn: () => settingsApi.getMetadataApplyRules(),
    staleTime: 5 * 60 * 1000,
  });
  return { rules: data, ready: !isPending };
}

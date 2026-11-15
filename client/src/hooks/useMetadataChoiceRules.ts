import { useQuery } from "@tanstack/react-query";
import { choiceRulesFrom, type ChoiceRules } from "@/helpers/metadataChoice";
import { queryKeys } from "@/lib/queryKeys";
import { settingsApi } from "@/services/api";

/**
 * The rules for choosing the main author and primary series when online metadata is applied.
 * `ready` flips once the request has settled either way; without rules every choice keeps the
 * book's current one, which is how applies behaved before the rules existed.
 */
export function useMetadataChoiceRules(): { rules: ChoiceRules; ready: boolean } {
  const { data, isPending } = useQuery({
    queryKey: queryKeys.metadataChoiceRules(),
    queryFn: () => settingsApi.getMetadataChoiceRules(),
    staleTime: 5 * 60 * 1000,
  });
  return { rules: choiceRulesFrom(data), ready: !isPending };
}

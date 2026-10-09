import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { queryKeys } from "@/lib/queryKeys";
import { filterPresetsApi } from "@/services/api";
import type { FilterPreset, FilterPresetScope } from "@/types/FilterPreset";

const NO_PRESETS: FilterPreset[] = [];

/**
 * The saved filter presets of one library list, plus the writes that change them. The list is
 * small and bounded by the backend (FilterPresetRules.MaxPresetsPerScope), so it is fetched whole
 * and every write just refetches it. A write rejects with the ApiError the caller shows (a taken
 * name arrives as a 409 with its message).
 */
export function useFilterPresets(scope: FilterPresetScope) {
  const queryClient = useQueryClient();
  const queryKey = queryKeys.filterPresets.byScope(scope);

  const query = useQuery({ queryKey, queryFn: () => filterPresetsApi.list(scope) });
  const refresh = () => queryClient.invalidateQueries({ queryKey });

  const create = useMutation({
    mutationFn: (input: { name: string; filters: Record<string, unknown> }) =>
      filterPresetsApi.create(scope, input.name, input.filters),
    onSuccess: refresh,
  });

  const update = useMutation({
    mutationFn: (input: { id: number; name: string; filters: Record<string, unknown> }) =>
      filterPresetsApi.update(input.id, input.name, input.filters),
    onSuccess: refresh,
  });

  const remove = useMutation({
    mutationFn: (id: number) => filterPresetsApi.remove(id),
    onSuccess: refresh,
  });

  return {
    presets: query.data ?? NO_PRESETS,
    isLoading: query.isLoading,
    error: query.error,
    refetch: query.refetch,
    create,
    update,
    remove,
  };
}

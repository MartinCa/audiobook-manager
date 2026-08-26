import { Ref, ref, watch } from "vue";
import { MetadataSearchServiceInfo } from "../types/MetadataSearchServiceInfo";

const storageKey = "abm.search.selectedSources";

function readStoredSources(): string[] | null {
  try {
    const raw = localStorage.getItem(storageKey);
    return raw ? JSON.parse(raw) : null;
  } catch {
    return null;
  }
}

function writeStoredSources(sources: string[]) {
  try {
    localStorage.setItem(storageKey, JSON.stringify(sources));
  } catch {
    // Ignore storage failures (e.g. private browsing) — selection just won't persist.
  }
}

export function useSelectedSearchSources(
  services: Ref<MetadataSearchServiceInfo[]>,
): Ref<string[]> {
  const selectedSources: Ref<string[]> = ref([]);

  watch(
    services,
    (newServices) => {
      if (!newServices.length) {
        return;
      }

      const enabledNames = newServices
        .filter((s) => s.enabled)
        .map((s) => s.name);

      const stored = readStoredSources();
      const restored = stored?.filter((s) => enabledNames.includes(s)) ?? [];

      selectedSources.value = restored.length ? restored : enabledNames;
    },
    { immediate: true },
  );

  watch(
    selectedSources,
    (sources) => {
      writeStoredSources(sources);
    },
    { deep: true },
  );

  return selectedSources;
}

import { Ref, ref, watch } from "vue";
import { MissingTagField } from "../types/MissingTag";

const storageKey = "abm.missingTags.selectedFields";

function readStoredFields(): string[] | null {
  try {
    const raw = localStorage.getItem(storageKey);
    return raw ? JSON.parse(raw) : null;
  } catch {
    return null;
  }
}

function writeStoredFields(fields: string[]) {
  try {
    localStorage.setItem(storageKey, JSON.stringify(fields));
  } catch {
    // Ignore storage failures (e.g. private browsing) — selection just won't persist.
  }
}

export function useMissingTagSelection(
  fields: Ref<MissingTagField[]>,
): Ref<string[]> {
  const selectedFields: Ref<string[]> = ref([]);

  watch(
    fields,
    (newFields) => {
      if (!newFields.length) {
        return;
      }

      const availableKeys = newFields.map((f) => f.key);
      const stored = readStoredFields();
      const restored = stored?.filter((k) => availableKeys.includes(k)) ?? [];

      selectedFields.value = restored.length
        ? restored
        : newFields.filter((f) => f.isCriticalByDefault).map((f) => f.key);
    },
    { immediate: true },
  );

  watch(
    selectedFields,
    (selected) => {
      writeStoredFields(selected);
    },
    { deep: true },
  );

  return selectedFields;
}

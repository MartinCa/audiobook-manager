import { useEffect, useRef, useState } from "react";
import type { MissingTagField } from "@/types/MissingTag";

const STORAGE_KEY = "abm.missingTags.selectedFields";

function readStoredFields(): string[] | null {
  try {
    const raw = localStorage.getItem(STORAGE_KEY);
    if (!raw) return null;
    const parsed: unknown = JSON.parse(raw);
    // Valid JSON is not necessarily the array shape we wrote (e.g. a stale key holding
    // `{}` or `3`), so guard before callers treat it as one.
    return Array.isArray(parsed) ? (parsed as string[]) : null;
  } catch {
    return null;
  }
}

function writeStoredFields(fields: string[]): void {
  try {
    localStorage.setItem(STORAGE_KEY, JSON.stringify(fields));
  } catch {
    // Ignore storage failures (e.g. private browsing) — selection just won't persist.
  }
}

/**
 * Restores the user's field selection from localStorage once `fields` is available
 * (filtered down to keys that still exist), falling back to every field marked
 * `isCriticalByDefault`. Every change is persisted back to localStorage.
 */
export function useMissingTagSelection(
  fields: MissingTagField[],
): [string[], (next: string[]) => void] {
  const [selectedFields, setSelectedFields] = useState<string[]>([]);
  const restoredRef = useRef(false);

  useEffect(() => {
    if (fields.length === 0 || restoredRef.current) return;
    restoredRef.current = true;

    const availableKeys = fields.map((f) => f.key);
    const stored = readStoredFields();
    const restored = stored?.filter((k) => availableKeys.includes(k)) ?? [];

    setSelectedFields(
      restored.length > 0
        ? restored
        : fields.filter((f) => f.isCriticalByDefault).map((f) => f.key),
    );
  }, [fields]);

  useEffect(() => {
    if (restoredRef.current) writeStoredFields(selectedFields);
  }, [selectedFields]);

  return [selectedFields, setSelectedFields];
}

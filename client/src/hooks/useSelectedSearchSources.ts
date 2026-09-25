import { useEffect, useRef, useState } from "react";
import type { MetadataSearchServiceInfo } from "@/types/MetadataSearchServiceInfo";

const STORAGE_KEY = "abm.search.selectedSources";

function readStoredSources(): string[] | null {
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

function writeStoredSources(sources: string[]): void {
  try {
    localStorage.setItem(STORAGE_KEY, JSON.stringify(sources));
  } catch {
    // Ignore storage failures (e.g. private browsing) — selection just won't persist.
  }
}

/**
 * Restores the user's metadata-source selection from localStorage once `services` is available
 * (filtered down to still-enabled sources), falling back to every enabled source only when
 * nothing was ever stored. An explicitly-cleared (empty) selection is restored as empty, not
 * reinterpreted as "no preference" - every change is persisted back to localStorage as-is.
 */
export function useSelectedSearchSources(
  services: MetadataSearchServiceInfo[],
): [string[], (next: string[]) => void] {
  const [selectedSources, setSelectedSources] = useState<string[]>([]);
  const restoredRef = useRef(false);

  useEffect(() => {
    if (services.length === 0 || restoredRef.current) return;
    restoredRef.current = true;

    const enabledNames = services.filter((s) => s.enabled).map((s) => s.name);
    const stored = readStoredSources();
    // `stored === null` means no preference was ever saved - default to every enabled source.
    // A *stored* `[]` means the user previously cleared every source deliberately, and that has
    // to survive remounting the same way any other non-empty selection does: filtering it against
    // the current enabled set (in case a source was disabled since) must not fall back to
    // "select everything" just because the filtered result happens to be empty too - that would
    // silently discard an explicit "nothing" the moment the dialog is reopened.
    setSelectedSources(
      stored === null ? enabledNames : stored.filter((s) => enabledNames.includes(s)),
    );
  }, [services]);

  useEffect(() => {
    if (restoredRef.current) writeStoredSources(selectedSources);
  }, [selectedSources]);

  return [selectedSources, setSelectedSources];
}

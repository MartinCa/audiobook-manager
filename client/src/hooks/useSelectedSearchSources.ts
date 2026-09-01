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
 * (filtered down to still-enabled sources), falling back to every enabled source. Every change
 * is persisted back to localStorage.
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
    const restored = stored?.filter((s) => enabledNames.includes(s)) ?? [];

    setSelectedSources(restored.length > 0 ? restored : enabledNames);
  }, [services]);

  useEffect(() => {
    if (restoredRef.current) writeStoredSources(selectedSources);
  }, [selectedSources]);

  return [selectedSources, setSelectedSources];
}

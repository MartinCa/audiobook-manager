import { describe, it, expect, beforeEach } from "vitest";
import { renderHook, waitFor } from "@testing-library/react";
import { useSelectedSearchSources } from "./useSelectedSearchSources";
import type { MetadataSearchServiceInfo } from "@/types/MetadataSearchServiceInfo";

const STORAGE_KEY = "abm.search.selectedSources";

const services: MetadataSearchServiceInfo[] = [
  { name: "Goodreads", enabled: true },
  { name: "Audible", enabled: true },
  { name: "Hardcover", enabled: false, disabledReason: "No API key configured" },
];

beforeEach(() => {
  localStorage.clear();
});

describe("useSelectedSearchSources", () => {
  it("defaults to every enabled source when nothing is stored", async () => {
    const { result } = renderHook(() => useSelectedSearchSources(services));

    await waitFor(() => expect(result.current[0]).toEqual(["Goodreads", "Audible"]));
  });

  it("restores a previously stored selection, filtered to still-enabled sources", async () => {
    localStorage.setItem(STORAGE_KEY, JSON.stringify(["Goodreads", "Hardcover", "Removed"]));

    const { result } = renderHook(() => useSelectedSearchSources(services));

    await waitFor(() => expect(result.current[0]).toEqual(["Goodreads"]));
  });

  // Regression test: an explicitly-cleared selection ("[]", stored by the user deselecting every
  // source) used to be reinterpreted as "no preference" on the next mount - `stored?.filter(...)
  // ?? []` never short-circuits for a stored `[]` (it's truthy), so filtering it still yields
  // `[]`, and the old fallback (`restored.length > 0 ? restored : enabledNames`) then silently
  // re-selected every enabled source instead of honoring the explicit "nothing".
  it("restores an explicitly-cleared (empty) selection as empty, not as every enabled source", async () => {
    localStorage.setItem(STORAGE_KEY, JSON.stringify([]));

    const { result } = renderHook(() => useSelectedSearchSources(services));

    await waitFor(() => expect(result.current[0]).toEqual([]));
  });

  it("persists a changed selection to localStorage", async () => {
    const { result } = renderHook(() => useSelectedSearchSources(services));
    await waitFor(() => expect(result.current[0]).toEqual(["Goodreads", "Audible"]));

    result.current[1](["Audible"]);

    await waitFor(() =>
      expect(JSON.parse(localStorage.getItem(STORAGE_KEY) ?? "[]")).toEqual(["Audible"]),
    );
  });
});

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

  it("persists a changed selection to localStorage", async () => {
    const { result } = renderHook(() => useSelectedSearchSources(services));
    await waitFor(() => expect(result.current[0]).toEqual(["Goodreads", "Audible"]));

    result.current[1](["Audible"]);

    await waitFor(() =>
      expect(JSON.parse(localStorage.getItem(STORAGE_KEY) ?? "[]")).toEqual(["Audible"]),
    );
  });
});

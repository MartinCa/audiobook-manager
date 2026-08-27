import { describe, it, expect, beforeEach } from "vitest";
import { ref, nextTick } from "vue";
import { useSelectedSearchSources } from "./useSelectedSearchSources";
import { MetadataSearchServiceInfo } from "../types/MetadataSearchServiceInfo";

const storageKey = "abm.search.selectedSources";

describe("useSelectedSearchSources", () => {
  beforeEach(() => {
    localStorage.clear();
  });

  it("selects all enabled services when nothing is stored", () => {
    const services = ref<MetadataSearchServiceInfo[]>([
      { name: "Goodreads", enabled: true },
      { name: "Audible", enabled: true },
      { name: "Hardcover", enabled: false },
    ]);

    const selected = useSelectedSearchSources(services);

    expect(selected.value).toEqual(["Goodreads", "Audible"]);
  });

  it("does nothing while the services list is empty", () => {
    const services = ref<MetadataSearchServiceInfo[]>([]);

    const selected = useSelectedSearchSources(services);

    expect(selected.value).toEqual([]);
  });

  it("persists a selection change to localStorage", async () => {
    const services = ref<MetadataSearchServiceInfo[]>([
      { name: "Goodreads", enabled: true },
      { name: "Audible", enabled: true },
    ]);

    const selected = useSelectedSearchSources(services);
    selected.value = ["Audible"];
    await nextTick();

    expect(JSON.parse(localStorage.getItem(storageKey)!)).toEqual(["Audible"]);
  });

  it("restores a previously saved selection that is still enabled", () => {
    localStorage.setItem(storageKey, JSON.stringify(["Audible"]));
    const services = ref<MetadataSearchServiceInfo[]>([
      { name: "Goodreads", enabled: true },
      { name: "Audible", enabled: true },
    ]);

    const selected = useSelectedSearchSources(services);

    expect(selected.value).toEqual(["Audible"]);
  });

  it("falls back to all enabled services when the stored selection is no longer enabled", () => {
    localStorage.setItem(storageKey, JSON.stringify(["Hardcover"]));
    const services = ref<MetadataSearchServiceInfo[]>([
      { name: "Goodreads", enabled: true },
      { name: "Audible", enabled: true },
      { name: "Hardcover", enabled: false },
    ]);

    const selected = useSelectedSearchSources(services);

    expect(selected.value).toEqual(["Goodreads", "Audible"]);
  });

  it("filters out stored sources that are no longer present, keeping the rest", () => {
    localStorage.setItem(
      storageKey,
      JSON.stringify(["Audible", "RemovedSource"]),
    );
    const services = ref<MetadataSearchServiceInfo[]>([
      { name: "Goodreads", enabled: true },
      { name: "Audible", enabled: true },
    ]);

    const selected = useSelectedSearchSources(services);

    expect(selected.value).toEqual(["Audible"]);
  });

  it("ignores malformed JSON in localStorage and falls back to enabled services", () => {
    localStorage.setItem(storageKey, "{not valid json");
    const services = ref<MetadataSearchServiceInfo[]>([
      { name: "Goodreads", enabled: true },
    ]);

    const selected = useSelectedSearchSources(services);

    expect(selected.value).toEqual(["Goodreads"]);
  });

  it.each([
    ["an object", "{}"],
    ["a number", "3"],
    ["a string", '"Goodreads"'],
    ["null", "null"],
  ])(
    "ignores valid JSON that is not an array (%s) and falls back to enabled services",
    (_label, stored) => {
      localStorage.setItem(storageKey, stored);
      const services = ref<MetadataSearchServiceInfo[]>([
        { name: "Goodreads", enabled: true },
      ]);

      const selected = useSelectedSearchSources(services);

      expect(selected.value).toEqual(["Goodreads"]);
    },
  );

  it("reacts to the services list changing later (e.g. arriving from the live API)", async () => {
    const services = ref<MetadataSearchServiceInfo[]>([]);
    const selected = useSelectedSearchSources(services);
    expect(selected.value).toEqual([]);

    services.value = [
      { name: "NewSourceFromApi", enabled: true },
      { name: "Hardcover", enabled: false },
    ];
    await nextTick();

    expect(selected.value).toEqual(["NewSourceFromApi"]);
  });

  it("regression guard: derives its source list entirely from the injected services ref, never a hardcoded array", async () => {
    // Any set of arbitrary, made-up source names should flow straight through -
    // proving there is no hardcoded fallback list baked into the composable.
    const arbitraryNames = ["ZzzMadeUpSourceOne", "AnotherFictionalSource"];
    const services = ref<MetadataSearchServiceInfo[]>(
      arbitraryNames.map((name) => ({ name, enabled: true })),
    );

    const selected = useSelectedSearchSources(services);
    expect(selected.value).toEqual(arbitraryNames);

    // Changing the injected list changes the output correspondingly - the
    // composable has no source names of its own.
    services.value = [{ name: "YetAnotherOne", enabled: true }];
    await nextTick();
    expect(selected.value).toEqual(["YetAnotherOne"]);
  });
});

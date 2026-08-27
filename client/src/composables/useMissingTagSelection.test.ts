import { describe, it, expect, beforeEach } from "vitest";
import { nextTick, ref } from "vue";
import { useMissingTagSelection } from "./useMissingTagSelection";
import { MissingTagField } from "../types/MissingTag";

const storageKey = "abm.missingTags.selectedFields";

const allFields: MissingTagField[] = [
  { key: "author", label: "Author", isCriticalByDefault: true },
  { key: "series", label: "Series", isCriticalByDefault: false },
  { key: "year", label: "Year", isCriticalByDefault: true },
];

beforeEach(() => {
  localStorage.clear();
});

describe("useMissingTagSelection", () => {
  it("starts with an empty selection", () => {
    const fields = ref<MissingTagField[]>([]);
    const selected = useMissingTagSelection(fields);

    expect(selected.value).toEqual([]);
  });

  it("stays empty while fields remain empty", async () => {
    const fields = ref<MissingTagField[]>([]);
    const selected = useMissingTagSelection(fields);

    fields.value = [];
    await nextTick();

    expect(selected.value).toEqual([]);
  });

  it("defaults to the critical-by-default fields when nothing is stored", async () => {
    const fields = ref<MissingTagField[]>(allFields);
    const selected = useMissingTagSelection(fields);
    await nextTick();

    expect(selected.value).toEqual(["author", "year"]);
  });

  it("restores a previously stored selection intersected with available keys", async () => {
    localStorage.setItem(storageKey, JSON.stringify(["series", "year"]));
    const fields = ref<MissingTagField[]>(allFields);

    const selected = useMissingTagSelection(fields);
    await nextTick();

    expect(selected.value).toEqual(["series", "year"]);
  });

  it("drops stored keys that are no longer available", async () => {
    localStorage.setItem(
      storageKey,
      JSON.stringify(["series", "obsolete-key"]),
    );
    const fields = ref<MissingTagField[]>(allFields);

    const selected = useMissingTagSelection(fields);
    await nextTick();

    expect(selected.value).toEqual(["series"]);
  });

  it("falls back to critical-by-default fields when the stored selection has no valid keys left", async () => {
    localStorage.setItem(storageKey, JSON.stringify(["obsolete-key"]));
    const fields = ref<MissingTagField[]>(allFields);

    const selected = useMissingTagSelection(fields);
    await nextTick();

    expect(selected.value).toEqual(["author", "year"]);
  });

  it("falls back to defaults when stored JSON is corrupt", async () => {
    localStorage.setItem(storageKey, "{not valid json");
    const fields = ref<MissingTagField[]>(allFields);

    const selected = useMissingTagSelection(fields);
    await nextTick();

    expect(selected.value).toEqual(["author", "year"]);
  });

  it.each([
    ["an object", "{}"],
    ["a number", "3"],
    ["a string", '"author"'],
    ["null", "null"],
  ])(
    "falls back to defaults when stored JSON is valid but not an array (%s)",
    async (_label, stored) => {
      localStorage.setItem(storageKey, stored);
      const fields = ref<MissingTagField[]>(allFields);

      const selected = useMissingTagSelection(fields);
      await nextTick();

      expect(selected.value).toEqual(["author", "year"]);
    },
  );

  it("persists selection changes to localStorage", async () => {
    const fields = ref<MissingTagField[]>(allFields);
    const selected = useMissingTagSelection(fields);
    await nextTick();

    selected.value = ["series"];
    await nextTick();

    expect(JSON.parse(localStorage.getItem(storageKey) ?? "[]")).toEqual([
      "series",
    ]);
  });

  it("recomputes the selection when the fields set changes", async () => {
    const fields = ref<MissingTagField[]>(allFields);
    const selected = useMissingTagSelection(fields);
    await nextTick();
    expect(selected.value).toEqual(["author", "year"]);

    fields.value = [
      { key: "publisher", label: "Publisher", isCriticalByDefault: true },
    ];
    await nextTick();

    expect(selected.value).toEqual(["publisher"]);
  });

  it("does not throw when localStorage access fails", async () => {
    const originalGetItem = Storage.prototype.getItem;
    Storage.prototype.getItem = () => {
      throw new Error("blocked");
    };

    try {
      const fields = ref<MissingTagField[]>(allFields);
      const selected = useMissingTagSelection(fields);
      await nextTick();

      expect(selected.value).toEqual(["author", "year"]);
    } finally {
      Storage.prototype.getItem = originalGetItem;
    }
  });

  it("does not throw when writing to localStorage fails", async () => {
    const originalSetItem = Storage.prototype.setItem;
    Storage.prototype.setItem = () => {
      throw new Error("quota exceeded");
    };

    try {
      const fields = ref<MissingTagField[]>(allFields);
      const selected = useMissingTagSelection(fields);
      await nextTick();

      expect(() => {
        selected.value = ["series"];
      }).not.toThrow();
      await nextTick();
    } finally {
      Storage.prototype.setItem = originalSetItem;
    }
  });
});

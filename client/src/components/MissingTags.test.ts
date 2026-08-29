import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { mount } from "@vue/test-utils";
import { createVuetify } from "vuetify";
import * as components from "vuetify/components";
import * as directives from "vuetify/directives";
import { nextTick } from "vue";
import MissingTags from "./MissingTags.vue";
import MissingTagService from "../services/MissingTagService";
import { AudiobookMissingTags } from "../types/MissingTag";

const vuetify = createVuetify({ components, directives });

vi.mock("../services/MissingTagService", () => ({
  default: {
    getFields: vi.fn(),
    getAudiobooksMissingTags: vi.fn(),
  },
}));

const mockedGetFields = vi.mocked(MissingTagService.getFields);
const mockedGetAudiobooks = vi.mocked(
  MissingTagService.getAudiobooksMissingTags,
);

const book = (id: number, bookName: string): AudiobookMissingTags => ({
  audiobookId: id,
  bookName,
  authors: ["Author"],
  missingFields: ["Year"],
});

const mountComponent = () =>
  mount(MissingTags, { global: { plugins: [vuetify] } });

beforeEach(() => {
  vi.useFakeTimers();
  localStorage.clear();
  mockedGetFields.mockReset();
  mockedGetAudiobooks.mockReset();
  mockedGetFields.mockResolvedValue([
    { key: "Year", label: "Year", isCriticalByDefault: true },
    { key: "Series", label: "Series", isCriticalByDefault: false },
  ]);
  mockedGetAudiobooks.mockResolvedValue([]);
});

afterEach(() => {
  vi.useRealTimers();
});

// Lets the mocked service promises settle while fake timers are installed.
const flush = async () => {
  for (let i = 0; i < 10; i++) {
    await Promise.resolve();
    await nextTick();
  }
};

describe("MissingTags.vue", () => {
  // Regression: the loader had no request-sequence guard, so a slow earlier scan landing after
  // a fast later one overwrote the newer results with rows for a field selection the user had
  // already changed.
  it("_IgnoresAStaleResponseThatLandsAfterANewerOne", async () => {
    let resolveFirst!: (value: AudiobookMissingTags[]) => void;
    mockedGetAudiobooks
      .mockImplementationOnce(
        () =>
          new Promise<AudiobookMissingTags[]>((resolve) => {
            resolveFirst = resolve;
          }),
      )
      .mockResolvedValueOnce([book(2, "Newer result")]);

    const wrapper = mountComponent();
    await flush();

    // First (slow) scan for the restored default selection.
    vi.advanceTimersByTime(500);
    await flush();

    // The user changes the selection; the second (fast) scan resolves immediately.
    const component = wrapper.vm as any;
    component.selectedFields = ["Series"];
    await flush();
    vi.advanceTimersByTime(500);
    await flush();

    expect(
      component.results.map((r: AudiobookMissingTags) => r.bookName),
    ).toEqual(["Newer result"]);

    // The superseded request finally answers - it must not overwrite the newer rows.
    resolveFirst([book(1, "Stale result")]);
    await flush();

    expect(
      component.results.map((r: AudiobookMissingTags) => r.bookName),
    ).toEqual(["Newer result"]);
    expect(component.loading).toBe(false);

    wrapper.unmount();
  });

  // Regression: the debounced loader had no matching onUnmounted cancel, so it fired after the
  // component was gone - mutating dead refs and issuing a request nobody reads.
  it("_DoesNotRunTheDebouncedScanAfterUnmount", async () => {
    const wrapper = mountComponent();
    await flush();

    const component = wrapper.vm as any;
    component.selectedFields = ["Series"];
    await flush();

    const callsBeforeUnmount = mockedGetAudiobooks.mock.calls.length;
    wrapper.unmount();

    vi.advanceTimersByTime(1000);
    await flush();

    expect(mockedGetAudiobooks.mock.calls.length).toBe(callsBeforeUnmount);
  });
});

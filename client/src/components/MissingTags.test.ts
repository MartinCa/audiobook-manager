import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { mount } from "@vue/test-utils";
import { createVuetify } from "vuetify";
import * as components from "vuetify/components";
import * as directives from "vuetify/directives";
import { nextTick } from "vue";
import MissingTags from "./MissingTags.vue";
import MissingTagService from "../services/MissingTagService";
import OperationsService from "../services/OperationsService";
import { AudiobookMissingTags } from "../types/MissingTag";

const vuetify = createVuetify({ components, directives });

vi.mock("../services/MissingTagService", () => ({
  default: {
    getFields: vi.fn(),
    getAudiobooksMissingTags: vi.fn(),
    startLanguageBackfill: vi.fn(),
  },
}));

vi.mock("../services/OperationsService", () => ({
  default: {
    getStatus: vi.fn(),
  },
}));

const mockedGetFields = vi.mocked(MissingTagService.getFields);
const mockedGetAudiobooks = vi.mocked(
  MissingTagService.getAudiobooksMissingTags,
);
const mockedStartBackfill = vi.mocked(MissingTagService.startLanguageBackfill);
const mockedGetStatus = vi.mocked(OperationsService.getStatus);

const idle = { isRunning: false, processed: 0, total: 0 };

const book = (id: number, bookName: string): AudiobookMissingTags => ({
  audiobookId: id,
  bookName,
  authors: ["Author"],
  missingFields: ["Year"],
});

const mountComponent = () =>
  mount(MissingTags, {
    global: { plugins: [vuetify], stubs: { RouterLink: true } },
  });

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
  mockedStartBackfill.mockReset();
  mockedStartBackfill.mockResolvedValue(undefined);
  mockedGetStatus.mockReset();
  mockedGetStatus.mockResolvedValue(idle);
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

  it("polls the operation status while the language backfill runs and refreshes when it ends", async () => {
    mockedGetStatus
      .mockResolvedValueOnce(idle) // resumeRunningBackfill on mount
      .mockResolvedValueOnce({ isRunning: true, processed: 4, total: 10 })
      .mockResolvedValueOnce({ isRunning: false, processed: 10, total: 10 });

    const wrapper = mountComponent();
    await flush();
    // Let the initial debounced scan run, so it is not counted against the backfill below.
    vi.advanceTimersByTime(500);
    await flush();
    const component = wrapper.vm as any;

    const scansBeforeBackfill = mockedGetAudiobooks.mock.calls.length;
    await component.startBackfill();
    await flush();

    expect(mockedStartBackfill).toHaveBeenCalledTimes(1);
    expect(component.backfillRunning).toBe(true);

    vi.advanceTimersByTime(1000);
    await flush();
    expect(component.backfillProcessed).toBe(4);
    expect(component.backfillTotal).toBe(10);
    expect(component.backfillRunning).toBe(true);

    vi.advanceTimersByTime(1000);
    await flush();
    expect(component.backfillRunning).toBe(false);
    // The finished pass fills in languages, so the list of books still missing one has to be
    // re-read rather than left showing the pre-backfill rows.
    expect(mockedGetAudiobooks.mock.calls.length).toBe(scansBeforeBackfill + 1);

    // Polling must stop once the run is over, not keep issuing a request every second.
    const pollsAfterFinish = mockedGetStatus.mock.calls.length;
    vi.advanceTimersByTime(5000);
    await flush();
    expect(mockedGetStatus.mock.calls.length).toBe(pollsAfterFinish);

    wrapper.unmount();
  });

  it("stops polling the backfill when the component unmounts", async () => {
    mockedGetStatus
      .mockResolvedValueOnce(idle)
      .mockResolvedValue({ isRunning: true, processed: 1, total: 10 });

    const wrapper = mountComponent();
    await flush();

    await (wrapper.vm as any).startBackfill();
    await flush();

    const pollsBeforeUnmount = mockedGetStatus.mock.calls.length;
    wrapper.unmount();

    vi.advanceTimersByTime(5000);
    await flush();

    // Same pairing rule as the debounced scan: a live interval outliving the component mutates
    // dead refs and issues requests nobody reads.
    expect(mockedGetStatus.mock.calls.length).toBe(pollsBeforeUnmount);
  });

  it("shows a backfill already in flight instead of offering to start a second", async () => {
    mockedGetStatus.mockResolvedValue({
      isRunning: true,
      processed: 30,
      total: 90,
    });

    const wrapper = mountComponent();
    await flush();
    const component = wrapper.vm as any;

    // The pass outlives this page, so a reload has to pick it back up.
    expect(component.backfillRunning).toBe(true);
    expect(component.backfillProcessed).toBe(30);
    expect(component.backfillTotal).toBe(90);
    expect(mockedStartBackfill).not.toHaveBeenCalled();

    wrapper.unmount();
  });

  it("stops showing the backfill as running when it fails to start", async () => {
    mockedStartBackfill.mockRejectedValueOnce(new Error("boom"));

    const wrapper = mountComponent();
    await flush();
    const component = wrapper.vm as any;

    await component.startBackfill();
    await flush();

    expect(component.backfillRunning).toBe(false);
    expect(component.snackbar).toBe(true);

    // No poll may be left running for a job that never started.
    const pollsAfterFailure = mockedGetStatus.mock.calls.length;
    vi.advanceTimersByTime(5000);
    await flush();
    expect(mockedGetStatus.mock.calls.length).toBe(pollsAfterFailure);

    wrapper.unmount();
  });
});

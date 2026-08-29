import { describe, it, expect, vi, beforeEach } from "vitest";
import { mount } from "@vue/test-utils";
import { createVuetify } from "vuetify";
import * as components from "vuetify/components";
import * as directives from "vuetify/directives";
import DiscoveredAudiobooks from "./DiscoveredAudiobooks.vue";
import LibraryService from "../../services/LibraryService";
import DiscoveredAudiobook from "../../types/DiscoveredAudiobook";

const vuetify = createVuetify({ components, directives });

vi.mock("../../services/LibraryService", () => ({
  default: {
    getDiscoveredBooks: vi.fn(),
    startLibraryScan: vi.fn(),
    bulkImportDiscovered: vi.fn(),
    deleteDiscoveredBook: vi.fn(),
  },
}));

const fakeSignalR = {
  on: vi.fn(),
  off: vi.fn(),
  onReconnected: vi.fn(),
  offReconnected: vi.fn(),
};

vi.mock("@/signalr/hub", async () => {
  const vue = await import("vue");
  return {
    useSignalR: () => fakeSignalR,
    useSignalREvent: (token: string, callback: (...args: any[]) => void) => {
      vue.onMounted(() => fakeSignalR.on(token, callback));
      vue.onUnmounted(() => fakeSignalR.off(token, callback));
    },
    useSignalRReconnected: (callback: () => void) => {
      vue.onMounted(() => fakeSignalR.onReconnected(callback));
      vue.onUnmounted(() => fakeSignalR.offReconnected(callback));
    },
  };
});

vi.mock("../../services/OperationsService", () => ({
  default: {
    getStatus: vi
      .fn()
      .mockResolvedValue({ isRunning: false, processed: 0, total: 0 }),
  },
}));

const mockedGetDiscoveredBooks = vi.mocked(LibraryService.getDiscoveredBooks);

function makeBook(path: string): DiscoveredAudiobook {
  return {
    id: path.length,
    bookName: path,
    fullPath: path,
    fileName: path.split("/").pop() ?? path,
    sizeInBytes: 1000,
    discoveredAt: "2026-01-01T00:00:00Z",
    isWellTagged: true,
    isDuplicate: false,
  } as DiscoveredAudiobook;
}

function flushPromises() {
  return new Promise((resolve) => setTimeout(resolve, 0));
}

function mountComponent() {
  return mount(DiscoveredAudiobooks, {
    global: {
      plugins: [vuetify],
      stubs: { BookOrganize: true, OperationProgressBar: true },
    },
  });
}

beforeEach(() => {
  vi.clearAllMocks();
  mockedGetDiscoveredBooks.mockResolvedValue({
    count: 3,
    total: 3,
    items: [
      makeBook("/library/a.m4b"),
      makeBook("/library/b.m4b"),
      makeBook("/library/c.m4b"),
    ],
  });
});

describe("DiscoveredAudiobooks open-panel tracking", () => {
  it("keeps the open panel on the same book when an earlier row is removed", async () => {
    // Regression: the open panel was tracked by array index, so removing a row above it
    // silently re-pointed it at whichever book shifted into that slot - with Vue reusing the
    // already-open BookOrganize form for a different file.
    const wrapper = mountComponent();
    await flushPromises();

    const vm = wrapper.vm as any;
    vm.discoveredActivePanel = "/library/c.m4b";
    await flushPromises();

    // The first book is queued, then finishes organizing and drops out of the list.
    const firstBook = vm.discoveredBooks[0];
    vm.markDiscoveredAsQueued(firstBook, firstBook.fullPath);
    await flushPromises();

    vm.onUpdateProgress({
      originalFileLocation: firstBook.fullPath,
      progressMessage: "Done",
      progress: 100,
    });
    await flushPromises();

    // The panel value must still resolve to the same book. An index-based value would now
    // resolve to a different row (or none) because everything after the removal shifted down.
    expect(vm.discoveredActivePanel).toBe("/library/c.m4b");
    expect(
      vm.discoveredBooks.find(
        (b: any) => b.fullPath === vm.discoveredActivePanel,
      )?.fullPath,
    ).toBe("/library/c.m4b");
    expect(vm.discoveredBooks.map((b: any) => b.fullPath)).toEqual([
      "/library/b.m4b",
      "/library/c.m4b",
    ]);

    wrapper.unmount();
  });

  it("closes the panel when the book it belongs to is removed", async () => {
    const wrapper = mountComponent();
    await flushPromises();

    const vm = wrapper.vm as any;
    vm.discoveredActivePanel = "/library/b.m4b";
    await flushPromises();

    vm.removeDiscoveredBook(vm.discoveredBooks[1]);
    await flushPromises();

    expect(vm.discoveredActivePanel).toBeNull();
    expect(vm.discoveredBooks.map((b: any) => b.fullPath)).toEqual([
      "/library/a.m4b",
      "/library/c.m4b",
    ]);

    wrapper.unmount();
  });
});

describe("DiscoveredAudiobooks stale-response handling", () => {
  it("ignores a slow list response that resolves after a newer one", async () => {
    const wrapper = mountComponent();
    await flushPromises();

    let resolveSlow: (value: any) => void = () => {};
    const slow = new Promise<any>((resolve) => {
      resolveSlow = resolve;
    });

    mockedGetDiscoveredBooks.mockReturnValueOnce(slow as any);
    mockedGetDiscoveredBooks.mockResolvedValueOnce({
      count: 1,
      total: 1,
      items: [makeBook("/library/newer.m4b")],
    });

    const vm = wrapper.vm as any;
    const stalePromise = vm.loadDiscoveredBooks();
    const newerPromise = vm.loadDiscoveredBooks();
    await newerPromise;

    resolveSlow({
      count: 1,
      total: 1,
      items: [makeBook("/library/stale.m4b")],
    });
    await stalePromise;
    await flushPromises();

    expect(vm.discoveredBooks.map((b: any) => b.fullPath)).toEqual([
      "/library/newer.m4b",
    ]);

    wrapper.unmount();
  });
});

describe("DiscoveredAudiobooks list loading", () => {
  // Regression test: loadDiscoveredBooks had no try/catch, unlike its sibling loadIssues in
  // LibraryConsistency.vue. It is called from SignalR completion callbacks and the reconnect
  // handler, so a rejection escaped into a hub callback with nothing to catch it, and the stale
  // list gave the user no sign anything had failed. Fails against the pre-fix component with an
  // unhandled rejection and no snackbar.
  it("surfaces a load failure instead of rejecting, and keeps the list it already had", async () => {
    const wrapper = mountComponent();
    await flushPromises();

    const vm = wrapper.vm as any;
    expect(vm.discoveredBooks.length).toBe(3);

    mockedGetDiscoveredBooks.mockRejectedValueOnce(new Error("network down"));
    await expect(vm.loadDiscoveredBooks()).resolves.toBeUndefined();
    await flushPromises();

    expect(vm.discoveredBooks.length).toBe(3);
    expect(vm.snackbar).toBe(true);
    expect(vm.snackbarText).toBe("Failed to refresh the discovered books list");

    wrapper.unmount();
  });

  // Regression test: the debounced search reset the page *and* called the loader, so from any
  // page but the first it issued two overlapping requests per search. Fails against the pre-fix
  // component, which makes two calls here.
  it("issues a single request when searching from a page other than the first", async () => {
    vi.useFakeTimers();
    try {
      const wrapper = mountComponent();
      await vi.advanceTimersByTimeAsync(50);

      const vm = wrapper.vm as any;
      vm.discoveredCurrentPage = 2;
      await vi.advanceTimersByTimeAsync(50);

      const callsBeforeSearch = mockedGetDiscoveredBooks.mock.calls.length;

      vm.discoveredSearchQuery = "harry";
      await vi.advanceTimersByTimeAsync(500);

      expect(mockedGetDiscoveredBooks.mock.calls.length).toBe(
        callsBeforeSearch + 1,
      );
      // ...and it asked for the first page of the new query.
      const lastCall = mockedGetDiscoveredBooks.mock.calls.at(-1)!;
      expect(lastCall[1]).toBe(0);
      expect(lastCall[2]).toBe("harry");

      wrapper.unmount();
    } finally {
      vi.useRealTimers();
    }
  });

  it("still issues one request when searching from the first page", async () => {
    vi.useFakeTimers();
    try {
      const wrapper = mountComponent();
      await vi.advanceTimersByTimeAsync(50);

      const vm = wrapper.vm as any;
      const callsBeforeSearch = mockedGetDiscoveredBooks.mock.calls.length;

      vm.discoveredSearchQuery = "harry";
      await vi.advanceTimersByTimeAsync(500);

      expect(mockedGetDiscoveredBooks.mock.calls.length).toBe(
        callsBeforeSearch + 1,
      );

      wrapper.unmount();
    } finally {
      vi.useRealTimers();
    }
  });
});

describe("DiscoveredAudiobooks selection", () => {
  // The clone-on-every-mutation was removed because a ref'd Set is already reactive. These
  // guard that the dependent computeds still update from in-place mutation.
  it("updates the select-all state when individual books are ticked in place", async () => {
    const wrapper = mountComponent();
    await flushPromises();

    const vm = wrapper.vm as any;
    expect(vm.isAllWellTaggedSelected).toBe(false);
    expect(vm.isSomeWellTaggedSelected).toBe(false);

    vm.toggleBookSelected(vm.discoveredBooks[0]);
    await flushPromises();

    expect(vm.selectedPaths.has("/library/a.m4b")).toBe(true);
    expect(vm.isSomeWellTaggedSelected).toBe(true);
    expect(vm.isAllWellTaggedSelected).toBe(false);

    vm.toggleSelectAllWellTagged();
    await flushPromises();

    expect(vm.isAllWellTaggedSelected).toBe(true);
    expect(vm.selectedPaths.size).toBe(3);

    vm.toggleSelectAllWellTagged();
    await flushPromises();

    expect(vm.isAllWellTaggedSelected).toBe(false);
    expect(vm.selectedPaths.size).toBe(0);

    wrapper.unmount();
  });

  it("deselects a book that leaves the list after finishing its organize", async () => {
    const wrapper = mountComponent();
    await flushPromises();

    const vm = wrapper.vm as any;
    const book = vm.discoveredBooks[0];
    vm.toggleBookSelected(book);
    await flushPromises();
    expect(vm.selectedPaths.size).toBe(1);

    vm.markDiscoveredAsQueued(book, book.fullPath);
    await flushPromises();

    expect(vm.selectedPaths.size).toBe(0);
    expect(vm.isSomeWellTaggedSelected).toBe(false);

    wrapper.unmount();
  });
});

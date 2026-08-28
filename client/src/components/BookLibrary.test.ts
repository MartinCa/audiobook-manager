import { describe, it, expect, vi, beforeEach } from "vitest";
import { mount } from "@vue/test-utils";
import { createVuetify } from "vuetify";
import * as components from "vuetify/components";
import * as directives from "vuetify/directives";
import BookLibrary from "./BookLibrary.vue";
import BrowseService from "../services/BrowseService";
import ConsistencyService from "../services/ConsistencyService";

const vuetify = createVuetify({ components, directives });

vi.mock("../services/BrowseService", () => ({
  default: {
    getBooks: vi.fn(),
    searchBooks: vi.fn(),
  },
}));

vi.mock("../services/ConsistencyService", () => ({
  default: {
    getIssueSummary: vi.fn(),
  },
}));

const mockedGetBooks = vi.mocked(BrowseService.getBooks);
const mockedSearchBooks = vi.mocked(BrowseService.searchBooks);
const mockedGetIssueSummary = vi.mocked(ConsistencyService.getIssueSummary);

function flushPromises() {
  return new Promise((resolve) => setTimeout(resolve, 0));
}

function mountLibrary() {
  return mount(BookLibrary, {
    global: { plugins: [vuetify] },
  });
}

beforeEach(() => {
  vi.clearAllMocks();
  mockedGetBooks.mockResolvedValue({ count: 0, total: 0, items: [] });
  mockedSearchBooks.mockResolvedValue({ count: 0, total: 0, items: [] });
  mockedGetIssueSummary.mockResolvedValue({});
});

describe("BookLibrary issue summary refresh", () => {
  it("loads the issue summary once on mount", async () => {
    const wrapper = mountLibrary();
    await flushPromises();

    expect(mockedGetIssueSummary).toHaveBeenCalledTimes(1);

    wrapper.unmount();
  });

  it("refreshes the issue summary when the page changes", async () => {
    mockedGetBooks.mockResolvedValue({
      count: 1,
      total: 200,
      items: [
        {
          id: 1,
          bookName: "Book",
          year: 2020,
          authors: [],
          narrators: [],
          genres: [],
        },
      ],
    });

    const wrapper = mountLibrary();
    await flushPromises();
    expect(mockedGetIssueSummary).toHaveBeenCalledTimes(1);

    const vm = wrapper.vm as any;
    vm.currentPage = 2;
    await flushPromises();

    expect(mockedGetIssueSummary).toHaveBeenCalledTimes(2);

    wrapper.unmount();
  });

  it("refreshes the issue summary when the search query changes", async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    try {
      const wrapper = mountLibrary();
      await flushPromises();
      expect(mockedGetIssueSummary).toHaveBeenCalledTimes(1);

      const vm = wrapper.vm as any;
      vm.searchQuery = "harry potter";
      await vi.advanceTimersByTimeAsync(350);
      await flushPromises();

      expect(mockedGetIssueSummary).toHaveBeenCalledTimes(2);

      wrapper.unmount();
    } finally {
      vi.useRealTimers();
    }
  });
});

function makeBook(id: number, bookName: string) {
  return {
    id,
    bookName,
    year: 2020,
    authors: [],
    narrators: [],
    genres: [],
  };
}

describe("BookLibrary stale-response handling", () => {
  it("ignores a slow search response that resolves after a newer one", async () => {
    // Regression: loadBooks had no request-sequence guard, so an older in-flight response
    // landing last overwrote the newer results the user is actually looking at.
    vi.useFakeTimers({ shouldAdvanceTime: true });
    try {
      const wrapper = mountLibrary();
      await flushPromises();

      let resolveSlow: (value: any) => void = () => {};
      const slow = new Promise<any>((resolve) => {
        resolveSlow = resolve;
      });

      mockedSearchBooks.mockReturnValueOnce(slow as any);
      mockedSearchBooks.mockResolvedValueOnce({
        count: 1,
        total: 1,
        items: [makeBook(2, "Newer Result")],
      });

      const vm = wrapper.vm as any;

      vm.searchQuery = "har";
      await vi.advanceTimersByTimeAsync(350);
      await flushPromises();

      vm.searchQuery = "harry";
      await vi.advanceTimersByTimeAsync(350);
      await flushPromises();

      // The first (stale) request only now comes back.
      resolveSlow({ count: 1, total: 1, items: [makeBook(1, "Stale Result")] });
      await flushPromises();

      expect(vm.books.map((b: any) => b.bookName)).toEqual(["Newer Result"]);

      wrapper.unmount();
    } finally {
      vi.useRealTimers();
    }
  });

  it("loads the page only once when a search resets the page from a later page", async () => {
    // Regression: the debounced search called loadBooks() itself *and* reset currentPage, which
    // tripped the page watcher into a second identical load - two page fetches and two issue
    // summaries per search.
    vi.useFakeTimers({ shouldAdvanceTime: true });
    try {
      mockedGetBooks.mockResolvedValue({
        count: 1,
        total: 200,
        items: [makeBook(1, "Book")],
      });

      const wrapper = mountLibrary();
      await flushPromises();

      const vm = wrapper.vm as any;
      vm.currentPage = 3;
      await flushPromises();

      mockedGetBooks.mockClear();
      mockedSearchBooks.mockClear();
      mockedGetIssueSummary.mockClear();

      vm.searchQuery = "dune";
      await vi.advanceTimersByTimeAsync(350);
      await flushPromises();

      expect(vm.currentPage).toBe(1);
      expect(mockedSearchBooks).toHaveBeenCalledTimes(1);
      expect(mockedGetIssueSummary).toHaveBeenCalledTimes(1);

      wrapper.unmount();
    } finally {
      vi.useRealTimers();
    }
  });

  it("cancels a pending debounced search on unmount", async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    try {
      const wrapper = mountLibrary();
      await flushPromises();

      mockedSearchBooks.mockClear();

      const vm = wrapper.vm as any;
      vm.searchQuery = "dune";
      await flushPromises();

      // Unmount before the debounce window elapses.
      wrapper.unmount();
      await vi.advanceTimersByTimeAsync(500);
      await flushPromises();

      expect(mockedSearchBooks).not.toHaveBeenCalled();
    } finally {
      vi.useRealTimers();
    }
  });
});

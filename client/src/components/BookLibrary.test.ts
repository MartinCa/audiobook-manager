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

import { describe, it, expect, vi, beforeEach } from "vitest";
import { mount } from "@vue/test-utils";
import { createVuetify } from "vuetify";
import * as components from "vuetify/components";
import * as directives from "vuetify/directives";
import { reactive } from "vue";
import BookDetail from "./BookDetail.vue";
import BrowseService from "../../services/BrowseService";
import ConsistencyService from "../../services/ConsistencyService";
import AudiobookService from "../../services/AudiobookService";
import AudiobookDetail from "../../types/AudiobookDetail";

const vuetify = createVuetify({ components, directives });

const route = reactive({ params: { bookId: "1" } });

vi.mock("vue-router", () => ({
  useRoute: () => route,
}));

vi.mock("../../services/BrowseService", () => ({
  default: { getBookDetail: vi.fn() },
}));

vi.mock("../../services/ConsistencyService", () => ({
  default: {
    getIssuesByAudiobook: vi.fn(),
    resolveIssue: vi.fn(),
  },
}));

vi.mock("../../services/AudiobookService", () => ({
  default: {
    generateNewPath: vi.fn(),
    updateBook: vi.fn(),
  },
}));

const mockedGetBookDetail = vi.mocked(BrowseService.getBookDetail);
const mockedGetIssues = vi.mocked(ConsistencyService.getIssuesByAudiobook);
const mockedGenerateNewPath = vi.mocked(AudiobookService.generateNewPath);

function makeBook(id: number, bookName: string): AudiobookDetail {
  return {
    id,
    bookName,
    year: 2020,
    authors: ["Author"],
    narrators: [],
    genres: [],
    filePath: `/library/book-${id}.m4b`,
    fileName: `book-${id}.m4b`,
    sizeInBytes: 1000,
  };
}

function mountDetail() {
  return mount(BookDetail, {
    global: {
      plugins: [vuetify],
      stubs: {
        BookEditForm: true,
        DiffDisplay: true,
      },
    },
  });
}

function flushPromises() {
  return new Promise((resolve) => setTimeout(resolve, 0));
}

beforeEach(() => {
  vi.clearAllMocks();
  route.params.bookId = "1";
  mockedGetIssues.mockResolvedValue([]);
  mockedGenerateNewPath.mockResolvedValue("generated/path.m4b");
});

describe("BookDetail route param reactivity", () => {
  it("reloads book detail and issues when navigating to a different bookId while the component instance persists", async () => {
    mockedGetBookDetail
      .mockResolvedValueOnce(makeBook(1, "First Book"))
      .mockResolvedValueOnce(makeBook(2, "Second Book"));

    const wrapper = mountDetail();
    await flushPromises();

    expect(mockedGetBookDetail).toHaveBeenCalledTimes(1);
    expect(mockedGetBookDetail).toHaveBeenCalledWith(1);
    expect(wrapper.text()).toContain("First Book");

    // Simulate Vue Router reusing this same component instance for a navigation
    // to another route matching the same record (e.g. /library/book/1 -> /library/book/2).
    route.params.bookId = "2";
    await flushPromises();
    await flushPromises();

    expect(mockedGetBookDetail).toHaveBeenCalledTimes(2);
    expect(mockedGetBookDetail).toHaveBeenCalledWith(2);
    expect(mockedGetIssues).toHaveBeenCalledTimes(2);
    expect(mockedGetIssues).toHaveBeenCalledWith(2);
    expect(wrapper.text()).toContain("Second Book");
    expect(wrapper.text()).not.toContain("First Book");

    wrapper.unmount();
  });
});

describe("BookDetail path regeneration debounce", () => {
  it("does not call generateNewPath again when only cover fields change", async () => {
    mockedGetBookDetail.mockResolvedValue(makeBook(1, "First Book"));

    const wrapper = mountDetail();
    await flushPromises();
    await new Promise((resolve) => setTimeout(resolve, 350));
    const callsAfterInitialLoad = mockedGenerateNewPath.mock.calls.length;

    const vm = wrapper.vm as any;
    vm.input.cover_base64 = "a-different-cover-payload";
    vm.input.cover_mime = "image/png";
    await flushPromises();
    await new Promise((resolve) => setTimeout(resolve, 350));

    expect(mockedGenerateNewPath).toHaveBeenCalledTimes(callsAfterInitialLoad);

    wrapper.unmount();
  });

  it("still calls generateNewPath when a non-cover field changes", async () => {
    mockedGetBookDetail.mockResolvedValue(makeBook(1, "First Book"));

    const wrapper = mountDetail();
    await flushPromises();
    await new Promise((resolve) => setTimeout(resolve, 350));
    const callsAfterInitialLoad = mockedGenerateNewPath.mock.calls.length;

    const vm = wrapper.vm as any;
    vm.input.bookName = "A Different Book Name";
    await flushPromises();
    await new Promise((resolve) => setTimeout(resolve, 350));

    expect(mockedGenerateNewPath.mock.calls.length).toBeGreaterThan(
      callsAfterInitialLoad,
    );

    wrapper.unmount();
  });
});

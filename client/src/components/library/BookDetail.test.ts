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
    recheckAudiobook: vi.fn(),
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
const mockedRecheckAudiobook = vi.mocked(ConsistencyService.recheckAudiobook);

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
  mockedRecheckAudiobook.mockResolvedValue([]);
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

describe("BookDetail check consistency action", () => {
  it("calls recheckAudiobook and reloads issues via getIssuesByAudiobook on click", async () => {
    mockedGetBookDetail.mockResolvedValue(makeBook(1, "First Book"));

    const wrapper = mountDetail();
    await flushPromises();

    const callsBeforeClick = mockedGetIssues.mock.calls.length;

    const button = wrapper
      .findAll("button")
      .find((b) => b.text().includes("Check Consistency"));
    expect(button).toBeTruthy();
    await button!.trigger("click");
    await flushPromises();

    expect(mockedRecheckAudiobook).toHaveBeenCalledWith(1);
    expect(mockedGetIssues.mock.calls.length).toBe(callsBeforeClick + 1);
    expect(mockedGetIssues).toHaveBeenLastCalledWith(1);
    expect((wrapper.vm as any).snackbarText).toBe("Consistency check complete");

    wrapper.unmount();
  });

  it("shows an error and leaves prior issues intact when recheckAudiobook fails", async () => {
    mockedGetBookDetail.mockResolvedValue(makeBook(1, "First Book"));
    mockedGetIssues.mockResolvedValueOnce([
      {
        id: 1,
        audiobookId: 1,
        bookName: "First Book",
        authors: ["Author"],
        issueType: "MissingCoverFile",
        description: "Cover file missing",
        expectedValue: undefined,
        actualValue: undefined,
        detectedAt: "2024-01-01T00:00:00Z",
      },
    ]);
    mockedRecheckAudiobook.mockRejectedValue(new Error("boom"));

    const wrapper = mountDetail();
    await flushPromises();

    expect(wrapper.text()).toContain("Issues (1)");

    const button = wrapper
      .findAll("button")
      .find((b) => b.text().includes("Check Consistency"));
    await button!.trigger("click");
    await flushPromises();

    expect((wrapper.vm as any).snackbarText).toBe(
      "Failed to check consistency",
    );
    expect(wrapper.text()).toContain("Issues (1)");

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

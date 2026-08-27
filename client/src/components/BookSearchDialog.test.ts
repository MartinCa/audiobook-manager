import { describe, it, expect, vi, beforeEach } from "vitest";
import { mount } from "@vue/test-utils";
import { createVuetify } from "vuetify";
import * as components from "vuetify/components";
import * as directives from "vuetify/directives";
import { nextTick } from "vue";
import BookSearchDialog from "./BookSearchDialog.vue";
import MetadataSearchService from "../services/MetadataSearchService";
import { Audiobook } from "../types/Audiobook";
import { MetadataSearchServiceInfo } from "../types/MetadataSearchServiceInfo";

vi.mock("../services/MetadataSearchService", () => ({
  default: {
    getServices: vi.fn(),
    searchMultiple: vi.fn(),
    getBookDetails: vi.fn(),
    searchSource: vi.fn(),
  },
}));

const vuetify = createVuetify({ components, directives });

const emptyBookDetails: Audiobook = {
  authors: [],
  narrators: [],
  genres: [],
};

// Deliberately unusual/distinctive names so a test failure that falls back to a
// hardcoded source list in the component (rather than the fetched list) is obvious.
const fetchedServices: MetadataSearchServiceInfo[] = [
  { name: "ZorkSource", enabled: true },
  { name: "QuuxSource", enabled: true },
  { name: "DisabledSource", enabled: false, disabledReason: "No API key" },
];

function mountDialog() {
  return mount(BookSearchDialog, {
    global: { plugins: [vuetify] },
    props: { bookDetails: emptyBookDetails },
    attachTo: document.body,
  });
}

function flushPromises() {
  return new Promise((resolve) => setTimeout(resolve, 0));
}

beforeEach(() => {
  vi.clearAllMocks();
  localStorage.clear();
  (MetadataSearchService.getServices as any).mockResolvedValue(fetchedServices);
  (MetadataSearchService.searchMultiple as any).mockResolvedValue({
    results: [],
    sourceStatuses: [],
  });
  (MetadataSearchService.getBookDetails as any).mockResolvedValue({
    url: "https://example.com/book/123",
    source: "ZorkSource",
    authors: [],
    narrators: [],
    bookName: "Fetched Book",
    year: 2020,
    series: [],
    genres: [],
  });
});

describe("BookSearchDialog source list", () => {
  it("renders the source chips from the fetched services list, not a hardcoded array", async () => {
    const wrapper = mountDialog();
    await flushPromises();
    await nextTick();

    expect(MetadataSearchService.getServices).toHaveBeenCalledOnce();

    const chipTexts = wrapper
      .findAll(".v-chip-group .v-chip")
      .map((c) => c.text());
    expect(chipTexts).toContain("ZorkSource");
    expect(chipTexts).toContain("QuuxSource");
    expect(chipTexts).toContain("DisabledSource");

    wrapper.unmount();
  });

  it("only enabled fetched sources are selected by default for searching", async () => {
    const wrapper = mountDialog();
    await flushPromises();
    await nextTick();

    // Assert on the exact set of selected chips rather than a substring of the
    // rendered text: a `toContain("Searching: Zork...")` prefix match still
    // passes when a disabled source is appended to the selection.
    const selectedChipTexts = wrapper
      .findAll(".v-chip-group .v-chip--selected")
      .map((c) => c.text());
    expect(selectedChipTexts).toEqual(["ZorkSource", "QuuxSource"]);

    wrapper.unmount();
  });
});

describe("BookSearchDialog URL vs. search-term submission", () => {
  it("submitting an absolute http(s) URL calls getBookDetails() directly and skips multi-source search", async () => {
    const wrapper = mountDialog();
    await flushPromises();
    await nextTick();

    const searchField = wrapper.find('input[type="text"]');
    await searchField.setValue("https://example.com/book/123");
    await searchField.trigger("keyup.enter");
    await flushPromises();
    await nextTick();

    expect(MetadataSearchService.getBookDetails).toHaveBeenCalledWith(
      "https://example.com/book/123",
    );
    expect(MetadataSearchService.searchMultiple).not.toHaveBeenCalled();

    wrapper.unmount();
  });

  it("submitting a URL with a single series result emits resultChosen without showing the source picker step", async () => {
    const wrapper = mountDialog();
    await flushPromises();
    await nextTick();

    const searchField = wrapper.find('input[type="text"]');
    await searchField.setValue("https://example.com/book/123");
    await searchField.trigger("keyup.enter");
    await flushPromises();
    await nextTick();

    const emitted = wrapper.emitted("resultChosen");
    expect(emitted).toBeTruthy();
    expect((emitted![0][0] as any).bookName).toBe("Fetched Book");

    wrapper.unmount();
  });

  it("submitting a plain search term triggers multi-source search using the fetched (not hardcoded) source list", async () => {
    const wrapper = mountDialog();
    await flushPromises();
    await nextTick();

    const searchField = wrapper.find('input[type="text"]');
    await searchField.setValue("Some Book Title");
    await searchField.trigger("keyup.enter");
    await flushPromises();
    await nextTick();

    expect(MetadataSearchService.getBookDetails).not.toHaveBeenCalled();
    expect(MetadataSearchService.searchMultiple).toHaveBeenCalledWith(
      ["ZorkSource", "QuuxSource"],
      "Some Book Title",
    );

    wrapper.unmount();
  });

  it("treats a non-absolute value (no scheme) as a search term, not a URL", async () => {
    const wrapper = mountDialog();
    await flushPromises();
    await nextTick();

    const searchField = wrapper.find('input[type="text"]');
    await searchField.setValue("example.com/book/123");
    await searchField.trigger("keyup.enter");
    await flushPromises();
    await nextTick();

    expect(MetadataSearchService.getBookDetails).not.toHaveBeenCalled();
    expect(MetadataSearchService.searchMultiple).toHaveBeenCalledWith(
      ["ZorkSource", "QuuxSource"],
      "example.com/book/123",
    );

    wrapper.unmount();
  });
});

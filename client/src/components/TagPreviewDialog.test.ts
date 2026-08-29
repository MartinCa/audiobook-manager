import { describe, it, expect, vi, beforeEach } from "vitest";
import { mount } from "@vue/test-utils";
import { createVuetify } from "vuetify";
import * as components from "vuetify/components";
import * as directives from "vuetify/directives";
import TagPreviewDialog from "./TagPreviewDialog.vue";
import LanguageService from "../services/LanguageService";
import OrganizeAudiobookInput from "../types/OrganizeAudiobookInput";
import { MetadataSearchResult } from "../types/MetadataSearchResult";

vi.mock("../services/LanguageService", () => ({
  default: {
    getLanguageOptions: vi.fn(),
  },
}));

const vuetify = createVuetify({ components, directives });

const makeInput = (
  overrides: Partial<OrganizeAudiobookInput> = {},
): OrganizeAudiobookInput => ({
  authors: "An Author",
  narrators: "",
  bookName: "A Book",
  subtitle: "",
  series: "",
  seriesPart: "",
  year: 2020,
  genres: "",
  description: "",
  ...overrides,
});

const makeResult = (
  overrides: Partial<MetadataSearchResult> = {},
): MetadataSearchResult =>
  ({
    url: "https://example.com/book",
    bookName: "A Book",
    authors: [{ name: "An Author" }],
    narrators: [],
    series: [],
    genres: [],
    year: 2020,
    ...overrides,
  }) as MetadataSearchResult;

const mountDialog = (
  currentInput: OrganizeAudiobookInput,
  searchResult: MetadataSearchResult,
) =>
  mount(TagPreviewDialog, {
    global: { plugins: [vuetify] },
    props: { dialogWidth: "800", currentInput, searchResult },
  });

const languageField = (wrapper: ReturnType<typeof mountDialog>) =>
  (wrapper.vm as any).fields.find((f: any) => f.key === "language");

const flushPromises = () => new Promise((resolve) => setTimeout(resolve, 0));

beforeEach(() => {
  vi.clearAllMocks();
  (LanguageService.getLanguageOptions as any).mockResolvedValue({
    languages: [
      { code: "en", displayName: "English", aliases: ["en", "eng", "english"] },
      {
        code: "da",
        displayName: "Danish",
        aliases: ["da", "dan", "danish", "dansk"],
      },
    ],
    defaultCode: "en",
  });
});

describe("TagPreviewDialog language row", () => {
  // The book stores a code while a source reports a display name, so comparing them raw made
  // every scrape of an English book report "en" changing to "English".
  it("does not report a change when the source names the language already set", async () => {
    const wrapper = mountDialog(
      makeInput({ language: "en" }),
      makeResult({ language: "English" }),
    );
    await flushPromises();

    const field = languageField(wrapper);
    expect(field.changed).toBe(false);
    expect(field.currentValue).toBe("English");
    expect(field.newValue).toBe("English");

    wrapper.unmount();
  });

  it("reports a real change between two managed languages", async () => {
    const wrapper = mountDialog(
      makeInput({ language: "en" }),
      makeResult({ language: "Dansk" }),
    );
    await flushPromises();

    const field = languageField(wrapper);
    expect(field.changed).toBe(true);
    expect(field.currentValue).toBe("English");
    expect(field.newValue).toBe("Danish");

    wrapper.unmount();
  });

  // Applying such a result leaves the current selection alone, so showing it as a change would
  // offer an edit that never happens.
  it("reports no change when the source names a language the library does not manage", async () => {
    const wrapper = mountDialog(
      makeInput({ language: "en" }),
      makeResult({ language: "German" }),
    );
    await flushPromises();

    const field = languageField(wrapper);
    expect(field.changed).toBe(false);
    expect(field.newValue).toBe("English");

    wrapper.unmount();
  });

  it("reports a change when the book has no language and the source has one", async () => {
    const wrapper = mountDialog(
      makeInput(),
      makeResult({ language: "English" }),
    );
    await flushPromises();

    const field = languageField(wrapper);
    expect(field.changed).toBe(true);
    expect(field.currentValue).toBe("");
    expect(field.newValue).toBe("English");

    wrapper.unmount();
  });
});

import { describe, it, expect, vi, beforeEach } from "vitest";
import { mount } from "@vue/test-utils";
import { createVuetify } from "vuetify";
import * as components from "vuetify/components";
import * as directives from "vuetify/directives";
import { nextTick } from "vue";
import BookEditForm from "./BookEditForm.vue";
import SimilarValueService from "../services/SimilarValueService";
import LanguageService from "../services/LanguageService";
import { Audiobook } from "../types/Audiobook";
import OrganizeAudiobookInput from "../types/OrganizeAudiobookInput";
import { MetadataSearchResult } from "../types/MetadataSearchResult";

vi.mock("../services/SimilarValueService", () => ({
  default: {
    getAuthorNames: vi.fn(),
    getSeriesNames: vi.fn(),
    addKnownAuthorNames: vi.fn(),
    addKnownSeriesNames: vi.fn(),
  },
}));

vi.mock("../services/LanguageService", () => ({
  default: {
    getLanguageOptions: vi.fn(),
  },
}));

const vuetify = createVuetify({ components, directives });

const emptyBookDetails: Audiobook = {
  authors: [],
  narrators: [],
  genres: [],
};

function makeInput(
  overrides: Partial<OrganizeAudiobookInput> = {},
): OrganizeAudiobookInput {
  return {
    authors: "",
    narrators: "",
    bookName: "",
    subtitle: "",
    series: "",
    seriesPart: "",
    year: undefined,
    genres: "",
    description: "",
    ...overrides,
  };
}

function mountForm(
  input: OrganizeAudiobookInput,
  props: Record<string, unknown> = {},
) {
  return mount(BookEditForm, {
    global: {
      plugins: [vuetify],
    },
    props: {
      searchBookDetails: emptyBookDetails,
      currentPath: "/library/Author/Book/file.m4b",
      newPath: "",
      input,
      ...props,
    },
    attachTo: document.body,
  });
}

beforeEach(() => {
  vi.clearAllMocks();
  (SimilarValueService.getAuthorNames as any).mockResolvedValue([
    "J.K. Rowling",
    "Brandon Sanderson",
    "Robert Jordan",
  ]);
  (SimilarValueService.getSeriesNames as any).mockResolvedValue([
    "Harry Potter",
    "The Wheel of Time",
    "Mistborn",
  ]);
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

describe("BookEditForm language field", () => {
  it("offers exactly the managed languages", async () => {
    const wrapper = mountForm(makeInput());
    await flushPromises();

    expect((wrapper.vm as any).languageItems).toEqual([
      { code: "en", displayName: "English", aliases: ["en", "eng", "english"] },
      {
        code: "da",
        displayName: "Danish",
        aliases: ["da", "dan", "danish", "dansk"],
      },
    ]);

    wrapper.unmount();
  });

  it("folds a free-text language tag onto the matching option", async () => {
    const input = makeInput({ language: "English" });
    const wrapper = mountForm(input);
    await flushPromises();

    expect(input.language).toBe("en");

    wrapper.unmount();
  });

  // A strict select renders empty for a value it cannot offer, and the next save then silently
  // wipes a real language off the book.
  it("keeps a language the library does not manage selectable", async () => {
    const input = makeInput({ language: "German" });
    const wrapper = mountForm(input);
    await flushPromises();

    expect(input.language).toBe("German");
    expect((wrapper.vm as any).languageItems).toEqual([
      { code: "en", displayName: "English", aliases: ["en", "eng", "english"] },
      {
        code: "da",
        displayName: "Danish",
        aliases: ["da", "dan", "danish", "dansk"],
      },
      { code: "German", displayName: "German (unrecognized)", aliases: [] },
    ]);

    wrapper.unmount();
  });

  it("seeds an untagged book being added with the default language", async () => {
    const input = makeInput();
    const wrapper = mountForm(input, { defaultEmptyLanguage: true });
    await flushPromises();

    expect(input.language).toBe("en");

    wrapper.unmount();
  });

  // A library book that never had a language must stay empty, or it silently disappears from
  // Missing Tags just because its edit page was opened.
  it("leaves an existing library book without a language empty", async () => {
    const input = makeInput();
    const wrapper = mountForm(input);
    await flushPromises();

    expect(input.language).toBeUndefined();

    wrapper.unmount();
  });
});

describe("BookEditForm autocomplete suggestions", () => {
  it("fetches author and series name lists on mount", async () => {
    mountForm(makeInput());
    await nextTick();
    await flushPromises();

    expect(SimilarValueService.getAuthorNames).toHaveBeenCalledOnce();
    expect(SimilarValueService.getSeriesNames).toHaveBeenCalledOnce();
  });

  it("shows matching author suggestions from the fetched name list while typing and focused", async () => {
    const wrapper = mountForm(makeInput());
    await flushPromises();

    const authorField = wrapper.find('.author-field-wrap input[type="text"]');
    await authorField.trigger("focus");
    await authorField.setValue("Rowl");
    await nextTick();

    const suggestions = wrapper.findAll(".suggestion-menu .v-list-item");
    const suggestionTexts = suggestions.map((s) => s.text());
    expect(suggestionTexts).toContain("J.K. Rowling");
    expect(suggestionTexts).not.toContain("Brandon Sanderson");

    wrapper.unmount();
  });

  it("shows no suggestion menu when the author field is not focused", async () => {
    const wrapper = mountForm(makeInput({ authors: "Rowl" }));
    await flushPromises();
    await nextTick();

    expect(wrapper.find(".suggestion-menu").exists()).toBe(false);

    wrapper.unmount();
  });

  // Regression: noteSavedNames() appended to the same array the matcher had cached a folded
  // form of, so the next keystroke in the Authors field indexed a fold shorter than the list and
  // threw "Cannot read properties of undefined (reading 'includes')" out of the computed.
  it("_StillSuggestsAfterASaveIntroducesANewAuthorName", async () => {
    const wrapper = mountForm(makeInput());
    await flushPromises();

    const authorField = wrapper.find('.author-field-wrap input[type="text"]');
    await authorField.trigger("focus");
    await authorField.setValue("Rowl");
    await nextTick();
    expect(
      wrapper.findAll(".suggestion-menu .v-list-item").map((s) => s.text()),
    ).toEqual(["J.K. Rowling"]);

    // A save that introduces an author the fetched list did not have.
    await authorField.setValue("Ursula Le Guin");
    await nextTick();
    (wrapper.vm as any).noteSavedNames();
    await nextTick();

    await authorField.setValue("Ursula");
    await nextTick();
    expect(
      wrapper.findAll(".suggestion-menu .v-list-item").map((s) => s.text()),
    ).toEqual(["Ursula Le Guin"]);

    // The names that were there before must still be suggested.
    await authorField.setValue("Rowl");
    await nextTick();
    expect(
      wrapper.findAll(".suggestion-menu .v-list-item").map((s) => s.text()),
    ).toEqual(["J.K. Rowling"]);

    wrapper.unmount();
  });

  it("clicking a suggestion fills the author field", async () => {
    const input = makeInput();
    const wrapper = mountForm(input);
    await flushPromises();

    const authorField = wrapper.find('.author-field-wrap input[type="text"]');
    await authorField.trigger("focus");
    await authorField.setValue("Sand");
    await nextTick();

    const suggestionItem = wrapper
      .findAll(".suggestion-menu .v-list-item")
      .find((item) => item.text() === "Brandon Sanderson");
    expect(suggestionItem).toBeTruthy();

    await suggestionItem!.trigger("mousedown");
    await nextTick();

    expect((authorField.element as HTMLInputElement).value).toBe(
      "Brandon Sanderson",
    );

    wrapper.unmount();
  });
});

describe("BookEditForm similar-entries hint", () => {
  it("does not show a hint for an author with no close match", async () => {
    const input = makeInput();
    const wrapper = mountForm(input);
    await flushPromises();

    const authorField = wrapper.find('.author-field-wrap input[type="text"]');
    await authorField.setValue("Some Totally New Author");
    await authorField.trigger("blur");
    await nextTick();

    expect(wrapper.text()).not.toContain("Similar existing author");

    wrapper.unmount();
  });

  it("shows a similar-existing-author hint after a near-duplicate is entered", async () => {
    const input = makeInput();
    const wrapper = mountForm(input);
    await flushPromises();

    const authorField = wrapper.find('.author-field-wrap input[type="text"]');
    // Near-duplicate of "Robert Jordan" (one-char diff, over threshold length)
    await authorField.setValue("Robert Jordn");
    await authorField.trigger("blur");
    await nextTick();

    expect(wrapper.text()).toContain("Similar existing author");
    expect(wrapper.text()).toContain("Robert Jordan");

    wrapper.unmount();
  });

  it("clicking the author hint applies the suggested value", async () => {
    const input = makeInput();
    const wrapper = mountForm(input);
    await flushPromises();

    const authorField = wrapper.find('.author-field-wrap input[type="text"]');
    await authorField.setValue("Robert Jordn");
    await authorField.trigger("blur");
    await nextTick();

    const hintLink = wrapper.find(".v-alert a");
    expect(hintLink.exists()).toBe(true);
    await hintLink.trigger("click");
    await nextTick();

    expect((authorField.element as HTMLInputElement).value).toBe(
      "Robert Jordan",
    );

    wrapper.unmount();
  });

  it("shows a similar-existing-series hint once a scrape result fills in a near-duplicate series name", async () => {
    const searchResult: MetadataSearchResult = {
      url: "https://example.com/book",
      source: "Audible",
      authors: [{ name: "Robert Jordan" }],
      narrators: [],
      bookName: "The Eye of the World",
      year: 1990,
      series: [{ seriesName: "The Wheel of Tim", seriesPart: "1" }],
      genres: [],
    };

    // Stubs stand in for the search/preview dialogs so the flow BookEditForm actually
    // wires up can be driven end-to-end: opening the search dialog, choosing a search
    // result (-> readSearchResult), then applying the previewed tags (-> applyPreviewedTags),
    // which is what triggers the similar-series re-check after a scrape fill.
    const wrapper = mount(BookEditForm, {
      global: {
        plugins: [vuetify],
        stubs: {
          BookSearchDialog: {
            template:
              '<button class="stub-choose-result" @click="$emit(\'resultChosen\', result)"></button>',
            data() {
              return { result: searchResult };
            },
          },
          TagPreviewDialog: {
            template:
              '<button class="stub-apply-tags" @click="$emit(\'apply\', searchResult, fields)"></button>',
            data() {
              return {
                searchResult,
                fields: new Set(["authors", "series"]),
              };
            },
          },
        },
      },
      props: {
        searchBookDetails: emptyBookDetails,
        currentPath: "/library/Author/Book/file.m4b",
        newPath: "",
        input: makeInput(),
      },
      attachTo: document.body,
    });
    await flushPromises();

    await wrapper.find("button.v-btn").trigger("click"); // "Search" toolbar button
    await nextTick();

    // v-dialog teleports its content to <body>, outside the mounted wrapper's
    // element, so the stub buttons inside it must be queried via the document.
    const chooseResultBtn = document.body.querySelector(
      ".stub-choose-result",
    ) as HTMLElement;
    expect(chooseResultBtn).toBeTruthy();
    chooseResultBtn.dispatchEvent(new Event("click", { bubbles: true }));
    await nextTick();

    const applyTagsBtn = document.body.querySelector(
      ".stub-apply-tags",
    ) as HTMLElement;
    expect(applyTagsBtn).toBeTruthy();
    applyTagsBtn.dispatchEvent(new Event("click", { bubbles: true }));
    await nextTick();

    expect(document.body.textContent).toContain("Similar existing series");
    expect(document.body.textContent).toContain("The Wheel of Time");

    wrapper.unmount();
  });
});

function flushPromises() {
  return new Promise((resolve) => setTimeout(resolve, 0));
}

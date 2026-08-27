import { describe, it, expect, vi, beforeEach } from "vitest";
import { mount } from "@vue/test-utils";
import { createVuetify } from "vuetify";
import * as components from "vuetify/components";
import * as directives from "vuetify/directives";
import { nextTick } from "vue";
import BookEditForm from "./BookEditForm.vue";
import SimilarValueService from "../services/SimilarValueService";
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

function mountForm(input: OrganizeAudiobookInput) {
  return mount(BookEditForm, {
    global: {
      plugins: [vuetify],
    },
    props: {
      searchBookDetails: emptyBookDetails,
      currentPath: "/library/Author/Book/file.m4b",
      newPath: "",
      input,
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

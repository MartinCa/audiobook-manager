import { describe, it, expect, vi, beforeEach } from "vitest";
import { mount } from "@vue/test-utils";
import { createVuetify } from "vuetify";
import * as components from "vuetify/components";
import * as directives from "vuetify/directives";
import BookOrganize from "./BookOrganize.vue";
import AudiobookService from "../services/AudiobookService";
import { Audiobook } from "../types/Audiobook";

const vuetify = createVuetify({ components, directives });

vi.mock("../services/AudiobookService", () => ({
  default: {
    parseBookDetails: vi.fn(),
    generateNewPath: vi.fn(),
    checkTargetPath: vi.fn(),
    organizeBook: vi.fn(),
  },
}));

const mockedParseBookDetails = vi.mocked(AudiobookService.parseBookDetails);
const mockedGenerateNewPath = vi.mocked(AudiobookService.generateNewPath);

function makeBook(): Audiobook {
  return {
    authors: [{ name: "Author" }],
    narrators: [],
    bookName: "A Book",
    genres: [],
    cover: { base64Data: "original-cover-data", mimeType: "image/jpeg" },
    fileInfo: {
      fullPath: "/import/book.m4b",
      fileName: "book.m4b",
      sizeInBytes: 100,
    },
  };
}

function flushPromises() {
  return new Promise((resolve) => setTimeout(resolve, 0));
}

function mountComponent() {
  return mount(BookOrganize, {
    global: {
      plugins: [vuetify],
      stubs: {
        BookEditForm: {
          template: "<div></div>",
          props: ["input", "searchBookDetails", "currentPath", "newPath"],
        },
        BookDeleteDialog: true,
        DuplicateTargetDialog: true,
        ErrorNotifications: true,
      },
    },
    props: { bookPath: "/import/book.m4b" },
  });
}

beforeEach(() => {
  vi.clearAllMocks();
  mockedGenerateNewPath.mockResolvedValue("generated/path.m4b");
});

describe("BookOrganize path regeneration debounce", () => {
  it("does not call generateNewPath again when only cover fields change", async () => {
    mockedParseBookDetails.mockResolvedValue(makeBook());

    const wrapper = mountComponent();
    await flushPromises();

    // Wait out the initial debounce window from resetInput() populating `input`.
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

  it("does not call generateNewPath when non-path fields like description change", async () => {
    mockedParseBookDetails.mockResolvedValue(makeBook());

    const wrapper = mountComponent();
    await flushPromises();

    // Wait out the initial debounce window from resetInput() populating `input`.
    await new Promise((resolve) => setTimeout(resolve, 350));
    const callsAfterInitialLoad = mockedGenerateNewPath.mock.calls.length;

    const vm = wrapper.vm as any;
    vm.input.description =
      "A long description that does not affect path generation";
    vm.input.copyright = "2024 Copyright";
    await flushPromises();
    await new Promise((resolve) => setTimeout(resolve, 350));

    expect(mockedGenerateNewPath).toHaveBeenCalledTimes(callsAfterInitialLoad);

    wrapper.unmount();
  });

  it("still calls generateNewPath when a non-cover field changes", async () => {
    mockedParseBookDetails.mockResolvedValue(makeBook());

    const wrapper = mountComponent();
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

  // Regression: the debounced path regeneration had no matching onUnmounted cancel, so it fired
  // after the component was gone - writing to dead refs and issuing a request nobody reads. This
  // component is mounted inside the discovered-books expansion panels, whose rows are removed
  // while open, so it genuinely unmounts mid-debounce.
  it("_DoesNotRegeneratePathAfterUnmount", async () => {
    mockedParseBookDetails.mockResolvedValue(makeBook());

    const wrapper = mountComponent();
    await flushPromises();
    await new Promise((resolve) => setTimeout(resolve, 350));

    const vm = wrapper.vm as any;
    vm.input.bookName = "Renamed Just Before Unmount";
    await flushPromises();

    const callsBeforeUnmount = mockedGenerateNewPath.mock.calls.length;
    wrapper.unmount();

    await new Promise((resolve) => setTimeout(resolve, 350));

    expect(mockedGenerateNewPath.mock.calls.length).toBe(callsBeforeUnmount);
  });
});

describe("BookOrganize error handling", () => {
  it("_ThrowsUserNotificationErrorWhenOrganizingWithoutBookDetails", async () => {
    mockedParseBookDetails.mockReturnValue(new Promise(() => {}));
    // We intentionally do not wait for parseBookDetails to populate bookDetails
    const wrapper = mountComponent();
    const vm = wrapper.vm as any;

    // We mock the form validation to pass so we can reach the organizeBook logic
    (wrapper.getCurrentComponent() as any).setupState.bookEditForm = {
      validate: vi.fn().mockResolvedValue(true),
    };

    // The method should throw UserNotificationError
    await expect(vm.organizeBook()).rejects.toThrow(
      "Failed to convert input to audiobook data.",
    );
  });
});

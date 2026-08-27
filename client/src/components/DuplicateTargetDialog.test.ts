import { describe, it, expect, vi, beforeEach } from "vitest";
import { mount } from "@vue/test-utils";
import { createVuetify } from "vuetify";
import * as components from "vuetify/components";
import * as directives from "vuetify/directives";
import { nextTick } from "vue";
import DuplicateTargetDialog from "./DuplicateTargetDialog.vue";
import FilesService from "../services/FilesService";

vi.mock("../services/FilesService", () => ({
  default: {
    getDirectoryContents: vi.fn(),
    deleteBook: vi.fn(),
  },
}));

const vuetify = createVuetify({ components, directives });

function flushPromises() {
  return new Promise((resolve) => setTimeout(resolve, 0));
}

function mountDialog() {
  return mount(DuplicateTargetDialog, {
    global: { plugins: [vuetify] },
    props: {
      newPath: "/import/Author/2016 - Children of Time/book.m4b",
      newSizeInBytes: 598_000_000,
      newDurationInSeconds: 39600,
      targetPath: "/library/Author/2016 - Children of Time/book.m4b",
      existingSizeInBytes: 590_000_000,
      existingDurationInSeconds: 39000,
    },
    attachTo: document.body,
  });
}

beforeEach(() => {
  vi.clearAllMocks();
  (FilesService.getDirectoryContents as any).mockResolvedValue([]);
  (FilesService.deleteBook as any).mockResolvedValue(undefined);
});

describe("DuplicateTargetDialog", () => {
  it("shows the new and existing file paths and sizes for comparison", () => {
    const wrapper = mountDialog();

    const text = wrapper.text();
    expect(text).toContain("/import/Author/2016 - Children of Time/book.m4b");
    expect(text).toContain("/library/Author/2016 - Children of Time/book.m4b");
    expect(text).toContain("598.00 MB");
    expect(text).toContain("590.00 MB");

    wrapper.unmount();
  });

  it("emits cancelled when Cancel is clicked without touching either file", async () => {
    const wrapper = mountDialog();

    const cancelBtn = wrapper
      .findAll("button")
      .find((b) => b.text() === "Cancel")!;
    await cancelBtn.trigger("click");

    expect(wrapper.emitted("cancelled")).toHaveLength(1);
    expect(FilesService.deleteBook).not.toHaveBeenCalled();

    wrapper.unmount();
  });

  it("Replace existing deletes the existing file and emits existingDeleted", async () => {
    const wrapper = mountDialog();

    const replaceBtn = wrapper
      .findAll("button")
      .find((b) => b.text() === "Replace existing")!;
    await replaceBtn.trigger("click");
    await flushPromises();
    await nextTick();

    const deleteBtn = wrapper
      .findAll("button")
      .find((b) => b.text() === "Delete")!;
    await deleteBtn.trigger("click");
    await flushPromises();

    expect(FilesService.deleteBook).toHaveBeenCalledWith(
      "/library/Author/2016 - Children of Time/book.m4b",
    );
    expect(wrapper.emitted("existingDeleted")).toHaveLength(1);
    expect(wrapper.emitted("newDeleted")).toBeUndefined();

    wrapper.unmount();
  });

  it("Delete new file deletes the new file and emits newDeleted", async () => {
    const wrapper = mountDialog();

    const deleteNewBtn = wrapper
      .findAll("button")
      .find((b) => b.text() === "Delete new file")!;
    await deleteNewBtn.trigger("click");
    await flushPromises();
    await nextTick();

    const deleteBtn = wrapper
      .findAll("button")
      .find((b) => b.text() === "Delete")!;
    await deleteBtn.trigger("click");
    await flushPromises();

    expect(FilesService.deleteBook).toHaveBeenCalledWith(
      "/import/Author/2016 - Children of Time/book.m4b",
    );
    expect(wrapper.emitted("newDeleted")).toHaveLength(1);
    expect(wrapper.emitted("existingDeleted")).toBeUndefined();

    wrapper.unmount();
  });

  it("cancelling the nested delete confirmation returns to the comparison view without emitting", async () => {
    const wrapper = mountDialog();

    const replaceBtn = wrapper
      .findAll("button")
      .find((b) => b.text() === "Replace existing")!;
    await replaceBtn.trigger("click");
    await flushPromises();
    await nextTick();

    const nestedCancelBtn = wrapper
      .findAll("button")
      .find((b) => b.text() === "Cancel")!;
    await nestedCancelBtn.trigger("click");
    await nextTick();

    expect(FilesService.deleteBook).not.toHaveBeenCalled();
    expect(wrapper.emitted("existingDeleted")).toBeUndefined();
    expect(wrapper.emitted("newDeleted")).toBeUndefined();
    expect(wrapper.emitted("cancelled")).toBeUndefined();
    expect(wrapper.text()).toContain("Replace existing");

    wrapper.unmount();
  });
});

import { describe, it, expect, vi, beforeEach } from "vitest";
import { mount } from "@vue/test-utils";
import { createVuetify } from "vuetify";
import * as components from "vuetify/components";
import * as directives from "vuetify/directives";
import { nextTick } from "vue";
import SimilarValues from "./SimilarValues.vue";
import SimilarValueService from "../services/SimilarValueService";
import { SimilarValueGroup } from "../types/SimilarValue";

const vuetify = createVuetify({ components, directives });

vi.mock("../services/SimilarValueService", () => ({
  default: {
    getSimilarAuthors: vi.fn(),
    getSimilarSeries: vi.fn(),
    startAlign: vi.fn(),
    invalidateNameCaches: vi.fn(),
  },
}));

const fakeSignalR = {
  on: vi.fn(),
  off: vi.fn(),
  onReconnected: vi.fn(),
  offReconnected: vi.fn(),
};

vi.mock("@/signalr/hub", async () => {
  const vue = await import("vue");
  return {
    useSignalR: () => fakeSignalR,
    useSignalREvent: (token: string, callback: (...args: any[]) => void) => {
      vue.onMounted(() => fakeSignalR.on(token, callback));
      vue.onUnmounted(() => fakeSignalR.off(token, callback));
    },
    useSignalRReconnected: (callback: () => void) => {
      vue.onMounted(() => fakeSignalR.onReconnected(callback));
      vue.onUnmounted(() => fakeSignalR.offReconnected(callback));
    },
  };
});

vi.mock("../services/OperationsService", () => ({
  default: {
    getStatus: vi
      .fn()
      .mockResolvedValue({ isRunning: false, processed: 0, total: 0 }),
  },
}));

const group: SimilarValueGroup = {
  candidates: [
    {
      value: "J.K. Rowling",
      bookCount: 10,
      books: [],
    },
    {
      value: "JK Rowling",
      bookCount: 2,
      books: [],
    },
  ],
};

const flush = async () => {
  for (let i = 0; i < 5; i++) {
    await Promise.resolve();
    await nextTick();
  }
};

beforeEach(() => {
  vi.clearAllMocks();
  vi.mocked(SimilarValueService.getSimilarAuthors).mockResolvedValue([group]);
  vi.mocked(SimilarValueService.getSimilarSeries).mockResolvedValue([]);
});

describe("SimilarValues alignment confirmation", () => {
  // Regression: the count summed the whole group, including the candidate chosen as the target.
  // Books that already carry the target are never touched (AlignAuthorsAsync drops it from the
  // source list), so the dialog promised to rewrite 12 books for an operation that rewrites 2 -
  // on a confirmation that also warns the change cannot be undone.
  it("_CountsOnlyTheBooksTheAlignmentWillActuallyRewrite", async () => {
    const wrapper = mount(SimilarValues, { global: { plugins: [vuetify] } });
    await flush();

    const vm = wrapper.vm as any;
    expect(vm.authorGroups).toHaveLength(1);
    // The first candidate is preselected as the target.
    expect(vm.authorSelections[0].target).toBe("J.K. Rowling");

    vm.onApplyClick("author", group, vm.authorSelections[0]);
    await nextTick();

    expect(vm.pendingBookCount).toBe(2);

    wrapper.unmount();
  });

  it("counts every candidate when the target is a free-text value in no candidate", async () => {
    const wrapper = mount(SimilarValues, { global: { plugins: [vuetify] } });
    await flush();

    const vm = wrapper.vm as any;
    vm.authorSelections[0].customValue = "Joanne Rowling";
    vm.onCustomValueEntered(vm.authorSelections[0]);
    vm.onApplyClick("author", group, vm.authorSelections[0]);
    await nextTick();

    expect(vm.pendingBookCount).toBe(12);

    wrapper.unmount();
  });
});

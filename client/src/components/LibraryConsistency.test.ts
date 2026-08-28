import { describe, it, expect, vi, beforeEach } from "vitest";
import { mount } from "@vue/test-utils";
import { createVuetify } from "vuetify";
import * as components from "vuetify/components";
import * as directives from "vuetify/directives";
import LibraryConsistency from "./LibraryConsistency.vue";
import ConsistencyService from "../services/ConsistencyService";
import ConsistencyIssue from "../types/ConsistencyIssue";

const vuetify = createVuetify({ components, directives });

vi.mock("../services/ConsistencyService", () => ({
  default: {
    startCheck: vi.fn(),
    getIssues: vi.fn(),
    resolveIssue: vi.fn(),
    resolveByType: vi.fn(),
    resolveSelectedIssues: vi.fn(),
    getOrphanDirectories: vi.fn(),
    resolveOrphanDirectory: vi.fn(),
    resolveAllOrphanDirectories: vi.fn(),
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

const mockedGetIssues = vi.mocked(ConsistencyService.getIssues);
const mockedGetOrphanDirectories = vi.mocked(
  ConsistencyService.getOrphanDirectories,
);
const mockedResolveByType = vi.mocked(ConsistencyService.resolveByType);

function makeIssue(
  id: number,
  audiobookId: number,
  issueType: string,
): ConsistencyIssue {
  return {
    id,
    audiobookId,
    bookName: `Book ${audiobookId}`,
    authors: ["Author"],
    issueType,
    description: `Issue ${id}`,
    detectedAt: "2024-01-01T00:00:00Z",
  };
}

function flushPromises() {
  return new Promise((resolve) => setTimeout(resolve, 0));
}

function mountComponent() {
  return mount(LibraryConsistency, {
    global: {
      plugins: [vuetify],
      stubs: { "router-link": true },
    },
    attachTo: document.body,
  });
}

beforeEach(() => {
  vi.clearAllMocks();
  mockedGetOrphanDirectories.mockResolvedValue([]);
});

describe("LibraryConsistency bulk resolve", () => {
  // Helper: run the bulk-resolve flow for the WrongFilePath group.
  async function bulkResolveWrongFilePaths(wrapper: any, groupSize: number) {
    const panelTitle = wrapper
      .findAll(".v-expansion-panel-title")
      .find((t: any) => t.text().includes("Wrong File Paths"));
    expect(panelTitle).toBeTruthy();
    await panelTitle!.trigger("click");
    await flushPromises();

    const resolveAllBtn = wrapper
      .findAll("button")
      .find((b: any) => b.text().includes(`Resolve All ${groupSize}`));
    expect(resolveAllBtn).toBeTruthy();
    await resolveAllBtn!.trigger("click");
    await flushPromises();

    const confirmBtn = Array.from(
      document.body.querySelectorAll(".v-card-actions button"),
    ).find((b) => b.textContent?.trim() === "Resolve All") as
      HTMLElement | undefined;
    expect(confirmBtn).toBeTruthy();
    confirmBtn!.dispatchEvent(new Event("click", { bubbles: true }));
    await flushPromises();
  }

  it("re-reads the issue list from the server instead of filtering it client-side", async () => {
    const initial: ConsistencyIssue[] = [];
    for (let audiobookId = 1; audiobookId <= 3; audiobookId++) {
      initial.push(makeIssue(audiobookId * 10, audiobookId, "WrongFilePath"));
    }
    initial.push(makeIssue(9003, 999, "MissingCoverFile"));

    mockedGetIssues.mockResolvedValue(initial);
    mockedResolveByType.mockResolvedValue({ resolved: 3, failed: 0 });

    const wrapper = mountComponent();
    await flushPromises();

    // After the resolve, the server reports only the untouched issue.
    mockedGetIssues.mockResolvedValue([
      makeIssue(9003, 999, "MissingCoverFile"),
    ]);

    await bulkResolveWrongFilePaths(wrapper, 3);

    expect(mockedResolveByType).toHaveBeenCalledWith("WrongFilePath");
    // Once on mount, once after the bulk resolve.
    expect(mockedGetIssues).toHaveBeenCalledTimes(2);
    expect(wrapper.text()).not.toContain("Wrong File Paths");
    expect(wrapper.text()).toContain("Issue 9003");
    expect(wrapper.text()).toContain("Issues (1)");

    wrapper.unmount();
  });

  it("keeps issues the server reported as failed visible instead of hiding the whole type", async () => {
    // Regression guard: the old client-side filter removed every issue of the resolved type
    // regardless of `failed`, so genuinely unresolved books silently vanished from the list
    // until the next full consistency check.
    const initial: ConsistencyIssue[] = [
      makeIssue(10, 1, "WrongFilePath"),
      makeIssue(20, 2, "WrongFilePath"),
      makeIssue(30, 3, "WrongFilePath"),
    ];

    mockedGetIssues.mockResolvedValue(initial);
    mockedResolveByType.mockResolvedValue({ resolved: 2, failed: 1 });

    const wrapper = mountComponent();
    await flushPromises();

    // The server resolved two and still holds the third.
    const stillFailing = makeIssue(30, 3, "WrongFilePath");
    mockedGetIssues.mockResolvedValue([stillFailing]);

    await bulkResolveWrongFilePaths(wrapper, 3);

    expect(wrapper.text()).toContain("Wrong File Paths");
    expect(wrapper.text()).toContain("Issues (1)");
    expect(wrapper.text()).toContain("Issue 30");
    // The snackbar is teleported out of the component's own tree.
    expect(document.body.textContent).toContain("Resolved 2 issues (1 failed)");

    wrapper.unmount();
  });
});

describe("LibraryConsistency OPF issue grouping", () => {
  it("groups missing and incorrect OPF file issues under their own labeled groups", async () => {
    mockedGetIssues.mockResolvedValue([
      makeIssue(1, 1, "MissingOpfFile"),
      makeIssue(2, 2, "IncorrectOpfFile"),
    ]);

    const wrapper = mountComponent();
    await flushPromises();

    expect(wrapper.text()).toContain("Missing OPF Files");
    expect(wrapper.text()).toContain("Incorrect OPF Files");

    wrapper.unmount();
  });
});

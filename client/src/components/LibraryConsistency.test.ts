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

describe("LibraryConsistency bulkResolve filtering", () => {
  it("removes only the audiobooks whose WrongFilePath issues were resolved, leaving other issue types for other books and untouched issue types for the same books", async () => {
    // A reasonably sized mixed list: several audiobooks each with a WrongFilePath issue
    // (which will be bulk-resolved) plus various other issues that must survive filtering
    // exactly as before the fix (hoisting the Set construction must not change semantics).
    const issues: ConsistencyIssue[] = [];
    for (let audiobookId = 1; audiobookId <= 20; audiobookId++) {
      issues.push(makeIssue(audiobookId * 10, audiobookId, "WrongFilePath"));
    }
    // Extra unrelated issues: two share an audiobookId with a WrongFilePath issue (and, per
    // existing semantics, are removed alongside it since resolving a book's WrongFilePath
    // implies the whole audiobook was reprocessed), one belongs to an untouched audiobook.
    issues.push(makeIssue(9001, 1, "MissingCoverFile"));
    issues.push(makeIssue(9002, 2, "TagMismatch"));
    issues.push(makeIssue(9003, 999, "MissingCoverFile"));

    mockedGetIssues.mockResolvedValue(issues);
    mockedResolveByType.mockResolvedValue({ resolved: 20, failed: 0 });

    const wrapper = mountComponent();
    await flushPromises();

    // Expand the WrongFilePath group panel so its (lazily-rendered) content mounts.
    const panelTitle = wrapper
      .findAll(".v-expansion-panel-title")
      .find((t) => t.text().includes("Wrong File Paths"));
    expect(panelTitle).toBeTruthy();
    await panelTitle!.trigger("click");
    await flushPromises();

    // Trigger the bulk resolve flow: click "Resolve All" for WrongFilePath, then confirm.
    const resolveAllBtn = wrapper
      .findAll("button")
      .find((b) => b.text().includes("Resolve All 20"));
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

    expect(mockedResolveByType).toHaveBeenCalledWith("WrongFilePath");

    // All 20 WrongFilePath issues gone (its whole group section disappears), and per the
    // existing (preserved) semantics, other issue types for the same now-resolved
    // audiobooks are cleared out too.
    expect(wrapper.text()).not.toContain("Wrong File Paths");
    expect(wrapper.text()).not.toContain("Issue 9001");
    expect(wrapper.text()).not.toContain("Issue 9002");
    // An issue for an audiobook that had no WrongFilePath issue is untouched.
    expect(wrapper.text()).toContain("Issue 9003");
    expect(wrapper.text()).toContain("Issues (1)");

    wrapper.unmount();
  });
});

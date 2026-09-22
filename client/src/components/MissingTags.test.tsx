import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MissingTags } from "./MissingTags";
import { queryKeys } from "@/lib/queryKeys";
import { RouterTestWrapper } from "@/test-utils/routerTestUtils";
import { SignalRContext } from "@/context/SignalRContext";
import { OperationKeys } from "@/constants/signalrEvents";
import { notifications } from "@/lib/notifications";

vi.mock("@/lib/notifications", () => ({
  notifications: { success: vi.fn(), error: vi.fn(), info: vi.fn(), warning: vi.fn() },
}));

vi.mock("@/services/api", () => ({
  missingTagsApi: {
    getFields: vi.fn(),
    getAudiobooksMissingTags: vi.fn(),
    startLanguageBackfill: vi.fn(),
  },
  operationsApi: {
    getStatus: vi.fn(),
  },
  // OwnedBookList's own fixtures: option-filter dropdowns, the issue-count/pending-refresh
  // badge summaries, and the cover URL helper - unrelated to this file's own assertions.
  browseApi: {
    getCoverUrl: vi.fn((id: number) => `/api/browse/audiobooks/${id}/cover`),
    getFilterOptions: vi.fn().mockResolvedValue({ sources: [], genres: [], languages: [] }),
  },
  settingsApi: {
    getLanguages: vi.fn().mockResolvedValue({ languages: [] }),
  },
  consistencyApi: {
    getIssueSummary: vi.fn().mockResolvedValue({}),
  },
  metadataRefreshApi: {
    getPendingSummary: vi.fn().mockResolvedValue([]),
  },
}));

import { missingTagsApi, operationsApi } from "@/services/api";

const mockSignalRValue = {
  connection: null,
  isConnected: false,
  on: vi.fn(),
  off: vi.fn(),
  onReconnected: vi.fn(),
  offReconnected: vi.fn(),
};

// OwnedBookList mounts BookBulkActionBar, which resyncs three OTHER operations
// (bulk-edit/metadata-refresh/consistency-check-selected) through this same
// operationsApi.getStatus - a blanket mockResolvedValue would report all of them running too,
// popping up three unrelated progress bars alongside the language-backfill one this file tests.
function mockLanguageBackfillStatus(status: {
  isRunning: boolean;
  processed: number;
  total: number;
}) {
  vi.mocked(operationsApi.getStatus).mockImplementation((key: string) =>
    Promise.resolve(
      key === OperationKeys.languageBackfill
        ? status
        : { isRunning: false, processed: 0, total: 0 },
    ),
  );
}

describe("MissingTags", () => {
  let queryClient: QueryClient;

  beforeEach(() => {
    vi.clearAllMocks();
    queryClient = new QueryClient({
      defaultOptions: { queries: { retry: false } },
    });

    vi.mocked(missingTagsApi.getFields).mockResolvedValue([
      { key: "language", label: "Language", isCriticalByDefault: true },
      { key: "year", label: "Year", isCriticalByDefault: true },
      { key: "series", label: "Series", isCriticalByDefault: false },
    ]);

    vi.mocked(missingTagsApi.getAudiobooksMissingTags).mockResolvedValue({
      items: [
        {
          audiobookId: 101,
          bookName: "Test Book Without Language",
          authors: ["Author A"],
          narrators: [],
          missingFields: ["language"],
        },
      ],
      totalCount: 1,
    });

    mockLanguageBackfillStatus({ isRunning: false, processed: 0, total: 0 });
  });

  const renderComponent = () =>
    render(
      <SignalRContext.Provider value={mockSignalRValue}>
        <QueryClientProvider client={queryClient}>
          <RouterTestWrapper ui={<MissingTags />} />
        </QueryClientProvider>
      </SignalRContext.Provider>,
    );

  it("loads fields and displays audiobooks with missing tags", async () => {
    renderComponent();

    expect(await screen.findByText("Missing Tags Inspection")).toBeInTheDocument();
    expect(await screen.findByText("Language")).toBeInTheDocument();
    expect(await screen.findByText(/Test Book Without Language/)).toBeInTheDocument();
    expect(screen.getByText("Missing language")).toBeInTheDocument();
  });

  it("toggles field filter selection", async () => {
    renderComponent();

    const seriesBadge = await screen.findByText("Series");
    fireEvent.click(seriesBadge);

    await waitFor(() => {
      expect(missingTagsApi.getAudiobooksMissingTags).toHaveBeenCalledWith(
        expect.arrayContaining(["language", "year", "series"]),
        { page: 0, pageSize: 50, search: "", filters: {} },
      );
    });
  });

  // Regression for the review finding: a language backfill can only shrink this list, so a user
  // sitting on a later page was left fetching a page that no longer exists - it came back empty
  // and the section showed a dead-end empty state. Completion drops back to page 0.
  it("drops back to page 0 when the backfill completes", async () => {
    const getBooks = vi.mocked(missingTagsApi.getAudiobooksMissingTags);
    const book = (offset: number) => ({
      audiobookId: 200 + offset,
      bookName: `Book ${String(offset).padStart(2, "0")}`,
      authors: ["Author A"],
      narrators: [],
      missingFields: ["language"],
    });
    getBooks.mockImplementation((_fields, params) => {
      const page = params?.page ?? 0;
      return Promise.resolve({
        items: Array.from({ length: page === 1 ? 10 : 50 }, (_, i) => book(page * 50 + i + 1)),
        totalCount: 60,
      });
    });

    renderComponent();

    expect(await screen.findByText(/Book 01/)).toBeInTheDocument();
    screen.getByRole("button", { name: "Next" }).click();

    await waitFor(() => {
      expect(getBooks).toHaveBeenLastCalledWith(expect.anything(), {
        page: 1,
        pageSize: 50,
        search: "",
        filters: {},
      });
    });
    expect(await screen.findByText(/Book 51/)).toBeInTheDocument();

    // The backfill closes the gap (a book gains its language, total shrinks to 50).
    getBooks.mockImplementation((_fields, params) => {
      const page = params?.page ?? 0;
      return Promise.resolve({
        items: page === 0 ? Array.from({ length: 50 }, (_, i) => book(i + 1)) : [],
        totalCount: 50,
      });
    });

    mockLanguageBackfillStatus({ isRunning: true, processed: 5, total: 10 });
    await queryClient.invalidateQueries({ queryKey: queryKeys.languageBackfillStatus() });
    // The running state must actually render (and flip prevRunningRef) before completing.
    expect(await screen.findByText(/50%/)).toBeInTheDocument();

    mockLanguageBackfillStatus({ isRunning: false, processed: 10, total: 10 });
    await queryClient.invalidateQueries({ queryKey: queryKeys.languageBackfillStatus() });

    await waitFor(() => {
      expect(notifications.success).toHaveBeenCalledWith("Language backfill operation completed");
    });
    await waitFor(() => {
      expect(getBooks).toHaveBeenLastCalledWith(expect.anything(), {
        page: 0,
        pageSize: 50,
        search: "",
        filters: {},
      });
    });
    expect(await screen.findByText(/Audiobooks with Missing Tags \(50\)/)).toBeInTheDocument();
    expect(await screen.findByText(/Book 01/)).toBeInTheDocument();
  });

  it("runs language backfill and stops polling once complete without infinite loop", async () => {
    vi.mocked(missingTagsApi.startLanguageBackfill).mockResolvedValue();

    // Starts running
    mockLanguageBackfillStatus({ isRunning: true, processed: 5, total: 10 });

    renderComponent();

    const backfillButton = await screen.findByRole("button", {
      name: /backfill missing languages/i,
    });
    fireEvent.click(backfillButton);

    await waitFor(() => {
      expect(missingTagsApi.startLanguageBackfill).toHaveBeenCalled();
    });

    // Verify progress bar is visible while running
    expect(await screen.findByText(/50%/)).toBeInTheDocument();

    // Completes
    mockLanguageBackfillStatus({ isRunning: false, processed: 10, total: 10 });

    await queryClient.invalidateQueries({ queryKey: queryKeys.languageBackfillStatus() });

    await waitFor(() => {
      expect(notifications.success).toHaveBeenCalledWith("Language backfill operation completed");
    });

    // Verify the notification was only triggered once (not looped)
    expect(
      vi
        .mocked(notifications.success)
        .mock.calls.filter((call) => call[0] === "Language backfill operation completed"),
    ).toHaveLength(1);
  });

  it("shows skeleton loading rows while fields and books load", async () => {
    vi.mocked(missingTagsApi.getFields).mockImplementation(() => new Promise(() => {}));

    const { container } = renderComponent();

    expect(await screen.findByRole("status", { name: "Scanning tags..." })).toBeInTheDocument();
    expect(container.querySelectorAll('[data-slot="skeleton"]').length).toBeGreaterThan(0);
  });
});

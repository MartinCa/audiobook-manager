import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MissingTags } from "./MissingTags";
import { RouterTestWrapper } from "@/test-utils/routerTestUtils";
import type * as SonnerModule from "sonner";
import { toast } from "sonner";

vi.mock("sonner", async (importOriginal) => {
  const actual = await importOriginal<typeof SonnerModule>();
  return {
    ...actual,
    toast: {
      success: vi.fn(),
      error: vi.fn(),
      info: vi.fn(),
    },
  };
});

vi.mock("@/services/api", () => ({
  missingTagsApi: {
    getFields: vi.fn(),
    getAudiobooksMissingTags: vi.fn(),
    startLanguageBackfill: vi.fn(),
  },
  operationsApi: {
    getStatus: vi.fn(),
  },
}));

import { missingTagsApi, operationsApi } from "@/services/api";

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
          missingFields: ["language"],
        },
      ],
      totalCount: 1,
    });

    vi.mocked(operationsApi.getStatus).mockResolvedValue({
      isRunning: false,
      processed: 0,
      total: 0,
    });
  });

  const renderComponent = () =>
    render(
      <QueryClientProvider client={queryClient}>
        <RouterTestWrapper ui={<MissingTags />} />
      </QueryClientProvider>,
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
        { page: 0, pageSize: 50, search: "" },
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

    vi.mocked(operationsApi.getStatus).mockResolvedValue({
      isRunning: true,
      processed: 5,
      total: 10,
    });
    await queryClient.invalidateQueries({ queryKey: ["languageBackfillStatus"] });
    // The running state must actually render (and flip prevRunningRef) before completing.
    expect(await screen.findByText(/50%/)).toBeInTheDocument();

    vi.mocked(operationsApi.getStatus).mockResolvedValue({
      isRunning: false,
      processed: 10,
      total: 10,
    });
    await queryClient.invalidateQueries({ queryKey: ["languageBackfillStatus"] });

    await waitFor(() => {
      expect(toast.success).toHaveBeenCalledWith("Language backfill operation completed");
    });
    await waitFor(() => {
      expect(getBooks).toHaveBeenLastCalledWith(expect.anything(), {
        page: 0,
        pageSize: 50,
        search: "",
      });
    });
    expect(await screen.findByText(/Audiobooks with Missing Tags \(50\)/)).toBeInTheDocument();
    expect(await screen.findByText(/Book 01/)).toBeInTheDocument();
  });

  it("runs language backfill and stops polling once complete without infinite loop", async () => {
    vi.mocked(missingTagsApi.startLanguageBackfill).mockResolvedValue();

    // Starts running
    vi.mocked(operationsApi.getStatus).mockResolvedValue({
      isRunning: true,
      processed: 5,
      total: 10,
    });

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
    vi.mocked(operationsApi.getStatus).mockResolvedValue({
      isRunning: false,
      processed: 10,
      total: 10,
    });

    await queryClient.invalidateQueries({ queryKey: ["languageBackfillStatus"] });

    await waitFor(() => {
      expect(toast.success).toHaveBeenCalledWith("Language backfill operation completed");
    });

    // Verify toast was only triggered once (not looped)
    expect(
      vi
        .mocked(toast.success)
        .mock.calls.filter((call) => call[0] === "Language backfill operation completed"),
    ).toHaveLength(1);
  });
});

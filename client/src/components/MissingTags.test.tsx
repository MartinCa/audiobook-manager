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

    vi.mocked(missingTagsApi.getAudiobooksMissingTags).mockResolvedValue([
      {
        audiobookId: 101,
        bookName: "Test Book Without Language",
        authors: ["Author A"],
        missingFields: ["language"],
      },
    ]);

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
      );
    });
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

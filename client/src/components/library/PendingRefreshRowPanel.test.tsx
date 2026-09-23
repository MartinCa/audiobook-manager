import { describe, it, expect, vi } from "vitest";
import { render, screen, waitFor, fireEvent } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { PendingRefreshRowPanel } from "./PendingRefreshRowPanel";
import type { AudiobookDetail } from "@/types/AudiobookDetail";
import type { PendingMetadataRefresh } from "@/types/MetadataRefresh";

vi.mock("@/lib/notifications", () => ({
  notifications: { success: vi.fn(), error: vi.fn(), info: vi.fn(), warning: vi.fn() },
}));

vi.mock("@/services/api", () => ({
  browseApi: {
    getAudiobookDetail: vi.fn(),
  },
  metadataRefreshApi: {
    getPendingForAudiobook: vi.fn(),
    applyPending: vi.fn().mockResolvedValue(undefined),
  },
  settingsApi: {
    getLanguages: vi.fn().mockResolvedValue({ languages: [] }),
  },
}));

import { browseApi, metadataRefreshApi } from "@/services/api";

function renderPanel(onApplied: () => void = vi.fn()) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={queryClient}>
      <PendingRefreshRowPanel audiobookId={42} onApplied={onApplied} />
    </QueryClientProvider>,
  );
}

const bookDetailWithOnlyRatingDiffering: AudiobookDetail = {
  id: 42,
  bookName: "Same Title",
  subtitle: undefined,
  series: undefined,
  seriesPart: undefined,
  year: undefined,
  authors: ["Author A"],
  narrators: [],
  genres: [],
  description: undefined,
  copyright: undefined,
  publisher: undefined,
  language: undefined,
  rating: "4.0",
  asin: undefined,
  www: undefined,
  filePath: "/library/book.m4b",
  fileName: "book.m4b",
  sizeInBytes: 1000,
  durationInSeconds: 1000,
  authorRefs: [],
};

const pendingWithOnlyRatingDiffering: PendingMetadataRefresh = {
  audiobookId: 42,
  fetchedAt: "2026-09-23T00:00:00Z",
  sourceName: "Audible",
  sourceUrl: "https://example.com/book",
  payload: {
    url: "https://example.com/book",
    source: "Audible",
    authors: ["Author A"],
    narrators: [],
    bookName: "Same Title",
    subtitle: undefined,
    seriesName: undefined,
    seriesPart: undefined,
    year: undefined,
    genres: [],
    description: undefined,
    language: undefined,
    rating: "4.5",
    copyright: undefined,
    publisher: undefined,
    asin: undefined,
  },
};

describe("PendingRefreshRowPanel", () => {
  // Regression: the pending-snapshot query can resolve before the book-detail query. Before the
  // fix, the pre-selection init ran off that pending-only render (currentInput still {}), which
  // computed every field the snapshot carried as "changed" and froze that over-inclusive key set
  // into `selected` forever (lastKeys never re-syncs once non-null). A book whose stored snapshot
  // really only changed one field (Rating here - Authors/BookName/etc. are identical) would then
  // apply those extra, never-shown-as-changed fields too. Forcing the pending query to resolve
  // well before the book-detail query reproduces the race deterministically.
  it("selects and applies only the fields that actually differ, even when the pending snapshot resolves before the book detail", async () => {
    let resolveBookDetail!: (value: AudiobookDetail) => void;
    vi.mocked(browseApi.getAudiobookDetail).mockReturnValue(
      new Promise<AudiobookDetail>((resolve) => {
        resolveBookDetail = resolve;
      }),
    );
    vi.mocked(metadataRefreshApi.getPendingForAudiobook).mockResolvedValue(
      pendingWithOnlyRatingDiffering,
    );

    renderPanel();

    // Let the pending-snapshot query resolve and commit at least one render before the
    // book-detail query does - this is the race the fix guards against.
    await waitFor(() => expect(metadataRefreshApi.getPendingForAudiobook).toHaveBeenCalled());
    await new Promise((r) => setTimeout(r, 0));

    resolveBookDetail(bookDetailWithOnlyRatingDiffering);

    const applyButton = await screen.findByRole("button", { name: /apply selected/i });
    // Only Rating actually differs - Authors/BookName/etc. are identical between the book and
    // the snapshot, so they must not be counted as selected even though they carry a value.
    expect(applyButton).toHaveTextContent("Apply Selected (1)");

    fireEvent.click(applyButton);

    await waitFor(() => expect(metadataRefreshApi.applyPending).toHaveBeenCalled());
    expect(metadataRefreshApi.applyPending).toHaveBeenCalledWith(42, ["Rating"]);
  });
});

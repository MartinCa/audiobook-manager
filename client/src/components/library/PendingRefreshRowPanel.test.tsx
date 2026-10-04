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
    checkApplyTarget: vi.fn().mockResolvedValue(undefined),
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
  // Matches pendingWithOnlyRatingDiffering's payload.url so this fixture keeps differing on
  // Rating alone - adding Www to CLIENT_KEY_TO_BACKEND_FIELDS means an unset www here would now
  // also surface as a (spurious, for this test's purpose) changed field.
  www: "https://example.com/book",
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
    expect(metadataRefreshApi.applyPending).toHaveBeenCalledWith(
      42,
      ["Rating"],
      false,
      undefined,
      false,
    );
  });

  // Regression: CLIENT_KEY_TO_BACKEND_FIELDS had no "www" entry, so a book newly matched via the
  // bulk online-match flow (no stored Www yet) never got the URL applied through this per-row
  // panel - MetadataRefreshApplier can write Www since the bulk-apply fix, but this panel's
  // client-to-backend field map silently dropped the "www" diff before it ever reached the apply
  // call, so the book stayed unmatched via this path even though the bulk "Apply All" path (which
  // recomputes the full field set server-side) was fixed.
  it("includes Www in the fields it selects and applies when the book has no stored URL", async () => {
    vi.mocked(browseApi.getAudiobookDetail).mockResolvedValue({
      ...bookDetailWithOnlyRatingDiffering,
      rating: "4.5",
      www: undefined,
    });
    vi.mocked(metadataRefreshApi.getPendingForAudiobook).mockResolvedValue(
      pendingWithOnlyRatingDiffering,
    );

    renderPanel();

    const applyButton = await screen.findByRole("button", { name: /apply selected/i });
    expect(applyButton).toHaveTextContent("Apply Selected (1)");

    fireEvent.click(applyButton);

    await waitFor(() => expect(metadataRefreshApi.applyPending).toHaveBeenCalled());
    expect(metadataRefreshApi.applyPending).toHaveBeenCalledWith(
      42,
      ["Www"],
      false,
      undefined,
      false,
    );
  });

  // Regression: with the toggle left at its default (unchecked), a "Title: Subtitle"-shaped
  // pending title must be applied unsplit, and the toggle must be passed through to the apply
  // call once checked.
  it("defaults the split-on-colon toggle to off, and passes it through when checked", async () => {
    vi.mocked(browseApi.getAudiobookDetail).mockResolvedValue(bookDetailWithOnlyRatingDiffering);
    vi.mocked(metadataRefreshApi.getPendingForAudiobook).mockResolvedValue(
      pendingWithOnlyRatingDiffering,
    );

    renderPanel();

    const applyButton = await screen.findByRole("button", { name: /apply selected/i });
    fireEvent.click(applyButton);

    await waitFor(() => expect(metadataRefreshApi.applyPending).toHaveBeenCalled());
    expect(metadataRefreshApi.applyPending).toHaveBeenLastCalledWith(
      42,
      ["Rating"],
      false,
      undefined,
      false,
    );

    const toggle = screen.getByRole("checkbox", {
      name: "Split title into book name and subtitle at first colon",
    });
    fireEvent.click(toggle);

    fireEvent.click(screen.getByRole("button", { name: /apply selected/i }));
    await waitFor(() => expect(metadataRefreshApi.applyPending).toHaveBeenCalledTimes(2));
    expect(metadataRefreshApi.applyPending).toHaveBeenLastCalledWith(
      42,
      ["Rating"],
      true,
      undefined,
      false,
    );
  });

  // Regression: switching the split toggle on makes "subtitle" a changed field, but the selection
  // was only seeded once, so the newly-split subtitle was shown as changed yet left unselected.
  it("selects the subtitle when the split toggle is switched on", async () => {
    vi.mocked(browseApi.getAudiobookDetail).mockResolvedValue({
      ...bookDetailWithOnlyRatingDiffering,
      rating: "4.5",
    });
    vi.mocked(metadataRefreshApi.getPendingForAudiobook).mockResolvedValue({
      ...pendingWithOnlyRatingDiffering,
      payload: { ...pendingWithOnlyRatingDiffering.payload, bookName: "Same Title: A Subtitle" },
    });

    renderPanel();

    // Unsplit: the full title differs from the book's, nothing else does.
    const applyButton = await screen.findByRole("button", { name: /apply selected/i });
    expect(applyButton).toHaveTextContent("Apply Selected (1)");

    fireEvent.click(
      screen.getByRole("checkbox", {
        name: "Split title into book name and subtitle at first colon",
      }),
    );

    // Split: book name now matches, and the subtitle is a changed, selected field.
    await waitFor(() =>
      expect(screen.getByRole("button", { name: /apply selected/i })).toHaveTextContent(
        "Apply Selected (1)",
      ),
    );
    fireEvent.click(screen.getByRole("button", { name: /apply selected/i }));
    await waitFor(() => expect(metadataRefreshApi.applyPending).toHaveBeenCalled());
    expect(vi.mocked(metadataRefreshApi.applyPending).mock.lastCall?.[1]).toEqual(["Subtitle"]);
  });

  describe("target path collision", () => {
    const collision = {
      targetPath: "/library/Author A/Same Title/Same Title.m4b",
      exists: true,
      existing: { audiobookId: 7, sizeInBytes: 4242, durationInSeconds: 99 },
    };

    function setUpRatingDiff() {
      vi.mocked(metadataRefreshApi.applyPending).mockClear();
      vi.mocked(metadataRefreshApi.checkApplyTarget).mockReset();
      vi.mocked(browseApi.getAudiobookDetail).mockResolvedValue(bookDetailWithOnlyRatingDiffering);
      vi.mocked(metadataRefreshApi.getPendingForAudiobook).mockResolvedValue(
        pendingWithOnlyRatingDiffering,
      );
    }

    // Regression: applying from the metadata-refresh page went straight to the apply endpoint, so a
    // book that would be filed onto an existing file failed with a bare error and no choice of
    // resolution, unlike the organize/book-detail flows that show the duplicate-target dialog.
    it("offers the duplicate-target dialog instead of applying when a file is already at the new path", async () => {
      setUpRatingDiff();
      vi.mocked(metadataRefreshApi.checkApplyTarget).mockResolvedValue(collision);

      renderPanel();
      fireEvent.click(await screen.findByRole("button", { name: /apply selected/i }));

      expect(await screen.findByText("Duplicate file at target location")).toBeInTheDocument();
      expect(screen.getByText(collision.targetPath)).toBeInTheDocument();
      expect(metadataRefreshApi.checkApplyTarget).toHaveBeenCalledWith(
        42,
        ["Rating"],
        false,
        undefined,
      );
      expect(metadataRefreshApi.applyPending).not.toHaveBeenCalled();
    });

    it("applies the checked fields with replaceExisting once the user chooses to replace", async () => {
      setUpRatingDiff();
      vi.mocked(metadataRefreshApi.checkApplyTarget).mockResolvedValue(collision);
      const onApplied = vi.fn();

      renderPanel(onApplied);
      fireEvent.click(await screen.findByRole("button", { name: /apply selected/i }));
      fireEvent.click(await screen.findByRole("button", { name: "Replace existing" }));

      await waitFor(() => expect(metadataRefreshApi.applyPending).toHaveBeenCalledTimes(1));
      expect(metadataRefreshApi.applyPending).toHaveBeenCalledWith(
        42,
        ["Rating"],
        false,
        undefined,
        true,
      );
      await waitFor(() => expect(onApplied).toHaveBeenCalled());
    });

    it("applies nothing when the user cancels the dialog", async () => {
      setUpRatingDiff();
      vi.mocked(metadataRefreshApi.checkApplyTarget).mockResolvedValue(collision);

      renderPanel();
      fireEvent.click(await screen.findByRole("button", { name: /apply selected/i }));
      fireEvent.click(await screen.findByRole("button", { name: "Cancel" }));

      await waitFor(() =>
        expect(screen.queryByText("Duplicate file at target location")).not.toBeInTheDocument(),
      );
      expect(metadataRefreshApi.applyPending).not.toHaveBeenCalled();
    });

    it("applies without the dialog when the server reports no collision", async () => {
      setUpRatingDiff();
      vi.mocked(metadataRefreshApi.checkApplyTarget).mockResolvedValue({
        targetPath: bookDetailWithOnlyRatingDiffering.filePath,
        exists: false,
      });

      renderPanel();
      fireEvent.click(await screen.findByRole("button", { name: /apply selected/i }));

      await waitFor(() => expect(metadataRefreshApi.applyPending).toHaveBeenCalledTimes(1));
      expect(screen.queryByText("Duplicate file at target location")).not.toBeInTheDocument();
    });

    it("still applies (and lets the server decide) when the preview itself fails", async () => {
      setUpRatingDiff();
      vi.mocked(metadataRefreshApi.checkApplyTarget).mockRejectedValue(new Error("boom"));

      renderPanel();
      fireEvent.click(await screen.findByRole("button", { name: /apply selected/i }));

      await waitFor(() => expect(metadataRefreshApi.applyPending).toHaveBeenCalledTimes(1));
      expect(metadataRefreshApi.applyPending).toHaveBeenCalledWith(
        42,
        ["Rating"],
        false,
        undefined,
        false,
      );
    });
  });

  describe("series", () => {
    const bookInMain: AudiobookDetail = {
      ...bookDetailWithOnlyRatingDiffering,
      series: "Main",
      seriesPart: "1",
      additionalSeries: [],
    };

    const pendingWithSeries = (
      series: { seriesName: string; seriesPart?: string }[],
    ): PendingMetadataRefresh => ({
      ...pendingWithOnlyRatingDiffering,
      payload: {
        ...pendingWithOnlyRatingDiffering.payload,
        rating: "4.0",
        seriesName: series[0]?.seriesName,
        seriesPart: series[0]?.seriesPart,
        series,
      },
    });

    it("offers no change when the source lists the book's series in a different order", async () => {
      vi.mocked(browseApi.getAudiobookDetail).mockResolvedValue({
        ...bookInMain,
        additionalSeries: [{ seriesName: "Spinoff", seriesPart: "3" }],
      });
      vi.mocked(metadataRefreshApi.getPendingForAudiobook).mockResolvedValue(
        pendingWithSeries([
          { seriesName: "Spinoff", seriesPart: "3" },
          { seriesName: "Main", seriesPart: "1" },
        ]),
      );

      renderPanel();

      expect(await screen.findByText(/no applicable pending changes/i)).toBeInTheDocument();
    });

    it("applies the Series field as one field, keeping the current primary unless another is picked", async () => {
      vi.mocked(metadataRefreshApi.applyPending).mockClear();
      vi.mocked(browseApi.getAudiobookDetail).mockResolvedValue(bookInMain);
      vi.mocked(metadataRefreshApi.getPendingForAudiobook).mockResolvedValue(
        pendingWithSeries([
          { seriesName: "Main", seriesPart: "1" },
          { seriesName: "Spinoff", seriesPart: "3" },
        ]),
      );

      renderPanel();

      // The default primary is the book's current one, so the chooser starts on it.
      expect(await screen.findByRole("radio", { name: "Main" })).toBeChecked();
      fireEvent.click(await screen.findByRole("button", { name: /apply all/i }));

      await waitFor(() =>
        expect(metadataRefreshApi.applyPending).toHaveBeenCalledWith(
          42,
          ["Series"],
          false,
          undefined,
          false,
        ),
      );
    });

    it("sends the chosen primary series along with the apply", async () => {
      vi.mocked(metadataRefreshApi.applyPending).mockClear();
      vi.mocked(browseApi.getAudiobookDetail).mockResolvedValue(bookInMain);
      vi.mocked(metadataRefreshApi.getPendingForAudiobook).mockResolvedValue(
        pendingWithSeries([
          { seriesName: "Main", seriesPart: "1" },
          { seriesName: "Spinoff", seriesPart: "3" },
        ]),
      );

      renderPanel();

      fireEvent.click(await screen.findByRole("radio", { name: "Spinoff" }));
      fireEvent.click(await screen.findByRole("button", { name: /apply all/i }));

      await waitFor(() =>
        expect(metadataRefreshApi.applyPending).toHaveBeenCalledWith(
          42,
          ["Series"],
          false,
          "Spinoff",
          false,
        ),
      );
    });

    it("shows no primary chooser when the source reports only one series", async () => {
      vi.mocked(browseApi.getAudiobookDetail).mockResolvedValue(bookInMain);
      vi.mocked(metadataRefreshApi.getPendingForAudiobook).mockResolvedValue(
        pendingWithSeries([{ seriesName: "Main", seriesPart: "2" }]),
      );

      renderPanel();

      await screen.findByRole("button", { name: /apply all/i });
      expect(screen.queryByRole("radio")).not.toBeInTheDocument();
    });
  });
});

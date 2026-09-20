import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor, fireEvent, act } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { AuthorFollowSection } from "./AuthorFollowSection";
import { browseApi } from "@/services/api";
import { queryKeys } from "@/lib/queryKeys";
import { notifications } from "@/lib/notifications";
import type * as ApiModule from "@/services/api";

vi.mock("@/services/api", async (importOriginal) => {
  const actual = await importOriginal<typeof ApiModule>();
  return {
    ...actual,
    browseApi: {
      getAuthorFollowStatus: vi.fn(),
      followAuthor: vi.fn().mockResolvedValue(undefined),
      unfollowAuthor: vi.fn().mockResolvedValue(undefined),
      getAuthorHardcoverMatch: vi.fn(),
      getAuthorHardcoverMatchCandidates: vi.fn(),
      matchAuthorToHardcover: vi.fn().mockResolvedValue({ success: true, lastRefreshedAt: null }),
      unmatchAuthorFromHardcover: vi.fn().mockResolvedValue(undefined),
    },
  };
});

vi.mock("@/lib/notifications", () => ({
  notifications: { success: vi.fn(), error: vi.fn(), info: vi.fn(), warning: vi.fn() },
}));

function renderSection() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={queryClient}>
      <AuthorFollowSection authorId={7} authorName="Brandon Sanderson" />
    </QueryClientProvider>,
  );
}

describe("AuthorFollowSection", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(browseApi.getAuthorFollowStatus).mockResolvedValue({ isFollowed: false });
    vi.mocked(browseApi.getAuthorHardcoverMatch).mockResolvedValue({
      sourceId: null,
      sourceName: null,
      sourceUrl: null,
    });
    vi.mocked(browseApi.getAuthorHardcoverMatchCandidates).mockResolvedValue([]);
    vi.mocked(browseApi.matchAuthorToHardcover).mockResolvedValue({
      success: true,
      lastRefreshedAt: null,
    });
  });

  it("shows Follow and Match to Hardcover when unfollowed and unmatched", async () => {
    renderSection();

    expect(
      await screen.findByRole("button", { name: /follow for upcoming releases/i }),
    ).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /match to hardcover/i })).toBeInTheDocument();
  });

  it("shows Following and the unlink action once matched", async () => {
    vi.mocked(browseApi.getAuthorFollowStatus).mockResolvedValue({ isFollowed: true });
    vi.mocked(browseApi.getAuthorHardcoverMatch).mockResolvedValue({
      sourceId: "123",
      sourceName: "Hardcover",
      sourceUrl: "https://hardcover.app/authors/123",
    });

    renderSection();

    expect(await screen.findByRole("button", { name: /following/i })).toBeInTheDocument();
    expect(
      await screen.findByRole("button", { name: /unlink hardcover match/i }),
    ).toBeInTheDocument();
  });

  it("follows the author when the follow button is clicked", async () => {
    renderSection();

    const button = await screen.findByRole("button", { name: /follow for upcoming releases/i });
    await waitFor(() => expect(button).not.toBeDisabled());
    fireEvent.click(button);

    await waitFor(() => expect(browseApi.followAuthor).toHaveBeenCalledWith(7));
  });

  it("unfollows the author when already following", async () => {
    vi.mocked(browseApi.getAuthorFollowStatus).mockResolvedValue({ isFollowed: true });

    renderSection();

    const button = await screen.findByRole("button", { name: /following/i });
    await waitFor(() => expect(button).not.toBeDisabled());
    fireEvent.click(button);

    await waitFor(() => expect(browseApi.unfollowAuthor).toHaveBeenCalledWith(7));
  });

  it("clears the match when unlink is clicked", async () => {
    vi.mocked(browseApi.getAuthorHardcoverMatch).mockResolvedValue({
      sourceId: "123",
      sourceName: "Hardcover",
      sourceUrl: null,
    });

    renderSection();

    const button = await screen.findByRole("button", { name: /unlink hardcover match/i });
    fireEvent.click(button);

    await waitFor(() => expect(browseApi.unmatchAuthorFromHardcover).toHaveBeenCalledWith(7));
  });

  it("opens the match dialog, requires a minimum query length, and matches a candidate", async () => {
    // The dialog pre-fills the search with the author's own name (>= the minimum length), so
    // candidates are already visible without further typing.
    vi.mocked(browseApi.getAuthorHardcoverMatchCandidates).mockResolvedValue([
      {
        sourceId: "123",
        sourceName: "Hardcover",
        name: "Brandon Sanderson",
        sourceUrl: null,
        bookCount: 40,
      },
    ]);

    renderSection();

    fireEvent.click(await screen.findByRole("button", { name: /match to hardcover/i }));

    expect(await screen.findByText(/match brandon sanderson to hardcover/i)).toBeInTheDocument();

    await waitFor(() =>
      expect(browseApi.getAuthorHardcoverMatchCandidates).toHaveBeenCalledWith(
        7,
        "Brandon Sanderson",
      ),
    );

    const candidateButton = await screen.findByRole("button", { name: /brandon sanderson/i });
    fireEvent.click(candidateButton);

    await waitFor(() =>
      expect(browseApi.matchAuthorToHardcover).toHaveBeenCalledWith(
        7,
        "123",
        "Hardcover",
        undefined,
      ),
    );
  });

  // The backend's MatchAuthor endpoint both persists the match AND refreshes the author's roster
  // (which can take seconds), so a successful match must invalidate the author detail and the
  // upcoming releases - otherwise the freshly-scraped books wait for the next periodic tick.
  it("invalidates the author detail and upcoming-release queries after a successful match", async () => {
    vi.mocked(browseApi.getAuthorHardcoverMatchCandidates).mockResolvedValue([
      {
        sourceId: "123",
        sourceName: "Hardcover",
        name: "Brandon Sanderson",
        sourceUrl: null,
        bookCount: 40,
      },
    ]);

    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    const invalidate = vi.spyOn(queryClient, "invalidateQueries");
    render(
      <QueryClientProvider client={queryClient}>
        <AuthorFollowSection authorId={7} authorName="Brandon Sanderson" />
      </QueryClientProvider>,
    );

    fireEvent.click(await screen.findByRole("button", { name: /match to hardcover/i }));
    const candidateButton = await screen.findByRole("button", { name: /brandon sanderson/i });
    fireEvent.click(candidateButton);

    await waitFor(() => {
      const invalidatedKeys = invalidate.mock.calls.map(([arg]) => arg?.queryKey);
      expect(invalidatedKeys).toEqual(
        expect.arrayContaining([
          queryKeys.author.all(),
          queryKeys.upcomingReleases.all(),
          queryKeys.authorHardcoverMatch(7),
        ]),
      );
    });
  });

  // Regression (review finding): the backend's MatchAuthor is persist-first - it stores the match
  // and THEN refreshes the roster, so a refresh failure AFTER the match was stored (daily budget
  // exhausted, the source cannot resolve the id, no author-capable scraper) comes back as a 200
  // with success=false rather than a rejection. The match is the thing the dialog acted on and it
  // is stored, so the client must still invalidate all three caches and close the dialog - only
  // the notification reports the refresh as pending. The old behavior left the dialog open and
  // the author shown as unmatched until a full reload, and every retry repeated the same refresh
  // failure forever.
  it("treats a refresh-failure response as an accepted match: invalidates, closes, and warns", async () => {
    vi.mocked(browseApi.getAuthorHardcoverMatchCandidates).mockResolvedValue([
      {
        sourceId: "123",
        sourceName: "Hardcover",
        name: "Brandon Sanderson",
        sourceUrl: null,
        bookCount: 40,
      },
    ]);
    vi.mocked(browseApi.matchAuthorToHardcover).mockResolvedValue({
      success: false,
      lastRefreshedAt: null,
    });

    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    const invalidate = vi.spyOn(queryClient, "invalidateQueries");
    render(
      <QueryClientProvider client={queryClient}>
        <AuthorFollowSection authorId={7} authorName="Brandon Sanderson" />
      </QueryClientProvider>,
    );

    fireEvent.click(await screen.findByRole("button", { name: /match to hardcover/i }));
    fireEvent.click(await screen.findByRole("button", { name: /brandon sanderson/i }));

    await waitFor(() => {
      const invalidatedKeys = invalidate.mock.calls.map(([arg]) => arg?.queryKey);
      expect(invalidatedKeys).toEqual(
        expect.arrayContaining([
          queryKeys.author.all(),
          queryKeys.upcomingReleases.all(),
          queryKeys.authorHardcoverMatch(7),
        ]),
      );
    });

    // The dialog closes and the user is told the match saved but the refresh failed - and the
    // plain success toast must NOT fire for an accepted-but-unrefreshed match.
    await waitFor(() =>
      expect(screen.queryByText(/match brandon sanderson to hardcover/i)).not.toBeInTheDocument(),
    );
    expect(notifications.warning).toHaveBeenCalledWith(
      expect.stringMatching(/roster refresh failed/i),
    );
    expect(notifications.success).not.toHaveBeenCalled();
  });

  it("shows a minimum-length hint before searching", async () => {
    vi.mocked(browseApi.getAuthorHardcoverMatchCandidates).mockClear();

    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    render(
      <QueryClientProvider client={queryClient}>
        <AuthorFollowSection authorId={7} authorName="A" />
      </QueryClientProvider>,
    );

    fireEvent.click(await screen.findByRole("button", { name: /match to hardcover/i }));

    expect(await screen.findByText(/type at least 2 characters/i)).toBeInTheDocument();
    expect(browseApi.getAuthorHardcoverMatchCandidates).not.toHaveBeenCalled();
  });

  // Regression guard: the search is meant to be debounced (300ms), not fire one request per
  // keystroke. Without the debounce, each fireEvent.change below would have triggered its own
  // call the moment the query re-rendered.
  it("debounces the Hardcover author search so only the settled query fires", async () => {
    vi.mocked(browseApi.getAuthorHardcoverMatchCandidates).mockResolvedValue([]);

    renderSection();

    fireEvent.click(await screen.findByRole("button", { name: /match to hardcover/i }));
    const input = await screen.findByPlaceholderText(/search hardcover authors/i);

    // Let the dialog's autofilled initial search (debounced from the author's own name) settle
    // before exercising fake timers for the keystroke-driven debounce.
    await waitFor(() =>
      expect(browseApi.getAuthorHardcoverMatchCandidates).toHaveBeenCalledWith(
        7,
        "Brandon Sanderson",
      ),
    );
    vi.mocked(browseApi.getAuthorHardcoverMatchCandidates).mockClear();

    vi.useFakeTimers();
    try {
      fireEvent.change(input, { target: { value: "Sand" } });
      act(() => {
        vi.advanceTimersByTime(100);
      });
      fireEvent.change(input, { target: { value: "Sanderso" } });
      act(() => {
        vi.advanceTimersByTime(100);
      });
      fireEvent.change(input, { target: { value: "Sanderson II" } });
      act(() => {
        vi.advanceTimersByTime(299);
      });

      // Still within the 300ms debounce window of the last keystroke - nothing should fire yet.
      expect(browseApi.getAuthorHardcoverMatchCandidates).not.toHaveBeenCalled();

      act(() => {
        vi.advanceTimersByTime(1);
      });
    } finally {
      vi.useRealTimers();
    }

    await waitFor(() =>
      expect(browseApi.getAuthorHardcoverMatchCandidates).toHaveBeenCalledWith(7, "Sanderson II"),
    );
    expect(browseApi.getAuthorHardcoverMatchCandidates).toHaveBeenCalledTimes(1);
  });
});

import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor, fireEvent } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { AuthorFollowSection } from "./AuthorFollowSection";
import { browseApi } from "@/services/api";
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
      matchAuthorToHardcover: vi.fn().mockResolvedValue(undefined),
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
});

import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor, fireEvent } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import type { ReactNode } from "react";
import { UpcomingReleasesList } from "./UpcomingReleasesList";
import { upcomingReleasesApi } from "@/services/api";
import type * as ApiModule from "@/services/api";
import type { UpcomingRelease } from "@/types/UpcomingRelease";

vi.mock("@tanstack/react-router", () => ({
  Link: ({ children, ...rest }: { children: ReactNode; [key: string]: unknown }) => (
    <a {...rest}>{children}</a>
  ),
}));

vi.mock("@/services/api", async (importOriginal) => {
  const actual = await importOriginal<typeof ApiModule>();
  return {
    ...actual,
    upcomingReleasesApi: {
      getUpcomingReleases: vi.fn(),
      removeUpcomingRelease: vi.fn().mockResolvedValue(undefined),
      dismissRosterUpcomingRelease: vi.fn().mockResolvedValue(undefined),
    },
  };
});

vi.mock("@/lib/notifications", () => ({
  notifications: { success: vi.fn(), error: vi.fn(), info: vi.fn(), warning: vi.fn() },
}));

function release(overrides: Partial<UpcomingRelease> = {}): UpcomingRelease {
  return {
    source: "Legacy",
    id: 1,
    title: "The Stormlight Archive 6",
    releaseDate: "2030-01-01",
    sourceName: "Hardcover",
    authorId: null,
    authorName: null,
    seriesId: null,
    seriesName: null,
    seriesPosition: null,
    sourceUrl: null,
    imageUrl: null,
    ...overrides,
  };
}

function renderList(props: Partial<React.ComponentProps<typeof UpcomingReleasesList>> = {}) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={queryClient}>
      <UpcomingReleasesList {...props} />
    </QueryClientProvider>,
  );
}

describe("UpcomingReleasesList", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it("shows the empty message when there are no releases", async () => {
    vi.mocked(upcomingReleasesApi.getUpcomingReleases).mockResolvedValue({
      count: 0,
      total: 0,
      items: [],
    });

    renderList({ emptyMessage: "Nothing tracked yet." });

    expect(await screen.findByText("Nothing tracked yet.")).toBeInTheDocument();
  });

  it("renders a release's title, date, and series position", async () => {
    vi.mocked(upcomingReleasesApi.getUpcomingReleases).mockResolvedValue({
      count: 1,
      total: 1,
      items: [release({ seriesPosition: "6" })],
    });

    renderList();

    expect(await screen.findByText("The Stormlight Archive 6")).toBeInTheDocument();
    expect(screen.getByText("#6")).toBeInTheDocument();
    expect(screen.getByText("2030-01-01")).toBeInTheDocument();
  });

  it("shows the author/series name only when showSource is set", async () => {
    vi.mocked(upcomingReleasesApi.getUpcomingReleases).mockResolvedValue({
      count: 1,
      total: 1,
      items: [release({ authorId: 7, authorName: "Brandon Sanderson" })],
    });

    renderList({ showSource: false });
    expect(await screen.findByText("The Stormlight Archive 6")).toBeInTheDocument();
    expect(screen.queryByText("Brandon Sanderson")).not.toBeInTheDocument();
  });

  it("shows the author name when showSource is true", async () => {
    vi.mocked(upcomingReleasesApi.getUpcomingReleases).mockResolvedValue({
      count: 1,
      total: 1,
      items: [release({ authorId: 7, authorName: "Brandon Sanderson" })],
    });

    renderList({ showSource: true });
    expect(await screen.findByText("Brandon Sanderson")).toBeInTheDocument();
  });

  // The author-scoped list now carries series books from the author's whole bibliography, so a
  // series book must show which series it belongs to (a link) even without showSource - the
  // author name is implied by the page, the series name is not.
  it("shows a series book's series name and position in an author-scoped list", async () => {
    vi.mocked(upcomingReleasesApi.getUpcomingReleases).mockResolvedValue({
      count: 1,
      total: 1,
      items: [
        release({
          source: "Roster",
          id: null,
          expectedBookId: 5,
          authorId: 7,
          authorName: "Brandon Sanderson",
          seriesId: 9,
          seriesName: "Mistborn",
          seriesPosition: "6",
        }),
      ],
    });

    renderList({ authorId: 7 });
    expect(await screen.findByText("Mistborn")).toBeInTheDocument();
    expect(screen.getByText("#6")).toBeInTheDocument();
    expect(screen.getByText("Mistborn").closest("a")).not.toBeNull();
  });

  it("does not repeat the series name in a series-scoped list (it is the page's own name)", async () => {
    vi.mocked(upcomingReleasesApi.getUpcomingReleases).mockResolvedValue({
      count: 1,
      total: 1,
      items: [release({ source: "Roster", id: null, seriesId: 9, seriesName: "Mistborn" })],
    });

    renderList({ seriesId: 9 });
    expect(await screen.findByText("The Stormlight Archive 6")).toBeInTheDocument();
    expect(screen.queryByText("Mistborn")).not.toBeInTheDocument();
  });

  // Regression guard: a failed fetch must render an error, not silently fall through to
  // emptyMessage - the two states would otherwise be indistinguishable to the user.
  it("shows an error message instead of the empty message when the fetch fails", async () => {
    vi.mocked(upcomingReleasesApi.getUpcomingReleases).mockRejectedValue(
      new Error("Network error"),
    );

    renderList({ emptyMessage: "Nothing tracked yet." });

    expect(await screen.findByText("Network error")).toBeInTheDocument();
    expect(screen.queryByText("Nothing tracked yet.")).not.toBeInTheDocument();
  });

  it("shows the overflow hint when more releases exist than fit on this page", async () => {
    vi.mocked(upcomingReleasesApi.getUpcomingReleases).mockResolvedValue({
      count: 1,
      total: 3,
      items: [release()],
    });

    renderList();

    expect(await screen.findByText("Showing 1 of 3 upcoming releases.")).toBeInTheDocument();
  });

  it("suppresses the overflow hint when showOverflowHint is false", async () => {
    vi.mocked(upcomingReleasesApi.getUpcomingReleases).mockResolvedValue({
      count: 1,
      total: 3,
      items: [release()],
    });

    renderList({ showOverflowHint: false });

    expect(await screen.findByText("The Stormlight Archive 6")).toBeInTheDocument();
    expect(screen.queryByText(/showing 1 of 3/i)).not.toBeInTheDocument();
  });

  it("does not show the overflow hint when everything fits on this page", async () => {
    vi.mocked(upcomingReleasesApi.getUpcomingReleases).mockResolvedValue({
      count: 1,
      total: 1,
      items: [release()],
    });

    renderList();

    expect(await screen.findByText("The Stormlight Archive 6")).toBeInTheDocument();
    expect(screen.queryByText(/showing 1 of 1/i)).not.toBeInTheDocument();
  });

  it("removes a release and invalidates the list when the remove button is clicked", async () => {
    vi.mocked(upcomingReleasesApi.getUpcomingReleases).mockResolvedValue({
      count: 1,
      total: 1,
      items: [release()],
    });

    renderList();

    const removeButton = await screen.findByTitle("Remove from upcoming releases");
    fireEvent.click(removeButton);

    await waitFor(() => expect(upcomingReleasesApi.removeUpcomingRelease).toHaveBeenCalledWith(1));
  });

  // "Roster" rows have no backing UpcomingRelease row - removing one dismisses the roster entry
  // instead, through the series/author expected-book ignore mechanism (see
  // AudiobookManager/UPCOMING_RELEASES_DESIGN.md).
  it("dismisses a series-sourced roster release by series name and position", async () => {
    vi.mocked(upcomingReleasesApi.getUpcomingReleases).mockResolvedValue({
      count: 1,
      total: 1,
      items: [
        release({
          source: "Roster",
          id: null,
          seriesName: "Mistborn",
          seriesPosition: "5",
          title: "The Lost Metal",
        }),
      ],
    });

    renderList();

    const removeButton = await screen.findByTitle("Remove from upcoming releases");
    fireEvent.click(removeButton);

    await waitFor(() => {
      expect(upcomingReleasesApi.dismissRosterUpcomingRelease).toHaveBeenCalledWith({
        seriesName: "Mistborn",
        seriesPosition: "5",
        title: "The Lost Metal",
      });
    });
    expect(upcomingReleasesApi.removeUpcomingRelease).not.toHaveBeenCalled();
  });

  it("dismisses an author-sourced roster release by author id", async () => {
    vi.mocked(upcomingReleasesApi.getUpcomingReleases).mockResolvedValue({
      count: 1,
      total: 1,
      items: [
        release({
          source: "Roster",
          id: null,
          seriesName: null,
          authorId: 9,
          authorName: "Brandon Sanderson",
          title: "Standalone Novella",
        }),
      ],
    });

    renderList();

    const removeButton = await screen.findByTitle("Remove from upcoming releases");
    fireEvent.click(removeButton);

    await waitFor(() => {
      expect(upcomingReleasesApi.dismissRosterUpcomingRelease).toHaveBeenCalledWith({
        authorId: 9,
        title: "Standalone Novella",
      });
    });
  });

  // The dismiss endpoint addresses a roster item by its most specific identity first: the stable
  // unified expected-book id a "Roster" row always carries (AudiobookManager/UPCOMING_RELEASES_
  // DESIGN.md). A row that carries one must never fall back to the title path - a person with two
  // same-titled entries could otherwise dismiss the wrong book.
  it("dismisses a roster release by its stable expected-book id when present", async () => {
    vi.mocked(upcomingReleasesApi.getUpcomingReleases).mockResolvedValue({
      count: 1,
      total: 1,
      items: [
        release({
          source: "Roster",
          id: null,
          expectedBookId: 77,
          seriesName: "Mistborn",
          seriesPosition: "5",
          title: "The Lost Metal",
        }),
      ],
    });

    renderList();

    const removeButton = await screen.findByTitle("Remove from upcoming releases");
    fireEvent.click(removeButton);

    await waitFor(() => {
      expect(upcomingReleasesApi.dismissRosterUpcomingRelease).toHaveBeenCalledWith({
        expectedBookId: 77,
      });
    });
    expect(upcomingReleasesApi.removeUpcomingRelease).not.toHaveBeenCalled();
  });

  // A roster row without an expected-book id (a legacy-copied row) is addressed by its source
  // identity (source book id + source name) before the title path.
  it("dismisses a roster release by its source book id when no expected-book id is present", async () => {
    vi.mocked(upcomingReleasesApi.getUpcomingReleases).mockResolvedValue({
      count: 1,
      total: 1,
      items: [
        release({
          source: "Roster",
          id: null,
          expectedBookId: null,
          sourceName: "Hardcover",
          sourceBookId: "abc-123",
          seriesName: "Mistborn",
          title: "The Lost Metal",
        }),
      ],
    });

    renderList();

    const removeButton = await screen.findByTitle("Remove from upcoming releases");
    fireEvent.click(removeButton);

    await waitFor(() => {
      expect(upcomingReleasesApi.dismissRosterUpcomingRelease).toHaveBeenCalledWith({
        sourceName: "Hardcover",
        sourceBookId: "abc-123",
      });
    });
  });

  // A roster row can have a Year but no precise ReleaseDate yet; the bare year is shown instead
  // of an empty date.
  it("falls back to the year when a roster row has no precise release date", async () => {
    vi.mocked(upcomingReleasesApi.getUpcomingReleases).mockResolvedValue({
      count: 1,
      total: 1,
      items: [release({ source: "Roster", id: null, releaseDate: null, year: 2027 })],
    });

    renderList();

    expect(await screen.findByText("2027")).toBeInTheDocument();
  });
});

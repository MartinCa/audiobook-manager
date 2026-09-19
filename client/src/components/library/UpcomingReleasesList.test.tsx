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
    },
  };
});

vi.mock("@/lib/notifications", () => ({
  notifications: { success: vi.fn(), error: vi.fn(), info: vi.fn(), warning: vi.fn() },
}));

function release(overrides: Partial<UpcomingRelease> = {}): UpcomingRelease {
  return {
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
});

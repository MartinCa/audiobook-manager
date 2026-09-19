import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor, fireEvent } from "@testing-library/react";
import { QueryClientProvider } from "@tanstack/react-query";
import type { ReactNode } from "react";
import { UpcomingReleasesPage } from "./UpcomingReleasesPage";
import { operationsApi, upcomingReleasesApi } from "@/services/api";
import { notifications } from "@/lib/notifications";
import { createQueryClient } from "@/lib/query";
import type * as ApiModule from "@/services/api";
import type { UpcomingRelease } from "@/types/UpcomingRelease";

vi.mock("@tanstack/react-router", () => ({
  useNavigate: () => vi.fn(),
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
      refreshUpcomingReleases: vi.fn().mockResolvedValue(undefined),
    },
    operationsApi: {
      getStatus: vi.fn(),
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

function renderPage() {
  // Mirrors the app's real QueryClient (including its 30s staleTime) rather than a fresh
  // QueryClient with defaults - the latter's staleTime: 0 would hide the fetchQuery-returns-a-
  // stale-cached-value bug this file regression-tests below.
  const queryClient = createQueryClient();
  return render(
    <QueryClientProvider client={queryClient}>
      <UpcomingReleasesPage />
    </QueryClientProvider>,
  );
}

describe("UpcomingReleasesPage", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(operationsApi.getStatus).mockResolvedValue({
      isRunning: false,
      processed: 0,
      total: 0,
    });
  });

  it("shows the total release count in the heading", async () => {
    vi.mocked(upcomingReleasesApi.getUpcomingReleases).mockResolvedValue({
      count: 1,
      total: 1,
      items: [release()],
    });

    renderPage();

    expect(await screen.findByText("Upcoming Releases (1)")).toBeInTheDocument();
  });

  it("starts a refresh when Check Now is clicked", async () => {
    vi.mocked(upcomingReleasesApi.getUpcomingReleases).mockResolvedValue({
      count: 0,
      total: 0,
      items: [],
    });

    renderPage();

    const button = await screen.findByRole("button", { name: /check now/i });
    button.click();

    await waitFor(() => expect(upcomingReleasesApi.refreshUpcomingReleases).toHaveBeenCalled());
  });

  it("disables Check Now and shows Checking... while a refresh is running", async () => {
    vi.mocked(upcomingReleasesApi.getUpcomingReleases).mockResolvedValue({
      count: 0,
      total: 0,
      items: [],
    });
    vi.mocked(operationsApi.getStatus).mockResolvedValue({
      isRunning: true,
      processed: 2,
      total: 5,
    });

    renderPage();

    const button = await screen.findByRole("button", { name: /checking/i });
    expect(button).toBeDisabled();
  });

  it("shows a pager only when more than one page of releases exists", async () => {
    vi.mocked(upcomingReleasesApi.getUpcomingReleases).mockResolvedValue({
      count: 50,
      total: 120,
      items: Array.from({ length: 50 }, (_, i) => release({ id: i + 1 })),
    });

    renderPage();

    expect(await screen.findByRole("button", { name: /next/i })).toBeInTheDocument();
    expect(screen.getByText(/showing 1–50 of 120/i)).toBeInTheDocument();
  });

  it("does not show a pager when everything fits on one page", async () => {
    vi.mocked(upcomingReleasesApi.getUpcomingReleases).mockResolvedValue({
      count: 1,
      total: 1,
      items: [release()],
    });

    renderPage();

    expect(await screen.findByText("The Stormlight Archive 6")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /next/i })).not.toBeInTheDocument();
  });

  // Regression guard: the completion effect fires on the running -> not-running transition of
  // the polled status, not just once on mount - a refresh that goes from not-running to running
  // to not-running again must notify and refresh the list exactly once, when it actually finishes.
  it("shows a success notification and refreshes the list once a refresh finishes", async () => {
    vi.mocked(upcomingReleasesApi.getUpcomingReleases).mockResolvedValue({
      count: 0,
      total: 0,
      items: [],
    });
    vi.mocked(operationsApi.getStatus)
      .mockResolvedValueOnce({ isRunning: false, processed: 0, total: 0 })
      .mockResolvedValueOnce({ isRunning: true, processed: 1, total: 5 })
      .mockResolvedValue({ isRunning: false, processed: 5, total: 5 });

    renderPage();

    const button = await screen.findByRole("button", { name: /check now/i });
    fireEvent.click(button);

    await screen.findByRole("button", { name: /checking/i });
    const callsBeforeFinish = vi.mocked(upcomingReleasesApi.getUpcomingReleases).mock.calls.length;

    await waitFor(
      () =>
        expect(notifications.success).toHaveBeenCalledWith(
          "Checked followed authors and series for new releases",
        ),
      { timeout: 3000 },
    );

    await waitFor(() =>
      expect(vi.mocked(upcomingReleasesApi.getUpcomingReleases).mock.calls.length).toBeGreaterThan(
        callsBeforeFinish,
      ),
    );
    expect(notifications.success).toHaveBeenCalledTimes(1);
  }, 10000);

  // Regression guard: the sweep can start and finish between the POST and this tab's next status
  // poll, so the running -> not-running transition is never observed (every poll sees
  // isRunning: false). A refresh this tab itself triggered must still be recognized as completed
  // instead of silently never notifying.
  it("shows a success notification even when the refresh finishes before any poll observes it running", async () => {
    vi.mocked(upcomingReleasesApi.getUpcomingReleases).mockResolvedValue({
      count: 0,
      total: 0,
      items: [],
    });
    // Every poll - before and after the triggered refresh - reports not-running.
    vi.mocked(operationsApi.getStatus).mockResolvedValue({
      isRunning: false,
      processed: 0,
      total: 0,
    });

    renderPage();

    const button = await screen.findByRole("button", { name: /check now/i });
    fireEvent.click(button);

    await waitFor(() => expect(upcomingReleasesApi.refreshUpcomingReleases).toHaveBeenCalled());
    await waitFor(() =>
      expect(notifications.success).toHaveBeenCalledWith(
        "Checked followed authors and series for new releases",
      ),
    );
    expect(notifications.success).toHaveBeenCalledTimes(1);
  });
});

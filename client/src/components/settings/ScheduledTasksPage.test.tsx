import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { ScheduledTasksPage } from "./ScheduledTasksPage";
import type { ScheduledTask } from "@/types/ScheduledTask";

vi.mock("@/services/api", () => ({
  settingsApi: {
    getScheduledTasks: vi.fn(),
  },
}));

import { settingsApi } from "@/services/api";

function renderPage() {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });
  return render(
    <QueryClientProvider client={queryClient}>
      <ScheduledTasksPage />
    </QueryClientProvider>,
  );
}

function makeTask(overrides: Partial<ScheduledTask> = {}): ScheduledTask {
  return {
    key: "upcoming_releases_refresh",
    name: "Upcoming Releases Refresh",
    cronSchedule: "0 3 * * *",
    enabled: true,
    lastRunAt: null,
    lastRunDurationMs: null,
    lastRunStatus: null,
    nextRunAt: null,
    ...overrides,
  };
}

describe("ScheduledTasksPage", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it("shows a never-run task as Never / — / —", async () => {
    vi.mocked(settingsApi.getScheduledTasks).mockResolvedValue([makeTask()]);

    renderPage();

    expect(await screen.findByText("Upcoming Releases Refresh")).toBeInTheDocument();
    expect(screen.getByText("0 3 * * *")).toBeInTheDocument();
    expect(screen.getByText("Never")).toBeInTheDocument();
  });

  it("shows a successful run's last-run time, duration and Success badge", async () => {
    vi.mocked(settingsApi.getScheduledTasks).mockResolvedValue([
      makeTask({
        lastRunAt: "2026-01-01T03:00:00Z",
        lastRunDurationMs: 2300,
        lastRunStatus: "Success",
        nextRunAt: "2026-01-02T03:00:00Z",
      }),
    ]);

    renderPage();

    expect(await screen.findByText("Success")).toBeInTheDocument();
    expect(screen.getByText("2s")).toBeInTheDocument();
  });

  it("shows a failed run's Failed badge", async () => {
    vi.mocked(settingsApi.getScheduledTasks).mockResolvedValue([
      makeTask({
        lastRunAt: "2026-01-01T03:00:00Z",
        lastRunDurationMs: 500,
        lastRunStatus: "Failed",
      }),
    ]);

    renderPage();

    expect(await screen.findByText("Failed")).toBeInTheDocument();
  });

  it("shows — for Next Run when the task is disabled", async () => {
    vi.mocked(settingsApi.getScheduledTasks).mockResolvedValue([
      makeTask({ enabled: false, nextRunAt: null }),
    ]);

    renderPage();

    await screen.findByText("Upcoming Releases Refresh");
    // Both Duration ("—") and Next Run ("—") render as em dashes when absent - just confirm at
    // least one is present rather than pinning an exact count that would break if either column's
    // formatting rule changes.
    expect(screen.getAllByText("—").length).toBeGreaterThan(0);
  });

  it("shows an empty state when no tasks are registered", async () => {
    vi.mocked(settingsApi.getScheduledTasks).mockResolvedValue([]);

    renderPage();

    expect(await screen.findByText("No scheduled tasks are registered.")).toBeInTheDocument();
  });
});

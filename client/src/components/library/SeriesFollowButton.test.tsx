import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor, fireEvent } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { SeriesFollowButton } from "./SeriesFollowButton";
import { seriesApi } from "@/services/api";
import type * as ApiModule from "@/services/api";

vi.mock("@/services/api", async (importOriginal) => {
  const actual = await importOriginal<typeof ApiModule>();
  return {
    ...actual,
    seriesApi: {
      getFollowStatus: vi.fn(),
      followSeries: vi.fn().mockResolvedValue(undefined),
      unfollowSeries: vi.fn().mockResolvedValue(undefined),
    },
  };
});

vi.mock("@/lib/notifications", () => ({
  notifications: { success: vi.fn(), error: vi.fn(), info: vi.fn(), warning: vi.fn() },
}));

function renderButton(onChanged?: () => void) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={queryClient}>
      <SeriesFollowButton seriesName="Mistborn" onChanged={onChanged} />
    </QueryClientProvider>,
  );
}

describe("SeriesFollowButton", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it("shows a Follow button when the series is not followed", async () => {
    vi.mocked(seriesApi.getFollowStatus).mockResolvedValue({ isFollowed: false });

    renderButton();

    expect(
      await screen.findByRole("button", { name: /follow for upcoming releases/i }),
    ).toBeInTheDocument();
  });

  it("shows Following when the series is already followed", async () => {
    vi.mocked(seriesApi.getFollowStatus).mockResolvedValue({ isFollowed: true });

    renderButton();

    expect(await screen.findByRole("button", { name: /following/i })).toBeInTheDocument();
  });

  it("calls followSeries and onChanged when clicked while unfollowed", async () => {
    vi.mocked(seriesApi.getFollowStatus).mockResolvedValue({ isFollowed: false });
    const onChanged = vi.fn();

    renderButton(onChanged);

    const button = await screen.findByRole("button", { name: /follow for upcoming releases/i });
    // The button's text matches this disabled-while-loading state too (the default before the
    // follow-status query resolves is "not followed"), so it must wait for the query to settle
    // and re-enable the button, or the click below silently no-ops.
    await waitFor(() => expect(button).not.toBeDisabled());
    fireEvent.click(button);

    await waitFor(() => expect(seriesApi.followSeries).toHaveBeenCalledWith("Mistborn"));
    await waitFor(() => expect(onChanged).toHaveBeenCalled());
    expect(seriesApi.unfollowSeries).not.toHaveBeenCalled();
  });

  it("calls unfollowSeries when clicked while followed", async () => {
    vi.mocked(seriesApi.getFollowStatus).mockResolvedValue({ isFollowed: true });

    renderButton();

    const button = await screen.findByRole("button", { name: /following/i });
    await waitFor(() => expect(button).not.toBeDisabled());
    fireEvent.click(button);

    await waitFor(() => expect(seriesApi.unfollowSeries).toHaveBeenCalledWith("Mistborn"));
    expect(seriesApi.followSeries).not.toHaveBeenCalled();
  });
});

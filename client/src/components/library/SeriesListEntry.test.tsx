import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import type { ReactNode } from "react";
import { SeriesListEntry } from "./SeriesListEntry";
import type { SeriesOverview } from "@/types/Series";

const mockLinkCalls: Array<{
  to: string;
  params?: { seriesName?: string };
  search?: { authorId?: number };
}> = [];

vi.mock("@tanstack/react-router", () => ({
  Link: ({
    to,
    params,
    search,
    children,
  }: {
    to: string;
    params?: { seriesName?: string };
    search?: { authorId?: number };
    children: ReactNode;
  }) => {
    mockLinkCalls.push({ to, params, search });
    return <a href={`/library/series/${params?.seriesName}`}>{children}</a>;
  },
}));

function series(overrides: Partial<SeriesOverview> = {}): SeriesOverview {
  return {
    id: 1,
    name: "Mistborn",
    authors: ["Brandon Sanderson"],
    ownedBookCount: 3,
    isMatched: false,
    matchedSourceName: null,
    matchedSourceId: null,
    matchedSourceUrl: null,
    matchConfidence: null,
    lastRefreshedAt: null,
    expectedBookCount: 0,
    missingBookCount: 0,
    ignoredBookCount: 0,
    includeOmnibusEditions: false,
    ...overrides,
  };
}

describe("SeriesListEntry", () => {
  beforeEach(() => {
    mockLinkCalls.splice(0);
  });

  it("renders a matched series with its source badge, confidence, owned line and missing badge", () => {
    render(
      <SeriesListEntry
        series={series({
          isMatched: true,
          matchedSourceName: "Hardcover",
          matchConfidence: 0.75,
          ownedBookCount: 3,
          missingBookCount: 2,
        })}
      />,
    );

    expect(screen.getByText("Mistborn")).toBeInTheDocument();
    expect(screen.getByText(/Hardcover/)).toBeInTheDocument();
    expect(screen.getByText("(75%)")).toBeInTheDocument();
    expect(screen.getByText("By Brandon Sanderson ·")).toBeInTheDocument();
    expect(screen.getByText("3 books owned")).toBeInTheDocument();
    expect(screen.getByText("· 2 missing")).toBeInTheDocument();
    expect(screen.getByText("2 missing")).toBeInTheDocument();
  });

  it("renders an unmatched series with the Unmatched badge and no missing indication", () => {
    render(<SeriesListEntry series={series({ isMatched: false, missingBookCount: 0 })} />);

    expect(screen.getByText("Mistborn")).toBeInTheDocument();
    expect(screen.getByText("Unmatched")).toBeInTheDocument();
    expect(screen.queryByText(/Hardcover/)).not.toBeInTheDocument();
    expect(screen.queryByText(/missing/)).not.toBeInTheDocument();
  });

  it("shows the singular owned book line for a one-book series", () => {
    render(<SeriesListEntry series={series({ ownedBookCount: 1 })} />);

    expect(screen.getByText("1 book owned")).toBeInTheDocument();
  });

  it("links to the series detail and forwards the search prop", () => {
    const search = { authorId: 7 };
    render(<SeriesListEntry series={series()} search={search} />);

    const link = screen.getByText("Mistborn").closest("a");
    expect(link).toHaveAttribute("href", "/library/series/Mistborn");
    expect(mockLinkCalls[0]).toEqual({
      to: "/library/series/$seriesName",
      params: { seriesName: "Mistborn" },
      search,
    });
  });

  it("does not forward a search prop when the caller passes none", () => {
    render(<SeriesListEntry series={series()} />);

    expect(mockLinkCalls[0]?.search).toBeUndefined();
  });
});

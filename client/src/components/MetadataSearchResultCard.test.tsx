import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { MetadataSearchResultCard } from "./MetadataSearchResultCard";
import type { MetadataSearchResult } from "@/types/MetadataSearchResult";

vi.mock("@/services/api", () => ({
  metadataSearchApi: {
    getProxyImageUrl: vi.fn((url: string) => `/proxy?url=${encodeURIComponent(url)}`),
  },
}));

const baseResult: MetadataSearchResult = {
  url: "https://www.audible.com/listen/1",
  cleanUrl: "https://www.audible.com/listen/1",
  source: "Audible",
  authors: [{ name: "Charlie Mackesy" }],
  narrators: [{ name: "Charlie Mackesy" }],
  bookName: "The Boy, the Mole, the Fox and the Horse",
  duration: "10 hrs and 23 mins",
  year: 2020,
  language: "English",
  series: [],
  genres: [],
};

describe("MetadataSearchResultCard", () => {
  it("renders the book name, source badge, year and duration", () => {
    render(<MetadataSearchResultCard result={baseResult} />);

    expect(screen.getByText("The Boy, the Mole, the Fox and the Horse")).toBeInTheDocument();
    expect(screen.getByText("Audible")).toBeInTheDocument();
    expect(screen.getByText("(2020)")).toBeInTheDocument();
    expect(screen.getByText(/10 hrs and 23 mins/)).toBeInTheDocument();
  });

  it("renders authors and narrators when present", () => {
    render(<MetadataSearchResultCard result={baseResult} />);

    expect(screen.getByText("Charlie Mackesy")).toBeInTheDocument();
    expect(screen.getByText(/Narrated by/)).toBeInTheDocument();
  });

  it("omits the authors/narrators lines when absent", () => {
    render(<MetadataSearchResultCard result={{ ...baseResult, authors: [], narrators: [] }} />);

    expect(screen.queryByText(/^By:/)).not.toBeInTheDocument();
    expect(screen.queryByText(/Narrated by/)).not.toBeInTheDocument();
  });

  it("renders the series name and part when present", () => {
    render(
      <MetadataSearchResultCard
        result={{ ...baseResult, series: [{ seriesName: "Mistborn", seriesPart: "1" }] }}
      />,
    );

    expect(screen.getByText(/Mistborn/)).toBeInTheDocument();
    expect(screen.getByText(/#1/)).toBeInTheDocument();
  });

  it("renders a View Source link to the result's url", () => {
    render(<MetadataSearchResultCard result={baseResult} />);

    const link = screen.getByRole("link", { name: /View Source/i });
    expect(link).toHaveAttribute("href", baseResult.url);
    expect(link).toHaveAttribute("target", "_blank");
  });

  it("omits the View Source link when the result has no url", () => {
    render(<MetadataSearchResultCard result={{ ...baseResult, url: "" }} />);

    expect(screen.queryByRole("link", { name: /View Source/i })).not.toBeInTheDocument();
  });

  it("renders the caller's action slot", () => {
    render(
      <MetadataSearchResultCard
        result={baseResult}
        actions={<button type="button">Select this match</button>}
      />,
    );

    expect(screen.getByRole("button", { name: "Select this match" })).toBeInTheDocument();
  });

  it("renders a cover image using the proxy-image url when imageUrl is present", () => {
    render(
      <MetadataSearchResultCard
        result={{ ...baseResult, imageUrl: "https://example.com/cover.jpg" }}
      />,
    );

    const img = screen.getByRole("img", { name: baseResult.bookName });
    expect(img).toHaveAttribute(
      "src",
      "/proxy?url=" + encodeURIComponent("https://example.com/cover.jpg"),
    );
  });

  it("renders no image when imageUrl is absent", () => {
    render(<MetadataSearchResultCard result={baseResult} />);

    expect(screen.queryByRole("img")).not.toBeInTheDocument();
  });

  it("renders the average rating and the grouped review count", () => {
    render(
      <MetadataSearchResultCard
        result={{ ...baseResult, rating: 4.2367, numberOfRatings: 12345 }}
      />,
    );

    expect(screen.getByTestId("result-rating")).toHaveTextContent("4.24 · 12,345 ratings");
  });

  it("uses the singular for a single rating", () => {
    render(<MetadataSearchResultCard result={{ ...baseResult, rating: 5, numberOfRatings: 1 }} />);

    expect(screen.getByTestId("result-rating")).toHaveTextContent("5 · 1 rating");
  });

  it("still shows the review count when the source reports no average", () => {
    render(<MetadataSearchResultCard result={{ ...baseResult, numberOfRatings: 87 }} />);

    expect(screen.getByTestId("result-rating")).toHaveTextContent("87 ratings");
  });

  it("omits the rating line when the source reports neither value", () => {
    render(<MetadataSearchResultCard result={baseResult} />);

    expect(screen.queryByTestId("result-rating")).not.toBeInTheDocument();
  });
});

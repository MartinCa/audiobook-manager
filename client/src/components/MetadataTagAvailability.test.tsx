import { describe, it, expect } from "vitest";
import { render, screen } from "@testing-library/react";
import { MetadataTagAvailability } from "./MetadataTagAvailability";

describe("MetadataTagAvailability", () => {
  it("shows a tag the source reported as present in the ok colour with an 'available' label", () => {
    render(<MetadataTagAvailability hasDescription={true} hasGenres={true} />);

    const description = screen.getByTestId("tag-description");
    expect(description).toHaveAttribute("data-state", "present");
    expect(description).toHaveAttribute("aria-label", "Description: available");
    expect(description).toHaveClass("text-status-ok");
    expect(screen.getByTestId("tag-genres")).toHaveAttribute("aria-label", "Genres: available");
  });

  it("shows a tag the source reported as missing in the error colour", () => {
    render(<MetadataTagAvailability hasDescription={false} hasGenres={false} />);

    const genres = screen.getByTestId("tag-genres");
    expect(genres).toHaveAttribute("data-state", "missing");
    expect(genres).toHaveAttribute("aria-label", "Genres: not provided by the source");
    expect(genres).toHaveClass("text-status-error");
    expect(screen.getByTestId("tag-description")).toHaveClass("text-status-error");
  });

  it("shows a tag the search response does not mention as unknown, for both null and undefined", () => {
    render(<MetadataTagAvailability hasDescription={null} />);

    const description = screen.getByTestId("tag-description");
    expect(description).toHaveAttribute("data-state", "unknown");
    expect(description).toHaveAttribute(
      "aria-label",
      "Description: not included in search results",
    );
    expect(description).toHaveClass("text-status-unknown");
    expect(screen.getByTestId("tag-genres")).toHaveAttribute("data-state", "unknown");
  });

  it("treats each tag independently", () => {
    render(<MetadataTagAvailability hasDescription={true} hasGenres={false} />);

    expect(screen.getByTestId("tag-description")).toHaveAttribute("data-state", "present");
    expect(screen.getByTestId("tag-genres")).toHaveAttribute("data-state", "missing");
  });
});

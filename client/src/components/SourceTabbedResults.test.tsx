import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { SourceTabbedResults } from "./SourceTabbedResults";
import type { MetadataSearchResult } from "@/types/MetadataSearchResult";

vi.mock("@/services/api", () => ({
  metadataSearchApi: { getProxyImageUrl: (url: string) => url },
}));

const make = (source: string, bookName: string): MetadataSearchResult => ({
  url: `https://example.com/${bookName}`,
  cleanUrl: `https://example.com/${bookName}`,
  source,
  authors: [],
  narrators: [],
  bookName,
  series: [],
  genres: [],
});

const results = [
  make("Audible", "Audible One"),
  make("Audible", "Audible Two"),
  make("Hardcover", "Hardcover One"),
];

function renderTabs(sources: string[], onPick = vi.fn()) {
  return render(
    <SourceTabbedResults
      results={results}
      sources={sources}
      renderActions={(r, idx) => (
        <button
          onClick={() => {
            onPick(r.bookName, idx);
          }}
        >
          Pick {r.bookName}
        </button>
      )}
    />,
  );
}

describe("SourceTabbedResults", () => {
  it("shows one tab per source labelled with its result count, in the given order", () => {
    renderTabs(["Hardcover", "Audible", "Goodreads"]);
    expect(screen.getAllByRole("tab").map((t) => t.textContent)).toEqual([
      "Hardcover (1)",
      "Audible (2)",
      "Goodreads (0)",
    ]);
  });

  it("disables a source with no results", () => {
    renderTabs(["Audible", "Goodreads"]);
    expect(screen.getByRole("tab", { name: "Goodreads (0)" })).toHaveAttribute(
      "aria-disabled",
      "true",
    );
  });

  it("opens on the first source that has results and shows only its cards", () => {
    renderTabs(["Goodreads", "Audible", "Hardcover"]);
    expect(screen.getByRole("tab", { name: "Audible (2)" })).toHaveAttribute(
      "aria-selected",
      "true",
    );
    expect(screen.getByText("Audible One")).toBeInTheDocument();
    expect(screen.queryByText("Hardcover One")).not.toBeInTheDocument();
  });

  it("switches panels when another tab is clicked", async () => {
    const user = userEvent.setup();
    renderTabs(["Audible", "Hardcover"]);
    await user.click(screen.getByRole("tab", { name: "Hardcover (1)" }));
    expect(screen.getByText("Hardcover One")).toBeInTheDocument();
    expect(screen.queryByText("Audible One")).not.toBeInTheDocument();
  });

  it("hands actions the result's index in the original flat list, not its position in the tab", async () => {
    const user = userEvent.setup();
    const onPick = vi.fn();
    renderTabs(["Audible", "Hardcover"], onPick);
    await user.click(screen.getByRole("tab", { name: "Hardcover (1)" }));
    await user.click(screen.getByRole("button", { name: "Pick Hardcover One" }));
    expect(onPick).toHaveBeenCalledWith("Hardcover One", 2);
  });

  it("appends a source that only appears in the results", () => {
    renderTabs(["Audible"]);
    expect(screen.getAllByRole("tab").map((t) => t.textContent)).toEqual([
      "Audible (2)",
      "Hardcover (1)",
    ]);
  });

  it("renders a plain list without tabs when there is a single source", () => {
    render(
      <SourceTabbedResults
        results={results.slice(0, 1)}
        sources={["Audible"]}
        renderActions={() => null}
      />,
    );
    expect(screen.queryByRole("tab")).not.toBeInTheDocument();
    expect(screen.getByText("Audible One")).toBeInTheDocument();
  });

  it("opens on the first source with results again when a new result set arrives", async () => {
    const user = userEvent.setup();
    const actions = () => null;
    const { rerender } = render(
      <SourceTabbedResults
        results={results}
        sources={["Audible", "Hardcover"]}
        renderActions={actions}
      />,
    );
    await user.click(screen.getByRole("tab", { name: "Hardcover (1)" }));
    rerender(
      <SourceTabbedResults
        results={[...results]}
        sources={["Audible", "Hardcover"]}
        renderActions={actions}
      />,
    );
    expect(screen.getByRole("tab", { name: "Audible (2)" })).toHaveAttribute(
      "aria-selected",
      "true",
    );
  });

  it("groups a result with an empty source under Unknown", () => {
    render(
      <SourceTabbedResults
        results={[make("", "No Source"), make("Audible", "A")]}
        sources={["Audible"]}
        renderActions={() => null}
      />,
    );
    expect(screen.getAllByRole("tab").map((t) => t.textContent)).toEqual([
      "Audible (1)",
      "Unknown (1)",
    ]);
  });

  it("labels a source whose search failed as failed, not as having zero results", () => {
    render(
      <SourceTabbedResults
        results={results}
        sources={["Audible", "Goodreads"]}
        failedSources={{ Goodreads: "timed out" }}
        renderActions={() => null}
      />,
    );
    const tab = screen.getByRole("tab", { name: "Goodreads (failed)" });
    expect(tab).toHaveAttribute("title", "timed out");
    expect(tab).toHaveAttribute("aria-disabled", "true");
  });

  it("drops the per-card source badge when tabs already name the source, keeps it for a single source", () => {
    const { unmount } = renderTabs(["Audible", "Hardcover"]);
    expect(
      screen.queryByText("Audible", { selector: "[data-slot=badge]" }),
    ).not.toBeInTheDocument();
    unmount();
    render(
      <SourceTabbedResults
        results={results.slice(0, 1)}
        sources={["Audible"]}
        renderActions={() => null}
      />,
    );
    expect(screen.getByText("Audible", { selector: "[data-slot=badge]" })).toBeInTheDocument();
  });

  it("opens on the first source with results again when only the source list changes", async () => {
    const user = userEvent.setup();
    const actions = () => null;
    const { rerender } = render(
      <SourceTabbedResults
        results={results}
        sources={["Audible", "Hardcover"]}
        renderActions={actions}
      />,
    );
    await user.click(screen.getByRole("tab", { name: "Hardcover (1)" }));
    rerender(
      <SourceTabbedResults
        results={results}
        sources={["Audible", "Hardcover", "Goodreads"]}
        renderActions={actions}
      />,
    );
    expect(screen.getByRole("tab", { name: "Audible (2)" })).toHaveAttribute(
      "aria-selected",
      "true",
    );
  });
});

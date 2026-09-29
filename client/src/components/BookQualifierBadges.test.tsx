import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { BookQualifierBadges } from "./BookQualifierBadges";

vi.mock("@/services/api", () => ({
  settingsApi: {
    getBookQualifiers: vi.fn().mockResolvedValue({
      qualifiers: [
        { key: "abridged", label: "Abridged", suffix: " (Abridged)" },
        { key: "dramatized", label: "Dramatized", suffix: " (Dramatized)" },
      ],
    }),
  },
}));

function renderBadges(qualifiers: readonly string[] | null | undefined) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={queryClient}>
      <BookQualifierBadges qualifiers={qualifiers} />
    </QueryClientProvider>,
  );
}

describe("BookQualifierBadges", () => {
  it("renders one badge per qualifier, labelled and alphabetical", async () => {
    renderBadges(["dramatized", "abridged"]);

    const badges = await screen.findAllByText(/Abridged|Dramatized/);
    expect(badges.map((b) => b.textContent)).toEqual(["Abridged", "Dramatized"]);
  });

  it("renders nothing for a book with no qualifiers", () => {
    const { container } = renderBadges([]);

    expect(container).toBeEmptyDOMElement();
  });

  it("renders nothing when the qualifiers are missing altogether", () => {
    const { container } = renderBadges(undefined);

    expect(container).toBeEmptyDOMElement();
  });

  it("does not need a query client for a book with no qualifiers", () => {
    // Long lists render this once per row; the common no-qualifier row must stay free.
    const { container } = render(<BookQualifierBadges qualifiers={[]} />);

    expect(container).toBeEmptyDOMElement();
  });

  it("shows a key the list no longer has as itself rather than dropping it", async () => {
    renderBadges(["zzz-retired"]);

    expect(await screen.findByText("zzz-retired")).toBeInTheDocument();
  });
});

import { describe, it, expect, vi } from "vitest";
import { render, screen, fireEvent } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { QualifiersField } from "./QualifiersField";

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

function renderField(value: string[], onChange = vi.fn(), disabled = false) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  render(
    <QueryClientProvider client={queryClient}>
      <QualifiersField value={value} onChange={onChange} disabled={disabled} />
    </QueryClientProvider>,
  );
  return onChange;
}

describe("QualifiersField", () => {
  it("offers every qualifier the backend serves, none pressed by default", async () => {
    renderField([]);

    const abridged = await screen.findByRole("button", { name: "Abridged" });
    expect(abridged).toHaveAttribute("aria-pressed", "false");
    expect(screen.getByRole("button", { name: "Dramatized" })).toHaveAttribute(
      "aria-pressed",
      "false",
    );
  });

  it("adds a qualifier in canonical order whatever order they were toggled in", async () => {
    const onChange = renderField(["dramatized"]);

    fireEvent.click(await screen.findByRole("button", { name: "Abridged" }));

    expect(onChange).toHaveBeenCalledWith(["abridged", "dramatized"]);
  });

  it("removes a qualifier that is toggled off", async () => {
    const onChange = renderField(["abridged", "dramatized"]);

    fireEvent.click(await screen.findByRole("button", { name: "Abridged" }));

    expect(onChange).toHaveBeenCalledWith(["dramatized"]);
  });

  it("keeps a key the list no longer has visible and removable instead of losing it silently", async () => {
    const onChange = renderField(["zzz-retired"]);

    const retired = await screen.findByRole("button", { name: "zzz-retired" });
    expect(retired).toHaveAttribute("aria-pressed", "true");
    fireEvent.click(retired);

    expect(onChange).toHaveBeenCalledWith([]);
  });

  it("disables every toggle when disabled", async () => {
    renderField([], vi.fn(), true);

    expect(await screen.findByRole("button", { name: "Abridged" })).toBeDisabled();
    expect(screen.getByRole("button", { name: "Dramatized" })).toBeDisabled();
  });
});

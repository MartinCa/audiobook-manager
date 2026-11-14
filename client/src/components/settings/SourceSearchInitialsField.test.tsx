import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { SourceSearchInitialsField } from "./SourceSearchInitialsField";
import { metadataSearchApi } from "@/services/api";
import type { SearchInitialsHandling } from "@/types/LibrarySettings";

vi.mock("@/services/api", () => ({
  metadataSearchApi: { getServices: vi.fn() },
}));

function renderField(
  value: Record<string, SearchInitialsHandling> = {},
  libraryDefault: SearchInitialsHandling | null = "AsStored",
) {
  const onChange = vi.fn();
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  render(
    <QueryClientProvider client={queryClient}>
      <SourceSearchInitialsField
        value={value}
        libraryDefault={libraryDefault}
        onChange={onChange}
      />
    </QueryClientProvider>,
  );
  return { onChange };
}

describe("SourceSearchInitialsField", () => {
  beforeEach(() => {
    vi.mocked(metadataSearchApi.getServices).mockResolvedValue([
      { name: "Audible", enabled: true },
      { name: "Hardcover", enabled: false, disabledReason: "API key not configured" },
    ]);
  });

  it("lists one select per source the backend reports, whether or not it is enabled", async () => {
    renderField();

    expect(
      await screen.findByRole("combobox", { name: "Audible initials handling" }),
    ).toBeVisible();
    expect(screen.getByRole("combobox", { name: "Hardcover initials handling" })).toBeVisible();
  });

  it("shows a source with no override as following the library default, naming that default", async () => {
    renderField({}, "Spaced");

    expect(
      await screen.findByRole("combobox", { name: "Audible initials handling" }),
    ).toHaveTextContent("Library default (Spaced (George R. R. Martin))");
  });

  it("shows a source's own override", async () => {
    renderField({ Hardcover: "Compact" });

    expect(
      await screen.findByRole("combobox", { name: "Hardcover initials handling" }),
    ).toHaveTextContent("Compact (George R.R. Martin)");
    expect(screen.getByRole("combobox", { name: "Audible initials handling" })).toHaveTextContent(
      "Library default",
    );
  });

  it("adds an override for just the changed source, keeping the others", async () => {
    const user = userEvent.setup();
    const { onChange } = renderField({ Audible: "Spaced" });

    await user.click(await screen.findByRole("combobox", { name: "Hardcover initials handling" }));
    await user.click(await screen.findByRole("option", { name: "Compact (George R.R. Martin)" }));

    expect(onChange).toHaveBeenCalledWith({ Audible: "Spaced", Hardcover: "Compact" });
  });

  it("removes the override when the source goes back to the library default", async () => {
    const user = userEvent.setup();
    const { onChange } = renderField({ Audible: "Spaced", Hardcover: "Compact" });

    await user.click(await screen.findByRole("combobox", { name: "Hardcover initials handling" }));
    await user.click(await screen.findByRole("option", { name: /^Library default/ }));

    expect(onChange).toHaveBeenCalledWith({ Audible: "Spaced" });
  });

  it("an explicit 'As stored' is an override of its own, not the same as the library default", async () => {
    const user = userEvent.setup();
    const { onChange } = renderField({}, "Compact");

    await user.click(await screen.findByRole("combobox", { name: "Audible initials handling" }));
    await user.click(
      await screen.findByRole("option", { name: "As stored (the name as it is in the library)" }),
    );

    expect(onChange).toHaveBeenCalledWith({ Audible: "AsStored" });
  });

  it("leaves an override stored for a source that is not listed untouched when another changes", async () => {
    const user = userEvent.setup();
    const { onChange } = renderField({ RemovedSource: "Spaced" });

    await user.click(await screen.findByRole("combobox", { name: "Audible initials handling" }));
    await user.click(await screen.findByRole("option", { name: "Compact (George R.R. Martin)" }));

    expect(onChange).toHaveBeenCalledWith({ RemovedSource: "Spaced", Audible: "Compact" });
  });

  it("renders nothing while there are no sources to list", async () => {
    vi.mocked(metadataSearchApi.getServices).mockResolvedValue([]);
    renderField();

    await vi.waitFor(() => expect(metadataSearchApi.getServices).toHaveBeenCalled());
    expect(screen.queryByRole("group")).not.toBeInTheDocument();
  });
});

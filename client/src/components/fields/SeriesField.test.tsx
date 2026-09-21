import { describe, it, expect, vi } from "vitest";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { SeriesField } from "./SeriesField";
import type * as ApiModule from "@/services/api";

vi.mock("@/services/api", async (importOriginal) => {
  const actual = await importOriginal<typeof ApiModule>();
  return {
    ...actual,
    similarValuesApi: {
      getAutocomplete: vi.fn().mockResolvedValue([]),
      getEntryStatus: vi.fn().mockResolvedValue({
        value: "",
        status: "new",
        exactMatch: null,
        similarMatches: [],
      }),
    },
  };
});

import { similarValuesApi } from "@/services/api";

function renderField(props: Partial<Parameters<typeof SeriesField>[0]> = {}) {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });
  const onChange = vi.fn();
  render(
    <QueryClientProvider client={queryClient}>
      <SeriesField value="Stormlight" onChange={onChange} {...props} />
    </QueryClientProvider>,
  );
  return { onChange };
}

describe("SeriesField", () => {
  it("forwards the owning form's ref to the underlying input", async () => {
    const ref = vi.fn();
    renderField({ ref });

    // The ref must be attached to the actual input element, not the wrapping div, so
    // react-hook-form's focus management sees a real DOM node to focus.
    const input = screen.getByPlaceholderText("Series name");
    await vi.waitFor(() => {
      expect(ref.mock.calls.some(([instance]) => instance === input)).toBe(true);
    });
  });

  // Regression for the review finding: BookEditForm used to hand the series TypeaheadInput
  // onBlur={field.onBlur} for the form's focus/blur tracking, and the SeriesField extraction
  // dropped it. A blur must reach the passed handler so RHF mark-on-blur and the
  // entry-time-duplicate-hint blur semantics keep working on the single-book form.
  it("forwards an onBlur handler to the underlying input", () => {
    const onBlur = vi.fn();
    renderField({ onBlur });

    fireEvent.blur(screen.getByPlaceholderText("Series name"));
    expect(onBlur).toHaveBeenCalledTimes(1);
  });

  it("classifies the typed value through the bounded server-backed endpoint", async () => {
    vi.mocked(similarValuesApi.getEntryStatus).mockResolvedValue({
      value: "Stormlight",
      status: "exact",
      exactMatch: { id: null, name: "Stormlight" },
      similarMatches: [],
    });
    renderField();

    await waitFor(() => {
      expect(similarValuesApi.getEntryStatus).toHaveBeenCalledWith("series", "Stormlight", 3);
    });
    expect(await screen.findByText("Stormlight")).toBeInTheDocument();
    expect(screen.getByText("— existing entry")).toBeInTheDocument();
  });

  // The match is case/accent-insensitive, so "exact" also covers a value that only differs in
  // casing from the library's - saving it as typed would create a second, differently-cased
  // value, so this gets an actionable casing-fix hint instead of the plain success note.
  it("offers to fix casing when the exact match differs only in case", async () => {
    vi.mocked(similarValuesApi.getEntryStatus).mockResolvedValue({
      value: "the stormlight archive",
      status: "exact",
      exactMatch: { id: null, name: "The Stormlight Archive" },
      similarMatches: [],
    });
    const { onChange } = renderField({ value: "the stormlight archive" });

    const hint = await screen.findByRole("button", {
      name: /different casing.*click to fix casing/i,
    });
    fireEvent.click(hint);
    expect(onChange).toHaveBeenCalledWith("The Stormlight Archive");
  });

  it("applies a similar candidate when its hint is clicked", async () => {
    vi.mocked(similarValuesApi.getEntryStatus).mockResolvedValue({
      value: "Storlight",
      status: "similar",
      exactMatch: null,
      similarMatches: [{ id: null, name: "The Stormlight Archive" }],
    });
    const { onChange } = renderField();

    const hint = await screen.findByRole("button", {
      name: /Similar to "The Stormlight Archive" — click to use/i,
    });
    fireEvent.click(hint);
    expect(onChange).toHaveBeenCalledWith("The Stormlight Archive");
  });

  it("surfaces an advisory error when the bounded classification query fails", async () => {
    vi.mocked(similarValuesApi.getEntryStatus).mockRejectedValue(new Error("network down"));
    renderField();

    expect(
      await screen.findByText(/couldn't check the library for existing entries/i),
    ).toBeInTheDocument();
    expect(screen.queryByText(/New — no exact or similar entry/i)).not.toBeInTheDocument();
  });

  it("requests type-ahead candidates through the bounded server-side lookup", async () => {
    vi.mocked(similarValuesApi.getAutocomplete).mockResolvedValue(["The Stormlight Archive"]);
    renderField({ value: "Storm" });

    await waitFor(() =>
      expect(similarValuesApi.getAutocomplete).toHaveBeenCalledWith("series", "Storm", 6),
    );

    // The dropdown only opens once the field is focused - same as every other TypeaheadInput.
    const input = screen.getByPlaceholderText("Series name");
    fireEvent.focus(input);
    expect(
      await screen.findByRole("option", { name: "The Stormlight Archive" }),
    ).toBeInTheDocument();
  });
});

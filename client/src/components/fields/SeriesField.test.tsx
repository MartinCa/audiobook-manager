import { describe, it, expect, vi } from "vitest";
import { render, screen, fireEvent } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { SeriesField } from "./SeriesField";
import type * as ApiModule from "@/services/api";

vi.mock("@/services/api", async (importOriginal) => {
  const actual = await importOriginal<typeof ApiModule>();
  return {
    ...actual,
    similarValuesApi: {
      getSeriesNames: vi.fn().mockResolvedValue(["The Stormlight Archive"]),
    },
  };
});

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
});

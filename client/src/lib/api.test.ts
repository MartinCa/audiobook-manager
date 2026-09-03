import { describe, it, expect, vi, beforeEach } from "vitest";
import { api } from "./api";

/**
 * The backend refuses state-changing /api requests that arrive without this header
 * (CrossSiteRequestGuardMiddleware), so every request this module makes has to carry it —
 * including the bodyless POSTs and DELETEs, which have no Content-Type to identify them by.
 */
describe("api request headers", () => {
  beforeEach(() => {
    vi.restoreAllMocks();
  });

  function mockFetch() {
    return vi.spyOn(globalThis, "fetch").mockResolvedValue(new Response(null, { status: 204 }));
  }

  function headersOf(fetchSpy: ReturnType<typeof mockFetch>): Headers {
    const init = fetchSpy.mock.calls[0]?.[1];
    return new Headers(init?.headers);
  }

  it("sends X-Requested-With on a POST with a body", async () => {
    const fetchSpy = mockFetch();

    await api.post("/audiobook/organize", { bookName: "A Book" });

    const headers = headersOf(fetchSpy);
    expect(headers.get("X-Requested-With")).toBe("XMLHttpRequest");
    expect(headers.get("Content-Type")).toBe("application/json");
  });

  it("sends X-Requested-With on a POST with no body", async () => {
    const fetchSpy = mockFetch();

    await api.post("/consistency/check");

    const headers = headersOf(fetchSpy);
    expect(headers.get("X-Requested-With")).toBe("XMLHttpRequest");
    // No body means no Content-Type, which is exactly why the guard cannot key on it.
    expect(headers.get("Content-Type")).toBeNull();
  });

  it("sends X-Requested-With on a DELETE", async () => {
    const fetchSpy = mockFetch();

    await api.delete("/audiobook/42");

    expect(headersOf(fetchSpy).get("X-Requested-With")).toBe("XMLHttpRequest");
  });

  it("sends X-Requested-With on a GET", async () => {
    const fetchSpy = mockFetch();

    await api.get("/browse/authors");

    expect(headersOf(fetchSpy).get("X-Requested-With")).toBe("XMLHttpRequest");
  });

  it("lets a caller-supplied header win over the defaults", async () => {
    const fetchSpy = mockFetch();

    await api.get("/browse/authors", { headers: { Accept: "text/plain" } });

    const headers = headersOf(fetchSpy);
    expect(headers.get("Accept")).toBe("text/plain");
    expect(headers.get("X-Requested-With")).toBe("XMLHttpRequest");
  });
});

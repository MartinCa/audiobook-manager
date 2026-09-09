import { describe, it, expect, vi, beforeEach } from "vitest";
import { api, REQUESTED_WITH_HEADER, REQUESTED_WITH_VALUE } from "./api";

/**
 * The protocol values the backend's CrossSiteRequestGuardMiddleware requires on every
 * state-changing /api request. These literals are the ground truth in this test: assertions
 * read from them rather than from the module's exported constants, so a constant that drifts
 * to an invalid value fails the suite (see "exports the exact protocol values" below).
 */
const EXPECTED_REQUESTED_WITH_HEADER = "X-Requested-With";
const EXPECTED_REQUESTED_WITH_VALUE = "XMLHttpRequest";

/**
 * The backend refuses state-changing /api requests that arrive without this header
 * (CrossSiteRequestGuardMiddleware), so every request this module makes has to carry it —
 * including the bodyless POSTs and DELETEs, which have no Content-Type to identify them by.
 */
describe("api request headers", () => {
  it("exports the exact protocol values CrossSiteRequestGuardMiddleware requires", () => {
    expect(REQUESTED_WITH_HEADER).toBe(EXPECTED_REQUESTED_WITH_HEADER);
    expect(REQUESTED_WITH_VALUE).toBe(EXPECTED_REQUESTED_WITH_VALUE);
  });
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
    expect(headers.get(EXPECTED_REQUESTED_WITH_HEADER)).toBe(EXPECTED_REQUESTED_WITH_VALUE);
    expect(headers.get("Content-Type")).toBe("application/json");
  });

  it("sends X-Requested-With on a POST with no body", async () => {
    const fetchSpy = mockFetch();

    await api.post("/consistency/check");

    const headers = headersOf(fetchSpy);
    expect(headers.get(EXPECTED_REQUESTED_WITH_HEADER)).toBe(EXPECTED_REQUESTED_WITH_VALUE);
    // No body means no Content-Type, which is exactly why the guard cannot key on it.
    expect(headers.get("Content-Type")).toBeNull();
  });

  it("sends X-Requested-With on a DELETE", async () => {
    const fetchSpy = mockFetch();

    await api.delete("/audiobook/42");

    expect(headersOf(fetchSpy).get(EXPECTED_REQUESTED_WITH_HEADER)).toBe(
      EXPECTED_REQUESTED_WITH_VALUE,
    );
  });

  it("sends X-Requested-With on a GET", async () => {
    const fetchSpy = mockFetch();

    await api.get("/browse/authors");

    expect(headersOf(fetchSpy).get(EXPECTED_REQUESTED_WITH_HEADER)).toBe(
      EXPECTED_REQUESTED_WITH_VALUE,
    );
  });

  it("lets a caller-supplied header win over the defaults", async () => {
    const fetchSpy = mockFetch();

    await api.get("/browse/authors", { headers: { Accept: "text/plain" } });

    const headers = headersOf(fetchSpy);
    expect(headers.get("Accept")).toBe("text/plain");
    expect(headers.get(EXPECTED_REQUESTED_WITH_HEADER)).toBe(EXPECTED_REQUESTED_WITH_VALUE);
  });
});

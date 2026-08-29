import { describe, it, expect, vi, beforeEach } from "vitest";
import apiClient from "../http-common";
import LanguageService from "./LanguageService";

vi.mock("../http-common", () => ({
  default: {
    get: vi.fn(),
  },
}));

const mockedGet = vi.mocked(apiClient.get);

const options = {
  languages: [
    { code: "en", displayName: "English", aliases: ["en", "eng", "english"] },
    {
      code: "da",
      displayName: "Danish",
      aliases: ["da", "dan", "danish", "dansk"],
    },
  ],
  defaultCode: "en",
};

beforeEach(() => {
  mockedGet.mockReset();
  LanguageService.invalidateCache();
});

describe("LanguageService", () => {
  it("fetches the language options from the settings endpoint", async () => {
    mockedGet.mockResolvedValue({ data: options } as any);

    const result = await LanguageService.getLanguageOptions();

    expect(mockedGet).toHaveBeenCalledWith("/settings/languages");
    expect(result).toEqual(options);
  });

  it("serves later callers from the cache instead of refetching", async () => {
    mockedGet.mockResolvedValue({ data: options } as any);

    await LanguageService.getLanguageOptions();
    await LanguageService.getLanguageOptions();

    expect(mockedGet).toHaveBeenCalledTimes(1);
  });

  it("shares one request between callers that ask before the first lands", async () => {
    mockedGet.mockResolvedValue({ data: options } as any);

    // Every form and preview dialog asks on mount, so this is the common case, not an edge one.
    const [first, second] = await Promise.all([
      LanguageService.getLanguageOptions(),
      LanguageService.getLanguageOptions(),
    ]);

    expect(mockedGet).toHaveBeenCalledTimes(1);
    expect(first).toEqual(options);
    expect(second).toEqual(options);
  });

  it("retries after a failure instead of caching the rejection forever", async () => {
    mockedGet.mockRejectedValueOnce(new Error("offline"));
    mockedGet.mockResolvedValueOnce({ data: options } as any);

    await expect(LanguageService.getLanguageOptions()).rejects.toThrow(
      "offline",
    );
    const result = await LanguageService.getLanguageOptions();

    expect(mockedGet).toHaveBeenCalledTimes(2);
    expect(result).toEqual(options);
  });
});

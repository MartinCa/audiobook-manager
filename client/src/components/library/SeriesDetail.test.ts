import { describe, it, expect, vi, beforeEach } from "vitest";
import { mount } from "@vue/test-utils";
import { createVuetify } from "vuetify";
import * as components from "vuetify/components";
import * as directives from "vuetify/directives";
import { reactive } from "vue";
import SeriesDetail from "./SeriesDetail.vue";
import SeriesService from "../../services/SeriesService";
import { SeriesDetail as SeriesDetailType } from "../../types/Series";

const vuetify = createVuetify({ components, directives });

const route = reactive({ params: { seriesName: "Series One" }, query: {} });

vi.mock("vue-router", () => ({
  useRoute: () => route,
  useRouter: () => ({ push: vi.fn() }),
}));

vi.mock("../../services/SeriesService", () => ({
  default: {
    getSeriesDetail: vi.fn(),
    getMatchCandidates: vi.fn(),
    searchMatchCandidates: vi.fn(),
    matchSeries: vi.fn(),
    ignoreExpectedBook: vi.fn(),
    unignoreExpectedBook: vi.fn(),
    setIncludeOmnibusEditions: vi.fn(),
    startRefreshSeries: vi.fn(),
  },
}));

const fakeSignalR = {
  on: vi.fn(),
  off: vi.fn(),
  onReconnected: vi.fn(),
  offReconnected: vi.fn(),
};

vi.mock("@/signalr/hub", async () => {
  const vue = await import("vue");
  return {
    useSignalR: () => fakeSignalR,
    useSignalREvent: (token: string, callback: (...args: any[]) => void) => {
      vue.onMounted(() => fakeSignalR.on(token, callback));
      vue.onUnmounted(() => fakeSignalR.off(token, callback));
    },
    useSignalRReconnected: (callback: () => void) => {
      vue.onMounted(() => fakeSignalR.onReconnected(callback));
      vue.onUnmounted(() => fakeSignalR.offReconnected(callback));
    },
  };
});

vi.mock("../../services/OperationsService", () => ({
  default: {
    getStatus: vi
      .fn()
      .mockResolvedValue({ isRunning: false, processed: 0, total: 0 }),
  },
}));

const mockedGetSeriesDetail = vi.mocked(SeriesService.getSeriesDetail);

function makeDetail(name: string, bookName: string): SeriesDetailType {
  return {
    overview: {
      id: 1,
      name,
      authors: ["Author"],
      ownedBookCount: 1,
      isMatched: false,
      matchedSourceName: null,
      matchedSourceId: null,
      matchedSourceUrl: null,
      matchConfidence: null,
      lastRefreshedAt: null,
      expectedBookCount: 0,
      missingBookCount: 0,
      ignoredBookCount: 0,
      includeOmnibusEditions: false,
    },
    ownedBooks: [
      {
        id: 1,
        bookName,
        seriesPart: "1",
        year: 2020,
        authors: ["Author"],
        narrators: [],
        durationInSeconds: null,
      },
    ],
    missingBooks: [],
    ignoredBooks: [],
  };
}

function mountDetail() {
  return mount(SeriesDetail, {
    global: { plugins: [vuetify] },
  });
}

function flushPromises() {
  return new Promise((resolve) => setTimeout(resolve, 0));
}

beforeEach(() => {
  vi.clearAllMocks();
  route.params.seriesName = "Series One";
});

describe("SeriesDetail route param reactivity", () => {
  it("reloads series detail when navigating to a different seriesName while the component instance persists", async () => {
    mockedGetSeriesDetail
      .mockResolvedValueOnce(makeDetail("Series One", "First Book"))
      .mockResolvedValueOnce(makeDetail("Series Two", "Second Book"));

    const wrapper = mountDetail();
    await flushPromises();

    expect(mockedGetSeriesDetail).toHaveBeenCalledTimes(1);
    expect(mockedGetSeriesDetail).toHaveBeenCalledWith("Series One");
    expect(wrapper.text()).toContain("First Book");

    route.params.seriesName = "Series Two";
    await flushPromises();

    expect(mockedGetSeriesDetail).toHaveBeenCalledTimes(2);
    expect(mockedGetSeriesDetail).toHaveBeenCalledWith("Series Two");
    expect(wrapper.text()).toContain("Second Book");
    expect(wrapper.text()).not.toContain("First Book");

    wrapper.unmount();
  });
});

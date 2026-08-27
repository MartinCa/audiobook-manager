import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { mount } from "@vue/test-utils";
import {
  useOperationProgress,
  OperationProgress,
  OperationProgressOptions,
} from "./useOperationProgress";
import OperationsService from "../services/OperationsService";

vi.mock("../services/OperationsService", () => ({
  default: { getStatus: vi.fn() },
}));

const fakeSignalR = {
  on: vi.fn(),
  off: vi.fn(),
  onReconnected: vi.fn(),
  offReconnected: vi.fn(),
};

vi.mock("@/signalr/hub", () => ({
  useSignalR: () => fakeSignalR,
}));

const mockedGetStatus = vi.mocked(OperationsService.getStatus);

interface TestProgress {
  processed: number;
  total: number;
}
interface TestComplete {
  succeeded: number;
}

function getHandler(
  mockFn: typeof fakeSignalR.on | typeof fakeSignalR.onReconnected,
  token?: string,
): (...args: any[]) => void {
  const call = token
    ? mockFn.mock.calls.find((c) => c[0] === token)
    : mockFn.mock.calls[0];
  if (!call) throw new Error(`No registered handler found for ${token}`);
  return token ? call[1] : call[0];
}

function mountComposable(
  overrides: Partial<OperationProgressOptions<TestProgress, TestComplete>> = {},
) {
  let captured!: OperationProgress;
  const onProgress = vi.fn();
  const onComplete = vi.fn();

  const options: OperationProgressOptions<TestProgress, TestComplete> = {
    key: "test-op",
    progressToken: "TestProgress",
    completeToken: "TestComplete",
    getProcessed: (p) => p.processed,
    getTotal: (p) => p.total,
    onProgress,
    onComplete,
    ...overrides,
  };

  const wrapper = mount({
    setup() {
      captured = useOperationProgress(options);
      return {};
    },
    template: "<div/>",
  });

  return { wrapper, captured: captured!, onProgress, onComplete };
}

beforeEach(() => {
  vi.clearAllMocks();
  mockedGetStatus.mockResolvedValue({
    isRunning: false,
    processed: 0,
    total: 0,
  });
});

afterEach(() => {
  vi.useRealTimers();
});

describe("useOperationProgress", () => {
  it("starts with isRunning false and zeroed counters", () => {
    const { captured } = mountComposable();

    expect(captured.isRunning.value).toBe(false);
    expect(captured.processed.value).toBe(0);
    expect(captured.total.value).toBe(0);
  });

  it("registers signalR listeners for the progress/complete tokens on setup", () => {
    mountComposable();

    expect(fakeSignalR.on).toHaveBeenCalledWith(
      "TestProgress",
      expect.any(Function),
    );
    expect(fakeSignalR.on).toHaveBeenCalledWith(
      "TestComplete",
      expect.any(Function),
    );
    expect(fakeSignalR.onReconnected).toHaveBeenCalledWith(
      expect.any(Function),
    );
  });

  describe("start()", () => {
    it("sets isRunning true and resets counters", () => {
      const { captured } = mountComposable();

      captured.start();

      expect(captured.isRunning.value).toBe(true);
      expect(captured.processed.value).toBe(0);
      expect(captured.total.value).toBe(0);
    });
  });

  describe("progress events", () => {
    it("sets isRunning true and applies the leading update immediately", () => {
      const { captured, onProgress } = mountComposable();
      const handleProgress = getHandler(fakeSignalR.on, "TestProgress");

      handleProgress({ processed: 3, total: 10 });

      expect(captured.isRunning.value).toBe(true);
      expect(captured.processed.value).toBe(3);
      expect(captured.total.value).toBe(10);
      expect(onProgress).toHaveBeenCalledWith({ processed: 3, total: 10 });
    });

    it("throttles rapid successive updates, applying the trailing value after the interval", () => {
      vi.useFakeTimers();
      const { captured } = mountComposable({ throttleMs: 250 });
      const handleProgress = getHandler(fakeSignalR.on, "TestProgress");

      handleProgress({ processed: 1, total: 10 });
      handleProgress({ processed: 2, total: 10 });
      handleProgress({ processed: 3, total: 10 });

      // Leading edge applied synchronously with the first call only.
      expect(captured.processed.value).toBe(1);

      vi.advanceTimersByTime(250);

      // Trailing edge applies the most recent value.
      expect(captured.processed.value).toBe(3);
    });

    it("calls onProgress synchronously for every event, unthrottled", () => {
      vi.useFakeTimers();
      const { onProgress } = mountComposable({ throttleMs: 250 });
      const handleProgress = getHandler(fakeSignalR.on, "TestProgress");

      handleProgress({ processed: 1, total: 10 });
      handleProgress({ processed: 2, total: 10 });

      expect(onProgress).toHaveBeenCalledTimes(2);
    });
  });

  describe("complete events", () => {
    it("resets isRunning and counters and calls onComplete", () => {
      const { captured, onComplete } = mountComposable();
      const handleProgress = getHandler(fakeSignalR.on, "TestProgress");
      const handleComplete = getHandler(fakeSignalR.on, "TestComplete");

      handleProgress({ processed: 5, total: 10 });
      handleComplete({ succeeded: 5 });

      expect(captured.isRunning.value).toBe(false);
      expect(captured.processed.value).toBe(0);
      expect(captured.total.value).toBe(0);
      expect(onComplete).toHaveBeenCalledWith({ succeeded: 5 });
    });

    it("cancels any pending throttled progress update", () => {
      vi.useFakeTimers();
      const { captured } = mountComposable({ throttleMs: 250 });
      const handleProgress = getHandler(fakeSignalR.on, "TestProgress");
      const handleComplete = getHandler(fakeSignalR.on, "TestComplete");

      handleProgress({ processed: 1, total: 10 });
      handleProgress({ processed: 2, total: 10 });
      handleComplete({ succeeded: 2 });

      vi.advanceTimersByTime(250);

      // The pending trailing update from before completion must not resurrect stale counters.
      expect(captured.processed.value).toBe(0);
      expect(captured.total.value).toBe(0);
    });
  });

  describe("initial status refresh (onMounted)", () => {
    it("fetches status for the operation key and applies a running status", async () => {
      mockedGetStatus.mockResolvedValue({
        isRunning: true,
        processed: 4,
        total: 9,
      });

      const { captured } = mountComposable();
      await vi.waitFor(() => expect(captured.isRunning.value).toBe(true));

      expect(mockedGetStatus).toHaveBeenCalledWith("test-op");
      expect(captured.processed.value).toBe(4);
      expect(captured.total.value).toBe(9);
    });

    it("leaves state unchanged when the status fetch fails", async () => {
      mockedGetStatus.mockRejectedValue(new Error("boom"));

      const { captured } = mountComposable();
      await vi.waitFor(() => expect(mockedGetStatus).toHaveBeenCalled());

      expect(captured.isRunning.value).toBe(false);
      expect(captured.processed.value).toBe(0);
      expect(captured.total.value).toBe(0);
    });
  });

  describe("reconnection", () => {
    it("re-fetches status when the connection reconnects", async () => {
      mountComposable();
      await vi.waitFor(() => expect(mockedGetStatus).toHaveBeenCalledTimes(1));

      mockedGetStatus.mockResolvedValueOnce({
        isRunning: true,
        processed: 2,
        total: 5,
      });
      const refreshStatus = getHandler(fakeSignalR.onReconnected);
      await refreshStatus();

      expect(mockedGetStatus).toHaveBeenCalledTimes(2);
    });
  });

  describe("unmount cleanup", () => {
    it("unregisters listeners and cancels the throttle on unmount", () => {
      vi.useFakeTimers();
      const { wrapper, captured } = mountComposable({ throttleMs: 250 });
      const handleProgress = getHandler(fakeSignalR.on, "TestProgress");

      handleProgress({ processed: 1, total: 10 });
      handleProgress({ processed: 2, total: 10 });

      wrapper.unmount();

      expect(fakeSignalR.off).toHaveBeenCalledWith(
        "TestProgress",
        expect.any(Function),
      );
      expect(fakeSignalR.off).toHaveBeenCalledWith(
        "TestComplete",
        expect.any(Function),
      );
      expect(fakeSignalR.offReconnected).toHaveBeenCalledWith(
        expect.any(Function),
      );

      vi.advanceTimersByTime(250);
      // Pending trailing update was cancelled by unmount, so it never lands.
      expect(captured.processed.value).toBe(1);
    });
  });
});

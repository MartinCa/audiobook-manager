import { describe, it, expect, vi, beforeEach } from "vitest";
import { mount } from "@vue/test-utils";
import { createSignalR, useSignalR, HubEventToken } from "./hub";

const mockConnection = {
  start: vi.fn().mockResolvedValue(undefined),
  on: vi.fn(),
  off: vi.fn(),
};

const mockBuilder = {
  withUrl: vi.fn().mockReturnThis(),
  withAutomaticReconnect: vi.fn().mockReturnThis(),
  configureLogging: vi.fn().mockReturnThis(),
  build: vi.fn().mockReturnValue(mockConnection),
};

vi.mock("@microsoft/signalr", () => ({
  HubConnectionBuilder: vi.fn(function () {
    return mockBuilder;
  }),
  LogLevel: { Warning: 2 },
}));

beforeEach(() => {
  vi.clearAllMocks();
  mockConnection.start.mockResolvedValue(undefined);
});

const Stub = { template: "<div/>" };

function mountWithSignalR<T>(setup: () => T) {
  let captured: T | undefined;
  let capturedError: Error | undefined;

  mount(
    {
      setup() {
        try {
          captured = setup();
        } catch (e) {
          capturedError = e as Error;
        }
      },
      template: "<div/>",
    },
    {
      global: {
        plugins: [createSignalR("http://test/hubs/organize")],
      },
    },
  );

  return { captured, capturedError };
}

describe("createSignalR", () => {
  it("builds a HubConnection with the given URL", () => {
    mount(Stub, {
      global: { plugins: [createSignalR("http://test/hubs/organize")] },
    });
    expect(mockBuilder.withUrl).toHaveBeenCalledWith(
      "http://test/hubs/organize",
    );
    expect(mockBuilder.withAutomaticReconnect).toHaveBeenCalled();
    expect(mockBuilder.build).toHaveBeenCalled();
  });

  it("starts the connection on install", () => {
    mount(Stub, {
      global: { plugins: [createSignalR("http://test/hubs/organize")] },
    });
    expect(mockConnection.start).toHaveBeenCalledOnce();
  });

  it("logs a console error if start() rejects", async () => {
    const consoleError = vi
      .spyOn(console, "error")
      .mockImplementation(() => {});
    mockConnection.start.mockRejectedValueOnce(new Error("network error"));

    mount(Stub, {
      global: { plugins: [createSignalR("http://test/hubs/organize")] },
    });
    await vi.waitFor(() => expect(consoleError).toHaveBeenCalled());

    expect(consoleError.mock.calls[0][0]).toBe("SignalR connection error:");
    consoleError.mockRestore();
  });
});

describe("useSignalR", () => {
  it("returns a client with on and off when plugin is installed", () => {
    const { captured } = mountWithSignalR(() => useSignalR());
    expect(captured).toBeDefined();
    expect(typeof captured!.on).toBe("function");
    expect(typeof captured!.off).toBe("function");
  });

  it("throws when called without the plugin", () => {
    let capturedError: Error | undefined;
    mount(
      {
        setup() {
          try {
            useSignalR();
          } catch (e) {
            capturedError = e as Error;
          }
        },
        template: "<div/>",
      },
      { global: { config: { warnHandler: () => {} } } },
    );
    expect(capturedError?.message).toBe("SignalR plugin not installed");
  });
});

describe("SignalRClient.on / off", () => {
  it("on() calls HubConnection.on with the token name and callback", () => {
    const token: HubEventToken<{ count: number }> = "ProgressUpdate";
    const cb = vi.fn();
    const { captured } = mountWithSignalR(() => useSignalR());

    captured!.on(token, cb);

    expect(mockConnection.on).toHaveBeenCalledWith("ProgressUpdate", cb);
  });

  it("off() calls HubConnection.off with the token name and callback", () => {
    const token: HubEventToken<{ count: number }> = "ProgressUpdate";
    const cb = vi.fn();
    const { captured } = mountWithSignalR(() => useSignalR());

    captured!.off(token, cb);

    expect(mockConnection.off).toHaveBeenCalledWith("ProgressUpdate", cb);
  });

  it("passes the same callback reference to on and off", () => {
    const token: HubEventToken<string> = "MyEvent";
    const cb = vi.fn();
    const { captured } = mountWithSignalR(() => useSignalR());

    captured!.on(token, cb);
    captured!.off(token, cb);

    expect(mockConnection.on.mock.calls[0][1]).toBe(
      mockConnection.off.mock.calls[0][1],
    );
  });
});

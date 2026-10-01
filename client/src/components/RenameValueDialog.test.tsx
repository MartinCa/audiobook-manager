import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor, fireEvent, act } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { RenameValueDialog } from "./RenameValueDialog";
import { SignalREvents } from "@/constants/signalrEvents";
import { SignalRContext } from "@/context/SignalRContext";
import { similarValuesApi } from "@/services/api";
import { notifications } from "@/lib/notifications";
import type { HubEventHandler, SignalRContextValue } from "@/context/SignalRContext";

vi.mock("@/services/api", () => ({
  similarValuesApi: { rename: vi.fn() },
  settingsApi: {
    getBookQualifiers: vi.fn().mockResolvedValue({
      qualifiers: [{ key: "dramatized", label: "Dramatized", suffix: " (Dramatized)" }],
    }),
  },
}));

vi.mock("@/lib/notifications", () => ({
  notifications: { success: vi.fn(), error: vi.fn(), info: vi.fn(), warning: vi.fn() },
}));

function makeSignalR() {
  const handlers = new Map<string, HubEventHandler<unknown>[]>();
  return {
    connection: null,
    isConnected: false,
    on: vi.fn((event: string, handler: HubEventHandler<unknown>) => {
      handlers.set(event, [...(handlers.get(event) ?? []), handler]);
    }),
    off: vi.fn((event: string, handler: HubEventHandler<unknown>) => {
      handlers.set(
        event,
        (handlers.get(event) ?? []).filter((h) => h !== handler),
      );
    }),
    onReconnected: vi.fn(),
    offReconnected: vi.fn(),
    emit: (event: string, data: unknown) => {
      for (const handler of handlers.get(event) ?? []) handler(data);
    },
  };
}

let signalR: ReturnType<typeof makeSignalR>;
const onRenamed = vi.fn();
const onOpenChange = vi.fn();

function renderDialog(props: Partial<React.ComponentProps<typeof RenameValueDialog>> = {}) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <SignalRContext.Provider value={signalR as SignalRContextValue}>
      <QueryClientProvider client={queryClient}>
        <RenameValueDialog
          open
          onOpenChange={onOpenChange}
          valueType="author"
          currentName="Robert Galbraith"
          onRenamed={onRenamed}
          {...props}
        />
      </QueryClientProvider>
    </SignalRContext.Provider>,
  );
}

describe("RenameValueDialog", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    signalR = makeSignalR();
    vi.mocked(similarValuesApi.rename).mockResolvedValue(undefined);
  });

  it("keeps Rename disabled until the name actually changes", () => {
    renderDialog();

    expect(screen.getByRole("button", { name: "Rename" })).toBeDisabled();
    expect(screen.getByText(/already this author's name/i)).toBeInTheDocument();
  });

  it("pre-fills a proposed name and renames with the trimmed values", async () => {
    renderDialog({ initialNewName: "J.K. Rowling" });

    fireEvent.click(screen.getByRole("button", { name: "Rename" }));

    await waitFor(() =>
      expect(similarValuesApi.rename).toHaveBeenCalledWith(
        "author",
        "Robert Galbraith",
        "J.K. Rowling",
      ),
    );
  });

  it("refuses a comma in an author name without calling the backend", () => {
    renderDialog({ initialNewName: "Rowling, J.K." });

    expect(screen.getByRole("button", { name: "Rename" })).toBeDisabled();
    expect(screen.getByText(/cannot contain a comma/i)).toBeInTheDocument();
  });

  it("refuses a series name carrying a qualifier suffix, using the backend's qualifier list", async () => {
    renderDialog({
      valueType: "series",
      currentName: "Jack Reacher",
      initialNewName: "Reacher (Dramatized)",
    });

    expect(await screen.findByText(/leave off "\(Dramatized\)"/i)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Rename" })).toBeDisabled();
  });

  it("reports success and hands the new name to the caller when every book was renamed", async () => {
    renderDialog({ initialNewName: "J.K. Rowling" });
    fireEvent.click(screen.getByRole("button", { name: "Rename" }));
    await waitFor(() => expect(similarValuesApi.rename).toHaveBeenCalled());

    act(() => {
      signalR.emit(SignalREvents.SimilarValueAlignComplete, {
        totalProcessed: 3,
        totalSucceeded: 3,
        totalFailed: 0,
      });
    });

    await waitFor(() => expect(onRenamed).toHaveBeenCalledWith("J.K. Rowling"));
    expect(notifications.success).toHaveBeenCalled();
    expect(onOpenChange).toHaveBeenCalledWith(false);
  });

  it("warns and stays open when some books failed, without handing over the new name", async () => {
    renderDialog({ initialNewName: "J.K. Rowling" });
    fireEvent.click(screen.getByRole("button", { name: "Rename" }));
    await waitFor(() => expect(similarValuesApi.rename).toHaveBeenCalled());

    act(() => {
      signalR.emit(SignalREvents.SimilarValueAlignComplete, {
        totalProcessed: 3,
        totalSucceeded: 2,
        totalFailed: 1,
      });
    });

    await waitFor(() => expect(notifications.warning).toHaveBeenCalled());
    expect(onRenamed).not.toHaveBeenCalled();
    expect(onOpenChange).not.toHaveBeenCalledWith(false);
  });

  it("ignores another operation's completion while it has not started a rename", () => {
    renderDialog({ initialNewName: "J.K. Rowling" });

    act(() => {
      signalR.emit(SignalREvents.SimilarValueAlignComplete, {
        totalProcessed: 1,
        totalSucceeded: 1,
        totalFailed: 0,
      });
    });

    expect(onRenamed).not.toHaveBeenCalled();
    expect(notifications.success).not.toHaveBeenCalled();
  });

  it("shows the backend's message and re-enables the form when the request is refused", async () => {
    vi.mocked(similarValuesApi.rename).mockRejectedValue(new Error("busy"));
    renderDialog({ initialNewName: "J.K. Rowling" });

    fireEvent.click(screen.getByRole("button", { name: "Rename" }));

    await waitFor(() => expect(notifications.error).toHaveBeenCalled());
    expect(screen.getByRole("button", { name: "Rename" })).not.toBeDisabled();
  });

  it("ignores a foreign completion that lands before a refused request, and shows no false success", async () => {
    let rejectRename: (e: Error) => void = () => {};
    vi.mocked(similarValuesApi.rename).mockReturnValue(
      new Promise((_, reject) => {
        rejectRename = reject;
      }),
    );
    renderDialog({ initialNewName: "J.K. Rowling" });
    fireEvent.click(screen.getByRole("button", { name: "Rename" }));
    await waitFor(() => expect(similarValuesApi.rename).toHaveBeenCalled());

    // Another client's operation finishes while our request is still in flight...
    act(() => {
      signalR.emit(SignalREvents.SimilarValueAlignComplete, {
        totalProcessed: 5,
        totalSucceeded: 5,
        totalFailed: 0,
      });
    });
    // ...and then the backend refuses ours (409: the shared lock was busy).
    await act(() => {
      rejectRename(new Error("An operation is already in progress"));
      return Promise.resolve();
    });

    await waitFor(() => expect(notifications.error).toHaveBeenCalled());
    expect(notifications.success).not.toHaveBeenCalled();
    expect(onRenamed).not.toHaveBeenCalled();
    expect(onOpenChange).not.toHaveBeenCalledWith(false);
  });

  it("handles a completion that beats the HTTP response once the request is accepted", async () => {
    let resolveRename: () => void = () => {};
    vi.mocked(similarValuesApi.rename).mockReturnValue(
      new Promise<void>((resolve) => {
        resolveRename = resolve;
      }),
    );
    renderDialog({ initialNewName: "J.K. Rowling" });
    fireEvent.click(screen.getByRole("button", { name: "Rename" }));
    await waitFor(() => expect(similarValuesApi.rename).toHaveBeenCalled());

    act(() => {
      signalR.emit(SignalREvents.SimilarValueAlignComplete, {
        totalProcessed: 0,
        totalSucceeded: 0,
        totalFailed: 0,
      });
    });
    expect(onRenamed).not.toHaveBeenCalled();

    await act(() => {
      resolveRename();
      return Promise.resolve();
    });

    await waitFor(() => expect(onRenamed).toHaveBeenCalledWith("J.K. Rowling"));
  });
});

import { toast } from "@/components/ui/toast";

/** Errors stay up longer than other toasts — they are the ones people need to read. */
export const DEFAULT_ERROR_TIMEOUT_MS = 10_000;

export interface NotifyOptions {
  /** Secondary line under the title, e.g. the server's error detail. */
  description?: string;
  /** Auto-dismiss delay in ms. Overrides the type's default. */
  timeout?: number;
  /** Stay until dismissed. Wins over `timeout` (the toast manager uses `timeout: 0` for this). */
  persistent?: boolean;
}

type NotifyType = "success" | "error" | "info" | "warning";

function notify(title: string, type: NotifyType, options: NotifyOptions = {}) {
  const { description, persistent, timeout } = options;
  const resolvedTimeout = persistent
    ? 0
    : (timeout ?? (type === "error" ? DEFAULT_ERROR_TIMEOUT_MS : undefined));
  return toast.add({
    title,
    type,
    ...(description !== undefined && { description }),
    ...(resolvedTimeout !== undefined && { timeout: resolvedTimeout }),
  });
}

export const notifications = {
  success: (title: string, options?: NotifyOptions) => notify(title, "success", options),
  error: (title: string, options?: NotifyOptions) => notify(title, "error", options),
  info: (title: string, options?: NotifyOptions) => notify(title, "info", options),
  warning: (title: string, options?: NotifyOptions) => notify(title, "warning", options),
};

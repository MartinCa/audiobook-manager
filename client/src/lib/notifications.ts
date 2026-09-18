import { toast } from "@/components/ui/toast";

export interface NotifyOptions {
  // 0 disables auto-dismiss; matches the underlying toast manager's `timeout` field.
  timeout?: number;
}

function notify(
  title: string,
  type: "success" | "error" | "info" | "warning",
  options?: NotifyOptions,
) {
  toast.add({ title, type, ...options });
}

export const notifications = {
  success: (title: string, options?: NotifyOptions) => notify(title, "success", options),
  error: (title: string, options?: NotifyOptions) => notify(title, "error", options),
  info: (title: string, options?: NotifyOptions) => notify(title, "info", options),
  warning: (title: string, options?: NotifyOptions) => notify(title, "warning", options),
};

import { onMounted, onUnmounted, Ref, ref } from "vue";
import { throttle } from "lodash";
import { HubEventToken, useSignalR } from "@/signalr/hub";
import OperationsService from "../services/OperationsService";

// Vuetify's determinate progress bar animates each value change with a ~0.2s CSS
// transition. Updating faster than that restarts the transition before it finishes, so the
// visible bar perpetually chases the real value instead of ever catching up. Throttling to
// roughly that interval keeps the animation coherent for operations that report progress
// once per item (which can be many times a second).
const DEFAULT_THROTTLE_MS = 250;

export interface OperationProgressOptions<TProgress, TComplete> {
  // Stable key identifying this operation server-side (see OperationStatusRegistry).
  key: string;
  progressToken: HubEventToken<TProgress>;
  completeToken: HubEventToken<TComplete>;
  getProcessed: (arg: TProgress) => number;
  getTotal: (arg: TProgress) => number;
  // Raw event hooks for any extra fields (message, succeeded/failed, ...) a component wants
  // to surface itself. Called synchronously, i.e. not throttled.
  onProgress?: (arg: TProgress) => void;
  onComplete?: (arg: TComplete) => void;
  throttleMs?: number;
}

export interface OperationProgress {
  isRunning: Ref<boolean>;
  processed: Ref<number>;
  total: Ref<number>;
  start: () => void;
}

export function useOperationProgress<TProgress, TComplete>(
  options: OperationProgressOptions<TProgress, TComplete>,
): OperationProgress {
  const signalR = useSignalR();

  const isRunning = ref(false);
  const processed = ref(0);
  const total = ref(0);

  const applyProgress = throttle(
    (newProcessed: number, newTotal: number) => {
      processed.value = newProcessed;
      total.value = newTotal;
    },
    options.throttleMs ?? DEFAULT_THROTTLE_MS,
    { leading: true, trailing: true },
  );

  // Called when the caller itself starts the operation, so the UI switches to "running"
  // immediately rather than waiting for the first progress event.
  const start = () => {
    isRunning.value = true;
    processed.value = 0;
    total.value = 0;
  };

  const handleProgress = (arg: TProgress) => {
    isRunning.value = true;
    applyProgress(options.getProcessed(arg), options.getTotal(arg));
    options.onProgress?.(arg);
  };

  const handleComplete = (arg: TComplete) => {
    applyProgress.cancel();
    isRunning.value = false;
    processed.value = 0;
    total.value = 0;
    options.onComplete?.(arg);
  };

  const refreshStatus = async () => {
    try {
      const status = await OperationsService.getStatus(options.key);
      applyProgress.cancel();
      isRunning.value = status.isRunning;
      processed.value = status.processed;
      total.value = status.total;
    } catch {
      // Leave current state as-is if the status check itself fails — a stale bar is no
      // worse than what we had before this refresh attempt.
    }
  };

  signalR.on(options.progressToken, handleProgress);
  signalR.on(options.completeToken, handleComplete);
  signalR.onReconnected(refreshStatus);

  onMounted(refreshStatus);

  onUnmounted(() => {
    signalR.off(options.progressToken, handleProgress);
    signalR.off(options.completeToken, handleComplete);
    signalR.offReconnected(refreshStatus);
    applyProgress.cancel();
  });

  return { isRunning, processed, total, start };
}

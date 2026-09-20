import { useMutation } from "@tanstack/react-query";
import { libraryApi } from "@/services/api";
import { handleApiError } from "@/lib/api";
import { notifications } from "@/lib/notifications";

/**
 * The single shared trigger for the combined "Scan Library" run (POST /api/library/scan — one
 * background operation that discovers new library files and then runs the full consistency
 * check). Used by both the Discovered Audiobooks page and the Library Consistency page so the
 * trigger cannot drift between them: same endpoint, same success toast, and failures surface the
 * backend's RFC 9457 problem-detail message (via handleApiError, like every other mutation here)
 * rather than a generic "request failed" string.
 *
 * The background run reports its own progress over SignalR (LibraryScanProgress/Complete during
 * discovery, then ConsistencyCheckProgress/Complete), so `isStarting` only covers the brief
 * start request itself — pages keep their own per-operation state through useOperationResync and
 * the SignalR handlers. On error the pending request rejects after the error toast, so a caller
 * that set optimistic busy state can unwind it in a catch.
 */
export function useStartLibraryScan() {
  const mutation = useMutation({
    mutationFn: () => libraryApi.startScan(),
    onSuccess: () => {
      notifications.success("Library scan started in background");
    },
    onError: (error: unknown) => {
      notifications.error(handleApiError(error).message);
    },
  });

  const startScan = async () => {
    await mutation.mutateAsync();
  };

  return { startScan, isStarting: mutation.isPending };
}

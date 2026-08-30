import type { components } from "@/lib/api-types";
import type { Require } from "@/lib/dto";

// AudiobookManager.Domain/AudiobookFileInfo.cs: fullPath/fileName/sizeInBytes are non-nullable
// and mirror the backend's AudiobookFileInfo. The queue* fields and error have no backend
// counterpart — they're frontend-only additions tracking SignalR organize-progress state.
type AudiobookFileInfoDto = Require<
  components["schemas"]["AudiobookFileInfo"],
  "fullPath" | "fileName" | "sizeInBytes"
>;

export interface BookFileInfo extends AudiobookFileInfoDto {
  queueId?: string;
  queueProgress?: number;
  queueMessage?: string;
  error?: string;
}

export type { BookFileInfo as default };

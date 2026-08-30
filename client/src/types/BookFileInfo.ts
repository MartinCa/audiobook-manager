export interface BookFileInfo {
  fullPath: string;
  fileName: string;
  sizeInBytes: number;
  queueId?: string;
  queueProgress?: number;
  queueMessage?: string;
  error?: string;
}

export type { BookFileInfo as default };

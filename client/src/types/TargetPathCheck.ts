export interface ExistingTargetFile {
  audiobookId?: number;
  sizeInBytes: number;
  durationInSeconds?: number;
}

export interface TargetPathCheckResult {
  targetPath: string;
  exists: boolean;
  existing?: ExistingTargetFile;
}

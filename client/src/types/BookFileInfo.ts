export default interface BookFileInfo {
  fullPath: string,
  fileName: string,
  sizeInBytes: number,
  queueId?: string,
  queueProgress?: number,
  queueMessage?: string,
  error?: string,
}

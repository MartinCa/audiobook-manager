import { pathsEqual } from "./pathHelpers";
import type { BookFileInfo } from "@/types/BookFileInfo";

/**
 * Normalizes a path by converting Windows backslashes to forward slashes
 * and trimming trailing slashes (except root '/').
 */
export function normalizePath(path: string): string {
  const normalized = path.trim().replace(/\\/g, "/");
  if (normalized.length > 1 && normalized.endsWith("/")) {
    return normalized.replace(/\/+$/, "");
  }
  return normalized;
}

/**
 * Extracts the containing folder path for a deletion target.
 *
 * If `files` is provided and non-empty:
 *   - If `targetPath` matches one of the files in `files`, extracts that file's parent directory.
 *   - If the first file's path starts with `targetPath + "/"`, then `targetPath` is the parent directory.
 *   - Otherwise extracts the parent directory of the first file.
 * If `files` is empty or not yet loaded:
 *   - If `targetPath` ends with a file extension, returns its parent directory.
 *   - Otherwise returns `targetPath` itself.
 */
export function getContainingFolderPath(targetPath: string, files?: BookFileInfo[]): string {
  if (!targetPath) return "";

  const cleanTarget = normalizePath(targetPath);

  if (files && files.length > 0) {
    // Case 1: targetPath is directly one of the files
    const matchingFile = files.find((f) => pathsEqual(f.fullPath, cleanTarget));
    if (matchingFile && matchingFile.fullPath) {
      const cleanFileFullPath = normalizePath(matchingFile.fullPath);
      const cleanFileName = matchingFile.fileName ? matchingFile.fileName.replace(/\\/g, "/") : "";

      if (cleanFileName && cleanFileFullPath.endsWith(cleanFileName)) {
        const dir = cleanFileFullPath.slice(0, -cleanFileName.length).replace(/\/+$/, "");
        if (dir) return dir;
      }

      const lastSlash = cleanFileFullPath.lastIndexOf("/");
      if (lastSlash > 0) {
        return cleanFileFullPath.substring(0, lastSlash);
      }
    }

    // Case 2: files are inside cleanTarget (cleanTarget is a directory)
    const firstFile = files[0];
    if (firstFile && firstFile.fullPath) {
      const cleanFirstFullPath = normalizePath(firstFile.fullPath);
      if (cleanFirstFullPath.startsWith(cleanTarget + "/")) {
        return cleanTarget;
      }

      // Case 3: fallback to first file's parent directory
      const cleanFileName = firstFile.fileName ? firstFile.fileName.replace(/\\/g, "/") : "";
      if (cleanFileName && cleanFirstFullPath.endsWith(cleanFileName)) {
        const dir = cleanFirstFullPath.slice(0, -cleanFileName.length).replace(/\/+$/, "");
        if (dir) return dir;
      }

      const lastSlash = cleanFirstFullPath.lastIndexOf("/");
      if (lastSlash > 0) {
        return cleanFirstFullPath.substring(0, lastSlash);
      }
    }
  }

  // If targetPath looks like a file (has an extension like .m4b, .mp3, .txt, etc.)
  const lastSlash = cleanTarget.lastIndexOf("/");
  const baseName = lastSlash !== -1 ? cleanTarget.substring(lastSlash + 1) : cleanTarget;
  if (baseName.includes(".") && lastSlash > 0) {
    return cleanTarget.substring(0, lastSlash);
  }

  return cleanTarget;
}

/**
 * Calculates the total size in bytes of a list of files.
 */
export function getTotalSizeInBytes(files: BookFileInfo[]): number {
  return files.reduce((acc, f) => acc + (f.sizeInBytes || 0), 0);
}

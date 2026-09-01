import { useQuery } from "@tanstack/react-query";
import { Folder, FileAudio, FileText, Image, File, Loader2 } from "lucide-react";
import { filesApi } from "@/services/api";
import { formatFileSize } from "@/helpers/formatHelpers";
import { getContainingFolderPath, getTotalSizeInBytes } from "@/helpers/folderHelpers";
import type { BookFileInfo } from "@/types/BookFileInfo";

export interface FolderDeleteContentsProps {
  targetPath?: string;
  files?: BookFileInfo[];
  isLoading?: boolean;
  showFolderPath?: boolean;
  folderPathLabel?: string;
  emptyMessage?: string;
  className?: string;
}

function getFileIcon(fileName: string) {
  const ext = fileName.toLowerCase().split(".").pop();
  if (ext === "m4b" || ext === "mp3" || ext === "m4a" || ext === "flac" || ext === "aac") {
    return <FileAudio className="text-primary h-3.5 w-3.5 shrink-0" />;
  }
  if (ext === "jpg" || ext === "jpeg" || ext === "png" || ext === "webp") {
    return <Image className="h-3.5 w-3.5 shrink-0 text-sky-500" />;
  }
  if (ext === "txt" || ext === "nfo" || ext === "json") {
    return <FileText className="text-muted-foreground h-3.5 w-3.5 shrink-0" />;
  }
  return <File className="text-muted-foreground h-3.5 w-3.5 shrink-0" />;
}

export function FolderDeleteContents({
  targetPath,
  files: passedFiles,
  isLoading: passedLoading,
  showFolderPath = true,
  folderPathLabel = "Folder to be deleted:",
  emptyMessage = "Folder is empty (0 B)",
  className = "",
}: FolderDeleteContentsProps) {
  const queryEnabled = Boolean(targetPath) && passedFiles === undefined;

  const { data: fetchedFiles = [], isLoading: queryLoading } = useQuery({
    queryKey: ["directoryContents", targetPath],
    queryFn: () => filesApi.getDirectoryContents(targetPath!),
    enabled: queryEnabled,
  });

  const files = passedFiles ?? fetchedFiles;
  const loading = passedLoading ?? (queryEnabled && queryLoading);

  const folderPath = targetPath ? getContainingFolderPath(targetPath, files) : "";
  const totalSize = getTotalSizeInBytes(files);

  return (
    <div className={`space-y-3 ${className}`}>
      {showFolderPath && folderPath && (
        <div className="space-y-1">
          <div className="text-muted-foreground flex items-center justify-between text-xs font-medium">
            <span className="flex items-center gap-1.5">
              <Folder className="h-3.5 w-3.5 text-amber-500" />
              {folderPathLabel}
            </span>
            {!loading && files.length > 0 && (
              <span className="text-muted-foreground font-mono text-[11px]">
                {files.length} {files.length === 1 ? "file" : "files"} ({formatFileSize(totalSize)})
              </span>
            )}
          </div>
          <div
            data-testid="folder-path-display"
            className="border-border bg-muted/60 text-foreground rounded-md border p-2 font-mono text-xs break-all select-all"
            title={folderPath}
          >
            {folderPath}
          </div>
        </div>
      )}

      <div className="space-y-1.5">
        <div className="text-muted-foreground text-xs font-medium">Contained contents:</div>

        {loading ? (
          <div className="text-muted-foreground flex items-center gap-2 py-3 text-xs">
            <Loader2 className="text-primary h-3.5 w-3.5 animate-spin" />
            Listing folder contents...
          </div>
        ) : files.length > 0 ? (
          <ul className="space-y-1">
            {files.map((f) => (
              <li
                key={f.fullPath}
                className="border-border bg-muted/40 flex items-center justify-between gap-2 rounded border px-2.5 py-1.5 text-xs"
              >
                <div className="flex min-w-0 items-center gap-2">
                  {getFileIcon(f.fileName)}
                  <span className="text-foreground truncate font-mono text-[11px]">
                    {f.fileName}
                  </span>
                </div>
                <span className="text-muted-foreground shrink-0 font-mono text-[11px]">
                  {formatFileSize(f.sizeInBytes)}
                </span>
              </li>
            ))}
          </ul>
        ) : (
          <div className="border-border bg-muted/30 text-muted-foreground rounded border border-dashed p-3 text-center text-xs">
            {emptyMessage}
          </div>
        )}
      </div>
    </div>
  );
}

export default FolderDeleteContents;

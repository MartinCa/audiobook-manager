import { Clock, HardDrive, FileText } from "lucide-react";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { formatDuration, formatFileSize } from "@/helpers/formatHelpers";

export interface AudiobookFileDetailsProps {
  filePath?: string | null;
  sizeInBytes?: number | null;
  durationInSeconds?: number | null;
  className?: string;
}

export function AudiobookFileDetails({
  filePath,
  sizeInBytes,
  durationInSeconds,
  className,
}: AudiobookFileDetailsProps) {
  return (
    <Card className={className}>
      <CardHeader>
        <CardTitle className="text-muted-foreground text-sm font-semibold uppercase">
          Technical Details
        </CardTitle>
      </CardHeader>
      <CardContent className="space-y-3 text-xs">
        <div className="flex items-center gap-2">
          <Clock className="text-muted-foreground h-4 w-4" />
          <span className="text-foreground font-medium">Duration:</span>
          <span>{durationInSeconds ? formatDuration(durationInSeconds) : "Unknown"}</span>
        </div>

        <div className="flex items-center gap-2">
          <HardDrive className="text-muted-foreground h-4 w-4" />
          <span className="text-foreground font-medium">File Size:</span>
          <span>{formatFileSize(sizeInBytes)}</span>
        </div>

        {filePath && (
          <div className="space-y-1">
            <div className="flex items-center gap-2">
              <FileText className="text-muted-foreground h-4 w-4" />
              <span className="text-foreground font-medium">File Path:</span>
            </div>
            <div
              className="bg-muted/60 text-muted-foreground rounded p-2 font-mono text-[11px] break-all"
              title={filePath}
            >
              {filePath}
            </div>
          </div>
        )}
      </CardContent>
    </Card>
  );
}

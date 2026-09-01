import { Progress } from "@/components/ui/progress";
import { cn } from "@/lib/utils";

interface OperationProgressBarProps {
  processed: number;
  total: number;
  label?: string;
  subText?: string;
  compact?: boolean;
  className?: string;
}

export function OperationProgressBar({
  processed,
  total,
  label = "Processing...",
  subText,
  compact = false,
  className,
}: OperationProgressBarProps) {
  const percentage = total > 0 ? Math.min(100, Math.round((processed / total) * 100)) : 0;

  if (compact) {
    return (
      <div className={cn("space-y-1 text-xs", className)}>
        <div className="flex items-center justify-between gap-2 font-medium">
          <span className="text-foreground min-w-0 flex-1 truncate text-left">{label}</span>
          <span className="text-muted-foreground shrink-0">{percentage}%</span>
        </div>
        <Progress value={percentage} className="h-1.5" />
        {subText && (
          <p className="text-muted-foreground truncate text-left text-[10px]">{subText}</p>
        )}
      </div>
    );
  }

  return (
    <div className={cn("border-border bg-card space-y-2 rounded-lg border p-4", className)}>
      <div className="flex flex-wrap items-center justify-between gap-1 text-sm font-medium">
        <span className="min-w-0 flex-1 truncate">{label}</span>
        <span className="shrink-0">
          {processed} / {total} ({percentage}%)
        </span>
      </div>
      <Progress value={percentage} className="h-2" />
      {subText && <p className="text-muted-foreground text-xs">{subText}</p>}
    </div>
  );
}

export default OperationProgressBar;

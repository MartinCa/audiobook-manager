import { Progress } from "@/components/ui/progress";

interface OperationProgressBarProps {
  processed: number;
  total: number;
  label?: string;
  subText?: string;
}

export function OperationProgressBar({
  processed,
  total,
  label = "Processing...",
  subText,
}: OperationProgressBarProps) {
  const percentage = total > 0 ? Math.min(100, Math.round((processed / total) * 100)) : 0;

  return (
    <div className="border-border bg-card space-y-2 rounded-lg border p-4">
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

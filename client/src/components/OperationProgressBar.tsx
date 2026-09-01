import React from "react";
import { Progress } from "@/components/ui/progress";

interface OperationProgressBarProps {
  processed: number;
  total: number;
  label?: string;
  subText?: string;
}

export const OperationProgressBar: React.FC<OperationProgressBarProps> = ({
  processed,
  total,
  label = "Processing...",
  subText,
}) => {
  const percentage =
    total > 0 ? Math.min(100, Math.round((processed / total) * 100)) : 0;

  return (
    <div className="space-y-2 p-4 border border-border rounded-lg bg-card">
      <div className="flex justify-between items-center text-sm font-medium">
        <span>{label}</span>
        <span>
          {processed} / {total} ({percentage}%)
        </span>
      </div>
      <Progress
        value={percentage}
        className="h-2"
      />
      {subText && <p className="text-xs text-muted-foreground">{subText}</p>}
    </div>
  );
};
export default OperationProgressBar;

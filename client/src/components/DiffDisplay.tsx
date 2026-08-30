import React from "react";
import { diffWords } from "diff";

interface DiffDisplayProps {
  original?: string;
  modified?: string;
}

export const DiffDisplay: React.FC<DiffDisplayProps> = ({ original = "", modified = "" }) => {
  const diffs = diffWords(original, modified);

  return (
    <div className="p-3 border border-border rounded-md bg-muted/50 font-mono text-xs whitespace-pre-wrap leading-relaxed">
      {diffs.map((part, index) => {
        if (part.added) {
          return (
            <span key={index} className="bg-emerald-500/20 text-emerald-600 dark:text-emerald-400 font-semibold px-0.5 rounded">
              {part.value}
            </span>
          );
        }
        if (part.removed) {
          return (
            <span key={index} className="bg-rose-500/20 text-rose-600 dark:text-rose-400 line-through px-0.5 rounded">
              {part.value}
            </span>
          );
        }
        return <span key={index}>{part.value}</span>;
      })}
    </div>
  );
};
export default DiffDisplay;

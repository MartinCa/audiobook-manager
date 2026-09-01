import { diffChars, type Change } from "diff";

interface DiffDisplayProps {
  expected?: string;
  actual?: string;
  original?: string;
  modified?: string;
}

export function DiffDisplay({ expected, actual, original, modified }: DiffDisplayProps) {
  const oldText = actual ?? original ?? "";
  const newText = expected ?? modified ?? "";
  const diffs: Change[] = diffChars(oldText, newText);

  return (
    <div className="border-border bg-muted/50 rounded-md border p-3 font-mono text-xs leading-relaxed break-all whitespace-pre-wrap">
      {diffs.map((part, index) => {
        if (part.added) {
          return (
            <span
              key={index}
              className="rounded bg-emerald-500/20 px-0.5 font-semibold text-emerald-600 dark:text-emerald-400"
            >
              {part.value}
            </span>
          );
        }
        if (part.removed) {
          return (
            <span
              key={index}
              className="rounded bg-rose-500/20 px-0.5 text-rose-600 line-through dark:text-rose-400"
            >
              {part.value}
            </span>
          );
        }
        return <span key={index}>{part.value}</span>;
      })}
    </div>
  );
}

export default DiffDisplay;

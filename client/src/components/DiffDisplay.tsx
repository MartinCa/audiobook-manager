import { diffChars, type Change } from "diff";

interface DiffDisplayProps {
  expected?: string;
  actual?: string;
  original?: string;
  modified?: string;
}

interface TagMismatchDiffDisplayProps {
  expected: string;
  actual: string;
}

type SerializedField = {
  field: string;
  value: string;
};

function parseJsonFields(serialized: string): SerializedField[] | null {
  if (!serialized.trimStart().startsWith("[")) return null;

  try {
    const parsed: unknown = JSON.parse(serialized);
    if (!Array.isArray(parsed)) return null;
    const fields = parsed as unknown[];
    if (
      fields.some(
        (entry) =>
          entry === null ||
          typeof entry !== "object" ||
          typeof (entry as SerializedField).field !== "string" ||
          typeof (entry as SerializedField).value !== "string",
      )
    ) {
      return null;
    }
    return fields as SerializedField[];
  } catch {
    return null;
  }
}

// The JSON payload is the only supported TagMismatch serialization. Anything else falls back to
// a single whole-value diff below rather than being guessed at field by field.
function parseTagMismatchFields(
  expected: string,
  actual: string,
): Array<{ field: string; expected: string; actual: string }> {
  const expectedFields = parseJsonFields(expected);
  const actualFields = parseJsonFields(actual);
  if (!expectedFields || !actualFields) return [];

  // Field names come from the payload, not the description - the description is display text and
  // must agree with the payload, but the payload is the source of truth.
  return expectedFields.map(({ field, value }) => ({
    field,
    expected: value,
    actual: actualFields.find((entry) => entry.field === field)?.value ?? "",
  }));
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

export function TagMismatchDiffDisplay({ expected, actual }: TagMismatchDiffDisplayProps) {
  const fields = parseTagMismatchFields(expected, actual);

  if (fields.length === 0) {
    return <DiffDisplay expected={expected} actual={actual} />;
  }

  return (
    <div className="space-y-2">
      {fields.map((field) => (
        <div key={field.field}>
          <div className="text-muted-foreground mb-1 text-[11px] font-semibold">{field.field}</div>
          <DiffDisplay expected={field.expected} actual={field.actual} />
        </div>
      ))}
    </div>
  );
}

export default DiffDisplay;

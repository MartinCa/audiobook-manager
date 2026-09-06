import { diffChars, type Change } from "diff";

interface DiffDisplayProps {
  expected?: string;
  actual?: string;
  original?: string;
  modified?: string;
}

interface TagMismatchDiffDisplayProps {
  description: string;
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

function escapeRegExp(value: string): string {
  return value.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
}

// Legacy format for issues stored before the JSON payload: newline-separated "Field: value"
// lines. The marker scan is a heuristic - a free-text value can contain a line starting with
// "Publisher: " - so it is only ever a fallback for rows the consistency check has not
// rewritten yet; newly detected issues serialize as unambiguous JSON above.
function parseSerializedFields(serialized: string, fieldNames: string[]): SerializedField[] {
  const fields: Array<{ field: string; start: number; valueStart: number }> = [];
  let searchFrom = 0;

  for (const field of fieldNames) {
    const marker = new RegExp(`(?:^|\\n)${escapeRegExp(field)}: ?`, "g");
    marker.lastIndex = searchFrom;
    const match = marker.exec(serialized);
    if (!match) continue;

    const markerStart = match.index + (serialized[match.index] === "\n" ? 1 : 0);
    fields.push({ field, start: markerStart, valueStart: match.index + match[0].length });
    searchFrom = match.index + match[0].length;
  }

  return fields.map((field, index) => ({
    field: field.field,
    value: serialized
      .slice(field.valueStart, fields[index + 1]?.start ?? serialized.length)
      .replace(/\n$/, ""),
  }));
}

function parseTagMismatchFields(
  description: string,
  expected: string,
  actual: string,
): Array<{ field: string; expected: string; actual: string }> {
  const fieldList = description.match(/m4b tags do not match library metadata:\s*(.*)$/)?.[1];
  if (!fieldList) return [];

  const expectedJson = parseJsonFields(expected);
  const actualJson = parseJsonFields(actual);

  // Legacy line-based fallback: the description still names the fields, and the values are
  // matched by their "Field: " markers.
  const fieldNames = fieldList
    .split(",")
    .map((field) => field.trim())
    .filter(Boolean);

  const expectedFields = expectedJson ?? parseSerializedFields(expected, fieldNames);
  const actualFields = actualJson ?? parseSerializedFields(actual, fieldNames);

  // When both sides carry the JSON payload, field names come from it - the description is
  // display text and must agree with the payload, but the payload is the source of truth.
  const fields = expectedJson && actualJson ? expectedJson.map((f) => f.field) : fieldNames;

  return fields.map((field) => ({
    field,
    expected: expectedFields.find((value) => value.field === field)?.value ?? "",
    actual: actualFields.find((value) => value.field === field)?.value ?? "",
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

export function TagMismatchDiffDisplay({
  description,
  expected,
  actual,
}: TagMismatchDiffDisplayProps) {
  const fields = parseTagMismatchFields(description, expected, actual);

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

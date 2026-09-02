import { useState, useMemo } from "react";
import type { TagMismatchField } from "@/types/TagMismatchField";
import { useQuery } from "@tanstack/react-query";
import { Dialog, DialogContent, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Button } from "@/components/ui/button";
import { RadioGroup, RadioGroupItem } from "@/components/ui/radio-group";
import { consistencyApi } from "@/services/api";
import type { ConsistencyIssue } from "@/types/ConsistencyIssue";

export type TagFieldChoice = "library" | "file" | "empty";

const DEFAULT_CHOICE: TagFieldChoice = "library";

function initialChoicesFor(fields: TagMismatchField[] | undefined): Record<string, TagFieldChoice> {
  const initial: Record<string, TagFieldChoice> = {};
  for (const field of fields ?? []) {
    initial[field.field] = DEFAULT_CHOICE;
  }
  return initial;
}

interface TagMismatchResolveDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  issue: ConsistencyIssue | null;
  onResolve: (issueId: number, fieldValues: Record<string, string | null>) => Promise<void>;
}

function ValueCell({ value }: { value: string | null | undefined }) {
  if (value === null || value === undefined || value.length === 0) {
    return <span className="text-muted-foreground italic">—</span>;
  }
  return <span className="break-all">{value}</span>;
}

export function TagMismatchResolveDialog({
  open,
  onOpenChange,
  issue,
  onResolve,
}: TagMismatchResolveDialogProps) {
  const { data: fields, isFetching } = useQuery({
    queryKey: ["tag-mismatch", issue?.id],
    queryFn: () => consistencyApi.getTagMismatch(issue!.id),
    enabled: open && issue != null,
    staleTime: 30_000,
  });

  // Key the state by issue id so per-field choices reset when a new issue opens.
  const [choicesByIssue, setChoicesByIssue] = useState<
    Record<number, Record<string, TagFieldChoice>>
  >({});
  const [submitting, setSubmitting] = useState(false);

  const choices = useMemo(
    () => (issue ? (choicesByIssue[issue.id] ?? initialChoicesFor(fields)) : {}),
    [choicesByIssue, issue, fields],
  );

  const setChoice = (field: string, value: TagFieldChoice) => {
    if (!issue) return;
    const base = choicesByIssue[issue.id] ?? initialChoicesFor(fields);
    setChoicesByIssue((prev) => ({
      ...prev,
      [issue.id]: { ...base, [field]: value },
    }));
  };

  const hasNonLibraryChoice = useMemo(
    () => Object.values(choices).some((c) => c !== "library"),
    [choices],
  );

  const handleSubmit = async () => {
    if (!issue || !fields) return;
    setSubmitting(true);
    try {
      const fieldValues: Record<string, string | null> = {};
      for (const field of fields) {
        const choice = choices[field.field] ?? "library";
        fieldValues[field.field] =
          choice === "library"
            ? (field.libraryValue ?? null)
            : choice === "file"
              ? (field.fileValue ?? null)
              : null;
      }
      await onResolve(issue.id, fieldValues);
      onOpenChange(false);
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="flex max-h-[90dvh] w-[calc(100vw-2rem)] flex-col overflow-hidden p-4 sm:max-w-3xl sm:p-6">
        <DialogHeader>
          <DialogTitle>Resolve Tag Mismatch</DialogTitle>
        </DialogHeader>

        <div className="flex-1 space-y-4 overflow-y-auto py-2 text-xs">
          <p className="text-muted-foreground">
            Choose which value to keep for each differing field. Each row offers the value in the
            library ({issue?.bookName ?? ""}
            &rsquo;s metadata) and the value embedded in the file&rsquo;s tags.
          </p>

          {isFetching || !fields ? (
            <div className="bg-muted/40 border-border space-y-2 rounded-md border p-3">
              <div className="bg-muted h-9 w-full animate-pulse rounded" />
              <div className="bg-muted h-9 w-full animate-pulse rounded" />
              <div className="bg-muted h-9 w-full animate-pulse rounded" />
            </div>
          ) : fields.length === 0 ? (
            <p className="text-muted-foreground">No differing fields found.</p>
          ) : (
            <div className="border-border overflow-x-auto rounded-md border">
              <table className="w-full border-collapse text-left text-xs">
                <thead className="bg-muted/70 text-muted-foreground sticky top-0 z-10 border-b">
                  <tr>
                    <th className="w-28 p-2 sm:w-32">Field</th>
                    <th className="min-w-[120px] p-2">Library</th>
                    <th className="min-w-[120px] p-2">File</th>
                    <th className="w-20 p-2 text-center">Keep Neither</th>
                  </tr>
                </thead>
                <tbody className="divide-border divide-y">
                  {fields.map((field) => {
                    const choice = choices[field.field] ?? "library";
                    return (
                      <tr
                        key={field.field}
                        className={
                          choice !== "library" ? "bg-muted/20 font-medium" : "text-muted-foreground"
                        }
                      >
                        <td className="text-foreground p-2 align-top font-semibold">
                          {field.field}
                        </td>
                        <td className="p-2">
                          <RadioGroup
                            value={choice}
                            onValueChange={(value: TagFieldChoice) => {
                              setChoice(field.field, value);
                            }}
                          >
                            <label
                              htmlFor={`${field.field}-library`}
                              className="flex cursor-pointer items-start gap-2"
                            >
                              <RadioGroupItem
                                value="library"
                                id={`${field.field}-library`}
                                className="shrink-0"
                              />
                              <span
                                className={
                                  choice === "library"
                                    ? "text-foreground font-medium"
                                    : "text-muted-foreground"
                                }
                              >
                                <ValueCell value={field.libraryValue} />
                              </span>
                            </label>
                          </RadioGroup>
                        </td>
                        <td className="p-2">
                          <RadioGroup
                            value={choice}
                            onValueChange={(value: TagFieldChoice) => {
                              setChoice(field.field, value);
                            }}
                          >
                            <label
                              htmlFor={`${field.field}-file`}
                              className="flex cursor-pointer items-start gap-2"
                            >
                              <RadioGroupItem
                                value="file"
                                id={`${field.field}-file`}
                                className="shrink-0"
                              />
                              <span
                                className={
                                  choice === "file"
                                    ? "text-foreground font-medium"
                                    : "text-muted-foreground"
                                }
                              >
                                <ValueCell value={field.fileValue} />
                              </span>
                            </label>
                          </RadioGroup>
                        </td>
                        <td className="p-2 text-center">
                          <RadioGroup
                            value={choice}
                            onValueChange={(value: TagFieldChoice) => {
                              setChoice(field.field, value);
                            }}
                          >
                            <label
                              htmlFor={`${field.field}-empty`}
                              className="flex cursor-pointer items-center justify-center"
                            >
                              <RadioGroupItem
                                value="empty"
                                id={`${field.field}-empty`}
                                aria-label={`Clear ${field.field}`}
                              />
                            </label>
                          </RadioGroup>
                        </td>
                      </tr>
                    );
                  })}
                </tbody>
              </table>
              {hasNonLibraryChoice && (
                <p className="bg-muted/40 text-muted-foreground border-t p-2 text-[11px]">
                  Rows marked <strong className="text-foreground">Keep Neither</strong> will have
                  that field cleared in both the file and the library.
                </p>
              )}
            </div>
          )}
        </div>

        <div className="border-border flex flex-col-reverse items-stretch justify-end gap-2 border-t pt-3 sm:flex-row sm:items-center sm:pt-4">
          <Button
            variant="outline"
            className="w-full sm:w-auto"
            onClick={() => onOpenChange(false)}
          >
            Cancel
          </Button>
          <Button
            className="w-full sm:w-auto"
            disabled={submitting || isFetching || !fields || fields.length === 0}
            onClick={() => void handleSubmit()}
          >
            {submitting ? "Applying..." : "Apply Choices"}
          </Button>
        </div>
      </DialogContent>
    </Dialog>
  );
}

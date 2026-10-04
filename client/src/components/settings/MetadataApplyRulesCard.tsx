import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Loader2, Save, Wand2 } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { settingsApi } from "@/services/api";
import { queryKeys } from "@/lib/queryKeys";
import { handleApiError } from "@/lib/api";
import { notifications } from "@/lib/notifications";
import type {
  AutomatedApplyRule,
  InteractiveApplyRule,
  MetadataApplyField,
  MetadataApplyOption,
} from "@/types/MetadataApplyRules";

interface Draft {
  interactive: InteractiveApplyRule;
  automated: AutomatedApplyRule;
}

function OptionLegend({ title, options }: { title: string; options: MetadataApplyOption[] }) {
  return (
    <div className="space-y-1">
      <h4 className="text-xs font-semibold">{title}</h4>
      <dl className="space-y-1">
        {options.map((o) => (
          <div key={o.key} className="text-muted-foreground text-xs">
            <dt className="text-foreground inline font-medium">{o.label}: </dt>
            <dd className="inline">{o.description}</dd>
          </div>
        ))}
      </dl>
    </div>
  );
}

/**
 * Per-field rules for applying online metadata: whether a review starts with the field ticked
 * ("when reviewing") and what a bulk or scheduled refresh does with it ("when automated"). The
 * fields, the options, their explanations and which combinations are refused all come from the
 * server, so nothing about them is hardcoded here.
 */
export function MetadataApplyRulesCard() {
  const queryClient = useQueryClient();
  // Rows the user has changed, by backend field name; empty until they touch anything, so the
  // saved rules show through.
  const [draft, setDraft] = useState<Record<string, Draft>>({});

  const { data, isLoading } = useQuery({
    queryKey: queryKeys.metadataApplyRules(),
    queryFn: () => settingsApi.getMetadataApplyRules(),
  });

  const mutation = useMutation({
    mutationFn: (
      changed: {
        field: string;
        interactive: InteractiveApplyRule;
        automated: AutomatedApplyRule;
      }[],
    ) => settingsApi.updateMetadataApplyRules({ rules: changed }),
    onSuccess: (saved) => {
      notifications.success("Online metadata handling saved");
      setDraft({});
      queryClient.setQueryData(queryKeys.metadataApplyRules(), saved);
    },
    onError: (err: unknown) => {
      notifications.error(handleApiError(err).message);
    },
  });

  const effective = (f: MetadataApplyField): Draft =>
    draft[f.field] ?? { interactive: f.interactive, automated: f.automated };

  const update = (f: MetadataApplyField, patch: Partial<Draft>) => {
    const next = { ...effective(f), ...patch };
    const unchanged = next.interactive === f.interactive && next.automated === f.automated;
    setDraft((prev) => {
      const rest = { ...prev };
      delete rest[f.field];
      return unchanged ? rest : { ...rest, [f.field]: next };
    });
  };

  const changedRules = Object.entries(draft).map(([field, rule]) => ({ field, ...rule }));

  return (
    <Card>
      <CardHeader>
        <CardTitle className="flex items-center gap-2 text-lg">
          <Wand2 className="text-primary h-5 w-5" />
          Online metadata handling
        </CardTitle>
        <CardDescription>
          Decide, field by field, what happens when metadata fetched from an online source differs
          from what a book has. <strong>When reviewing</strong> is for searches and refreshes you
          confirm yourself: it only sets whether the field starts ticked, and you can always change
          it. <strong>When automated</strong> is for bulk and scheduled refreshes, where nobody is
          there to confirm: the field is settled by its rule, unless a field set to <em>Ask me</em>{" "}
          differs, in which case nothing is applied for that book and its whole changeset waits for
          your review (using the &quot;when reviewing&quot; rules). The defaults,{" "}
          <em>Always select</em> and <em>Ask me</em>, are how the app always worked.
        </CardDescription>
      </CardHeader>
      <CardContent className="space-y-4">
        {isLoading || !data ? (
          <div className="text-muted-foreground flex items-center justify-center py-8">
            <Loader2 className="text-primary mr-2 h-5 w-5 animate-spin" />
            <span className="text-sm">Loading rules...</span>
          </div>
        ) : (
          <>
            {/* Column titles on wide screens; on a phone each select carries its own title instead. */}
            <div
              aria-hidden
              className="text-muted-foreground hidden gap-2 text-xs font-semibold sm:flex"
            >
              <span className="w-32">Field</span>
              <span className="w-56">When reviewing</span>
              <span className="w-64">When automated</span>
            </div>
            <div className="space-y-5 sm:space-y-3">
              {data.fields.map((f) => {
                const rule = effective(f);
                const showWarning =
                  rule.automated === "AlwaysOverwrite" && Boolean(f.alwaysOverwriteWarning);
                return (
                  <div key={f.field} className="space-y-1.5">
                    <div className="flex flex-col gap-2 sm:flex-row sm:items-center">
                      <span className="text-sm font-medium sm:w-32">{f.label}</span>
                      <div className="space-y-1 sm:w-56">
                        <span aria-hidden className="text-muted-foreground block text-xs sm:hidden">
                          When reviewing
                        </span>
                        <Select
                          value={rule.interactive}
                          onValueChange={(v) => {
                            if (v != null) update(f, { interactive: v });
                          }}
                          items={data.interactiveOptions.map((o) => ({
                            value: o.key,
                            label: o.label,
                          }))}
                          disabled={mutation.isPending}
                        >
                          <SelectTrigger
                            className="w-full"
                            aria-label={`${f.label}: when reviewing`}
                          >
                            <SelectValue />
                          </SelectTrigger>
                          <SelectContent>
                            {data.interactiveOptions.map((o) => (
                              <SelectItem key={o.key} value={o.key}>
                                {o.label}
                              </SelectItem>
                            ))}
                          </SelectContent>
                        </Select>
                      </div>
                      <div className="space-y-1 sm:w-64">
                        <span aria-hidden className="text-muted-foreground block text-xs sm:hidden">
                          When automated
                        </span>
                        <Select
                          value={rule.automated}
                          onValueChange={(v) => {
                            if (v != null) update(f, { automated: v });
                          }}
                          items={data.automatedOptions.map((o) => ({
                            value: o.key,
                            label: o.label,
                          }))}
                          disabled={mutation.isPending}
                        >
                          <SelectTrigger
                            className="w-full"
                            aria-label={`${f.label}: when automated`}
                          >
                            <SelectValue />
                          </SelectTrigger>
                          <SelectContent>
                            {data.automatedOptions.map((o) => (
                              <SelectItem
                                key={o.key}
                                value={o.key}
                                disabled={o.key === "AlwaysOverwrite" && !f.alwaysOverwriteAllowed}
                              >
                                {o.key === "AlwaysOverwrite" && !f.alwaysOverwriteAllowed
                                  ? `${o.label} (not available: required field)`
                                  : o.label}
                              </SelectItem>
                            ))}
                          </SelectContent>
                        </Select>
                      </div>
                    </div>
                    {showWarning && (
                      <p role="alert" className="text-destructive text-xs sm:ml-32">
                        {f.alwaysOverwriteWarning}
                      </p>
                    )}
                  </div>
                );
              })}
            </div>

            <div className="grid gap-4 border-t pt-4 sm:grid-cols-2">
              <OptionLegend title="When reviewing" options={data.interactiveOptions} />
              <OptionLegend title="When automated" options={data.automatedOptions} />
            </div>

            <div className="flex justify-end">
              <Button
                type="button"
                size="sm"
                disabled={mutation.isPending || changedRules.length === 0}
                onClick={() => mutation.mutate(changedRules)}
              >
                {mutation.isPending ? (
                  <Loader2 className="mr-1.5 h-4 w-4 animate-spin" />
                ) : (
                  <Save className="mr-1.5 h-4 w-4" />
                )}
                Save rules
              </Button>
            </div>
          </>
        )}
      </CardContent>
    </Card>
  );
}

export default MetadataApplyRulesCard;

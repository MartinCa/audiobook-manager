import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Loader2, Save, UserCheck } from "lucide-react";
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
  MetadataChoice,
  MetadataChoiceOption,
  SourceNamesMainRule,
  SourceSilentRule,
} from "@/types/MetadataChoiceRules";

interface Draft {
  whenSourceNamesMain: SourceNamesMainRule;
  whenSourceSilent: SourceSilentRule;
}

function OptionLegend({ title, options }: { title: string; options: MetadataChoiceOption[] }) {
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
 * How a book's main author and primary series are chosen when an online source lists several. A
 * source that names a main one itself (Goodreads' primary contributor, Hardcover's featured series)
 * and one that is silent are different situations, so each choice has a rule for both. The choices,
 * options, explanations and warnings all come from the server; nothing about them is hardcoded here.
 * "Ask me" matters only when a book's Authors/Series field is being applied unattended: the book
 * then waits for review instead of being applied.
 */
export function MetadataChoiceRulesCard() {
  const queryClient = useQueryClient();
  // Choices the user has changed, by backend key; empty until they touch anything, so the saved
  // rules show through.
  const [draft, setDraft] = useState<Record<string, Draft>>({});

  const { data, isLoading } = useQuery({
    queryKey: queryKeys.metadataChoiceRules(),
    queryFn: () => settingsApi.getMetadataChoiceRules(),
  });

  const mutation = useMutation({
    mutationFn: (
      changed: {
        choice: string;
        whenSourceNamesMain: SourceNamesMainRule;
        whenSourceSilent: SourceSilentRule;
      }[],
    ) => settingsApi.updateMetadataChoiceRules({ rules: changed }),
    onSuccess: (saved) => {
      notifications.success("Main author and primary series handling saved");
      setDraft({});
      queryClient.setQueryData(queryKeys.metadataChoiceRules(), saved);
    },
    onError: (err: unknown) => {
      notifications.error(handleApiError(err).message);
    },
  });

  const effective = (c: MetadataChoice): Draft =>
    draft[c.choice] ?? {
      whenSourceNamesMain: c.whenSourceNamesMain,
      whenSourceSilent: c.whenSourceSilent,
    };

  const update = (c: MetadataChoice, patch: Partial<Draft>) => {
    const next = { ...effective(c), ...patch };
    const unchanged =
      next.whenSourceNamesMain === c.whenSourceNamesMain &&
      next.whenSourceSilent === c.whenSourceSilent;
    setDraft((prev) => {
      const rest = { ...prev };
      delete rest[c.choice];
      return unchanged ? rest : { ...rest, [c.choice]: next };
    });
  };

  const changedRules = Object.entries(draft).map(([choice, rule]) => ({ choice, ...rule }));

  return (
    <Card>
      <CardHeader>
        <CardTitle className="flex items-center gap-2 text-lg">
          <UserCheck className="text-primary h-5 w-5" />
          Main author and primary series
        </CardTitle>
        <CardDescription>
          When an online source lists several authors or several series, which one is the
          book&apos;s main author and primary series? Some sources say which they consider the main
          one and some do not, so each choice has a rule for{" "}
          <strong>when the source names a main one</strong> and one for{" "}
          <strong>when it is silent</strong>. In a review you can always pick by hand; these rules
          set what is pre-selected and what bulk and scheduled refreshes do. <em>Ask me</em> only
          applies while an Authors or Series change is being applied unattended: the book then waits
          for your review. The defaults, <em>Keep current</em>, are how the app always worked.
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
            <div className="space-y-6">
              {data.choices.map((c) => {
                const rule = effective(c);
                return (
                  <div key={c.choice} className="space-y-2">
                    <div>
                      <h3 className="text-sm font-medium">{c.label}</h3>
                      <p className="text-muted-foreground text-xs">{c.description}</p>
                    </div>
                    <div className="grid gap-3 sm:grid-cols-2">
                      <div className="space-y-1">
                        <span className="text-muted-foreground block text-xs">
                          When the source names a main one
                        </span>
                        <Select
                          value={rule.whenSourceNamesMain}
                          onValueChange={(v) => {
                            if (v != null) update(c, { whenSourceNamesMain: v });
                          }}
                          items={c.namesMainOptions.map((o) => ({ value: o.key, label: o.label }))}
                          disabled={mutation.isPending}
                        >
                          <SelectTrigger
                            className="w-full"
                            aria-label={`${c.label}: when the source names a main one`}
                          >
                            <SelectValue />
                          </SelectTrigger>
                          <SelectContent>
                            {c.namesMainOptions.map((o) => (
                              <SelectItem key={o.key} value={o.key}>
                                {o.label}
                              </SelectItem>
                            ))}
                          </SelectContent>
                        </Select>
                      </div>
                      <div className="space-y-1">
                        <span className="text-muted-foreground block text-xs">
                          When the source is silent
                        </span>
                        <Select
                          value={rule.whenSourceSilent}
                          onValueChange={(v) => {
                            if (v != null) update(c, { whenSourceSilent: v });
                          }}
                          items={c.silentOptions.map((o) => ({ value: o.key, label: o.label }))}
                          disabled={mutation.isPending}
                        >
                          <SelectTrigger
                            className="w-full"
                            aria-label={`${c.label}: when the source is silent`}
                          >
                            <SelectValue />
                          </SelectTrigger>
                          <SelectContent>
                            {c.silentOptions.map((o) => (
                              <SelectItem key={o.key} value={o.key}>
                                {o.label}
                              </SelectItem>
                            ))}
                          </SelectContent>
                        </Select>
                      </div>
                    </div>
                    {c.warning && <p className="text-muted-foreground text-xs">{c.warning}</p>}
                    <div className="grid gap-4 pt-1 sm:grid-cols-2">
                      <OptionLegend
                        title="When the source names a main one"
                        options={c.namesMainOptions}
                      />
                      <OptionLegend title="When the source is silent" options={c.silentOptions} />
                    </div>
                  </div>
                );
              })}
            </div>

            <div className="flex justify-end">
              <Button
                type="button"
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

export default MetadataChoiceRulesCard;

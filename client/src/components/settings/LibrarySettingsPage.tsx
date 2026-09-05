import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Loader2, Save, SlidersHorizontal } from "lucide-react";
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
import { handleApiError } from "@/lib/api";
import { toast } from "sonner";
import type { InitialsSpacing } from "@/types/LibrarySettings";

const INITIALS_SPACING_OPTIONS: { value: InitialsSpacing; label: string }[] = [
  { value: "Spaced", label: "Spaced (J. K. Rowling)" },
  { value: "Unspaced", label: "Unspaced (J.K. Rowling)" },
];

/**
 * Library-wide settings. The saved value is used by the backend (and, for initials spacing,
 * by the person similarity compliance check) — this page is the editing surface for it.
 */
export function LibrarySettingsPage() {
  const queryClient = useQueryClient();
  const [value, setValue] = useState<InitialsSpacing | null>(null);

  const { data, isLoading } = useQuery({
    queryKey: ["librarySettings"],
    queryFn: () => settingsApi.getLibrarySettings(),
  });

  const mutation = useMutation({
    mutationFn: (spacing: InitialsSpacing) =>
      settingsApi.updateLibrarySettings({ initialsSpacing: spacing }),
    onSuccess: () => {
      toast.success("Library settings saved");
      void queryClient.invalidateQueries({ queryKey: ["librarySettings"] });
    },
    onError: (err: unknown) => {
      toast.error(handleApiError(err).message);
    },
  });

  const current = value ?? data?.initialsSpacing ?? null;

  return (
    <div className="max-w-4xl space-y-6">
      <div>
        <h1 className="text-foreground flex items-center gap-2 text-2xl font-bold">
          <SlidersHorizontal className="text-primary h-6 w-6" />
          Library Settings
        </h1>
        <p className="text-muted-foreground text-sm">
          Settings that apply to the whole library, stored once and used across scans, checks and
          the consistency tools.
        </p>
      </div>

      <Card>
        <CardHeader>
          <CardTitle className="flex items-center gap-2 text-lg">
            <SlidersHorizontal className="text-primary h-5 w-5" />
            Library
          </CardTitle>
          <CardDescription>
            Choose how initials in person names are spaced. The consistency check reports authors
            and narrators whose stored name does not follow this convention.
          </CardDescription>
        </CardHeader>
        <CardContent className="space-y-4">
          {isLoading ? (
            <div className="text-muted-foreground flex items-center justify-center py-8">
              <Loader2 className="text-primary mr-2 h-5 w-5 animate-spin" />
              <span className="text-sm">Loading settings...</span>
            </div>
          ) : (
            <div className="space-y-4">
              <div className="space-y-1.5">
                <label className="mb-1 block text-xs font-medium">Initials spacing</label>
                <Select
                  value={current ?? undefined}
                  onValueChange={(v) => setValue(v)}
                  items={INITIALS_SPACING_OPTIONS.map((o) => ({ value: o.value, label: o.label }))}
                  disabled={mutation.isPending}
                >
                  <SelectTrigger className="w-full sm:w-72">
                    <SelectValue placeholder="Select initials spacing" />
                  </SelectTrigger>
                  <SelectContent>
                    {INITIALS_SPACING_OPTIONS.map((o) => (
                      <SelectItem key={o.value} value={o.value}>
                        {o.label}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
                <p className="text-muted-foreground text-xs">
                  When new books are organized, this setting decides whether author initials are
                  written with a space between them (J. K. Rowling) or without (J.K. Rowling).
                </p>
              </div>

              <div className="flex justify-end">
                <Button
                  onClick={() => current && mutation.mutate(current)}
                  disabled={mutation.isPending || !current}
                >
                  {mutation.isPending ? (
                    <Loader2 className="mr-1.5 h-4 w-4 animate-spin" />
                  ) : (
                    <Save className="mr-1.5 h-4 w-4" />
                  )}
                  Save
                </Button>
              </div>
            </div>
          )}
        </CardContent>
      </Card>
    </div>
  );
}

export default LibrarySettingsPage;

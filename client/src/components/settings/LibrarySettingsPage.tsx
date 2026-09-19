import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Link } from "@tanstack/react-router";
import { CalendarClock, Loader2, Save, SlidersHorizontal } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Checkbox } from "@/components/ui/checkbox";
import { Input } from "@/components/ui/input";
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
  const [upcomingReleasesEnabled, setUpcomingReleasesEnabled] = useState<boolean | null>(null);
  const [upcomingReleasesCronSchedule, setUpcomingReleasesCronSchedule] = useState<string | null>(
    null,
  );

  const { data, isLoading } = useQuery({
    queryKey: queryKeys.librarySettings(),
    queryFn: () => settingsApi.getLibrarySettings(),
  });

  const mutation = useMutation({
    mutationFn: (settings: {
      initialsSpacing: InitialsSpacing;
      upcomingReleasesEnabled: boolean;
      upcomingReleasesCronSchedule: string;
    }) => settingsApi.updateLibrarySettings(settings),
    onSuccess: () => {
      notifications.success("Library settings saved");
      void queryClient.invalidateQueries({ queryKey: queryKeys.librarySettings() });
    },
    onError: (err: unknown) => {
      notifications.error(handleApiError(err).message);
    },
  });

  const current = value ?? data?.initialsSpacing ?? null;
  const currentUpcomingReleasesEnabled =
    upcomingReleasesEnabled ?? data?.upcomingReleasesEnabled ?? true;
  const currentCronSchedule =
    upcomingReleasesCronSchedule ?? data?.upcomingReleasesCronSchedule ?? "";

  const handleSave = () => {
    if (!current) {
      return;
    }
    mutation.mutate({
      initialsSpacing: current,
      upcomingReleasesEnabled: currentUpcomingReleasesEnabled,
      upcomingReleasesCronSchedule: currentCronSchedule,
    });
  };

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
            </div>
          )}
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle className="flex items-center gap-2 text-lg">
            <CalendarClock className="text-primary h-5 w-5" />
            Upcoming Releases Refresh
          </CardTitle>
          <CardDescription>
            How often followed authors and series are polled for upcoming releases. See this
            schedule and its run history on the{" "}
            <Link to="/settings/tasks" className="text-primary hover:underline">
              Tasks
            </Link>{" "}
            page.
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
              <label className="flex items-center gap-2 text-sm">
                <Checkbox
                  checked={currentUpcomingReleasesEnabled}
                  onCheckedChange={(checked) => setUpcomingReleasesEnabled(Boolean(checked))}
                  disabled={mutation.isPending}
                />
                Check for upcoming releases
              </label>

              <div className="space-y-1.5">
                <label className="mb-1 block text-xs font-medium">Cron schedule</label>
                <Input
                  value={currentCronSchedule}
                  onChange={(e) => setUpcomingReleasesCronSchedule(e.target.value)}
                  placeholder="0 3 * * *"
                  disabled={mutation.isPending || !currentUpcomingReleasesEnabled}
                  className="w-full font-mono sm:w-72"
                />
                <p className="text-muted-foreground text-xs">
                  Standard 5-field cron expression (minute hour day month weekday), evaluated in
                  UTC.
                </p>
              </div>
            </div>
          )}
        </CardContent>
      </Card>

      <div className="flex justify-end">
        <Button onClick={handleSave} disabled={mutation.isPending || !current || isLoading}>
          {mutation.isPending ? (
            <Loader2 className="mr-1.5 h-4 w-4 animate-spin" />
          ) : (
            <Save className="mr-1.5 h-4 w-4" />
          )}
          Save
        </Button>
      </div>
    </div>
  );
}

export default LibrarySettingsPage;

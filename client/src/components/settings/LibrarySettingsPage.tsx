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
import { MetadataApplyRulesCard } from "@/components/settings/MetadataApplyRulesCard";
import { QualifierIndicatorsCard } from "@/components/settings/QualifierIndicatorsCard";
import { PAGE_SIZE_OPTIONS, type PageSizeOption } from "@/constants/paging";
import type {
  InitialsPunctuation,
  InitialsSpacing,
  SearchInitialsHandling,
} from "@/types/LibrarySettings";

const INITIALS_SPACING_OPTIONS: { value: InitialsSpacing; label: string }[] = [
  { value: "Spaced", label: "Spaced (J. K. Rowling)" },
  { value: "Unspaced", label: "Unspaced (J.K. Rowling)" },
];

const INITIALS_PUNCTUATION_OPTIONS: { value: InitialsPunctuation; label: string }[] = [
  { value: "Dotted", label: "Dotted (J. R. R. Tolkien)" },
  { value: "Undotted", label: "Undotted (J R R Tolkien)" },
];

const SEARCH_INITIALS_HANDLING_OPTIONS: { value: SearchInitialsHandling; label: string }[] = [
  { value: "AsStored", label: "As stored (the name as it is in the library)" },
  { value: "Compact", label: "Compact (George R.R. Martin)" },
  { value: "Spaced", label: "Spaced (George R. R. Martin)" },
];

const MIN_NARRATORS_IN_PATH = 1;
const MAX_NARRATORS_IN_PATH = 10;

const PAGE_SIZE_SELECT_OPTIONS: { value: PageSizeOption; label: string }[] = PAGE_SIZE_OPTIONS.map(
  (size) => ({ value: size, label: `${size} rows per page` }),
);

/**
 * Library-wide settings. The saved value is used by the backend (and, for initials spacing,
 * by the person similarity compliance check) — this page is the editing surface for it.
 */
export function LibrarySettingsPage() {
  const queryClient = useQueryClient();
  const [value, setValue] = useState<InitialsSpacing | null>(null);
  const [punctuationValue, setPunctuationValue] = useState<InitialsPunctuation | null>(null);
  const [upcomingReleasesEnabled, setUpcomingReleasesEnabled] = useState<boolean | null>(null);
  const [upcomingReleasesCronSchedule, setUpcomingReleasesCronSchedule] = useState<string | null>(
    null,
  );
  const [defaultPageSize, setDefaultPageSize] = useState<PageSizeOption | null>(null);
  const [searchInitialsHandling, setSearchInitialsHandling] =
    useState<SearchInitialsHandling | null>(null);
  const [includeNarratorInPath, setIncludeNarratorInPath] = useState<boolean | null>(null);
  // Held as the typed text so a half-edited field ("", "1") is not snapped back under the cursor.
  const [maxNarratorsInPath, setMaxNarratorsInPath] = useState<string | null>(null);

  const { data, isLoading } = useQuery({
    queryKey: queryKeys.librarySettings(),
    queryFn: () => settingsApi.getLibrarySettings(),
  });

  const mutation = useMutation({
    mutationFn: (settings: {
      initialsSpacing: InitialsSpacing;
      initialsPunctuation: InitialsPunctuation;
      upcomingReleasesEnabled: boolean;
      upcomingReleasesCronSchedule: string;
      defaultPageSize: number;
      searchInitialsHandling: SearchInitialsHandling;
      includeNarratorInPath: boolean;
      maxNarratorsInPath: number;
    }) => settingsApi.updateLibrarySettings(settings),
    onSuccess: () => {
      notifications.success("Library settings saved");
      void queryClient.invalidateQueries({ queryKey: queryKeys.librarySettings() });
      // The Settings > Tasks page reads the same upcoming-releases enabled/cron values from this
      // save via a separate query - without this it would show the previous schedule until its
      // own 30s refetch caught up.
      void queryClient.invalidateQueries({ queryKey: queryKeys.scheduledTasks() });
    },
    onError: (err: unknown) => {
      notifications.error(handleApiError(err).message);
    },
  });

  const current = value ?? data?.initialsSpacing ?? null;
  const currentPunctuation = punctuationValue ?? data?.initialsPunctuation ?? null;
  const currentUpcomingReleasesEnabled =
    upcomingReleasesEnabled ?? data?.upcomingReleasesEnabled ?? true;
  const currentCronSchedule =
    upcomingReleasesCronSchedule ?? data?.upcomingReleasesCronSchedule ?? "";
  const currentDefaultPageSize = defaultPageSize ?? data?.defaultPageSize ?? null;
  const currentSearchInitialsHandling =
    searchInitialsHandling ?? data?.searchInitialsHandling ?? null;
  const currentIncludeNarratorInPath =
    includeNarratorInPath ?? data?.includeNarratorInPath ?? false;
  const currentMaxNarratorsInPathText =
    maxNarratorsInPath ?? (data ? String(data.maxNarratorsInPath) : "");
  const currentMaxNarratorsInPath = Number(currentMaxNarratorsInPathText);
  // Mirrors the server's bounds (SettingsController refuses anything else with a 400).
  const maxNarratorsInPathValid =
    Number.isInteger(currentMaxNarratorsInPath) &&
    currentMaxNarratorsInPath >= MIN_NARRATORS_IN_PATH &&
    currentMaxNarratorsInPath <= MAX_NARRATORS_IN_PATH;
  // The limit only matters while the narrator is in the folder name: with it off the field is
  // read-only, so a half-edited value must not leave Save disabled with no way to fix it.
  const maxNarratorsInPathBlocksSave = currentIncludeNarratorInPath && !maxNarratorsInPathValid;

  const handleSave = () => {
    if (
      !current ||
      !currentPunctuation ||
      !currentDefaultPageSize ||
      !currentSearchInitialsHandling
    ) {
      return;
    }
    mutation.mutate({
      initialsSpacing: current,
      initialsPunctuation: currentPunctuation,
      upcomingReleasesEnabled: currentUpcomingReleasesEnabled,
      upcomingReleasesCronSchedule: currentCronSchedule,
      defaultPageSize: currentDefaultPageSize,
      searchInitialsHandling: currentSearchInitialsHandling,
      includeNarratorInPath: currentIncludeNarratorInPath,
      // Off, the typed text is ignored and the stored limit is sent back unchanged (the server
      // validates the field either way).
      maxNarratorsInPath: currentIncludeNarratorInPath
        ? currentMaxNarratorsInPath
        : (data?.maxNarratorsInPath ?? currentMaxNarratorsInPath),
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
            Choose how initials in person names are spaced and punctuated. The consistency check
            reports authors and narrators whose stored name does not follow this convention.
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

              <div className="space-y-1.5">
                <label className="mb-1 block text-xs font-medium">Initials punctuation</label>
                <Select
                  value={currentPunctuation ?? undefined}
                  onValueChange={(v) => setPunctuationValue(v)}
                  items={INITIALS_PUNCTUATION_OPTIONS.map((o) => ({
                    value: o.value,
                    label: o.label,
                  }))}
                  disabled={mutation.isPending}
                >
                  <SelectTrigger className="w-full sm:w-72">
                    <SelectValue placeholder="Select initials punctuation" />
                  </SelectTrigger>
                  <SelectContent>
                    {INITIALS_PUNCTUATION_OPTIONS.map((o) => (
                      <SelectItem key={o.value} value={o.value}>
                        {o.label}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
                <p className="text-muted-foreground text-xs">
                  Decides whether author initials carry a trailing period (J. R. R. Tolkien) or not
                  (J R R Tolkien).
                </p>
              </div>

              <div className="space-y-1.5">
                <label className="mb-1 block text-xs font-medium">Search initials handling</label>
                <Select
                  value={currentSearchInitialsHandling ?? undefined}
                  onValueChange={(v) => {
                    if (v != null) setSearchInitialsHandling(v);
                  }}
                  items={SEARCH_INITIALS_HANDLING_OPTIONS.map((o) => ({
                    value: o.value,
                    label: o.label,
                  }))}
                  disabled={mutation.isPending}
                >
                  <SelectTrigger className="w-full sm:w-72">
                    <SelectValue placeholder="Select search initials handling" />
                  </SelectTrigger>
                  <SelectContent>
                    {SEARCH_INITIALS_HANDLING_OPTIONS.map((o) => (
                      <SelectItem key={o.value} value={o.value}>
                        {o.label}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
                <p className="text-muted-foreground text-xs">
                  How author initials are written in the default online-metadata search, for both
                  the manual search and the bulk search. Some sources only match one form (on
                  Hardcover, &ldquo;George R.R. Martin&rdquo; finds books that &ldquo;George R. R.
                  Martin&rdquo; misses). It never changes stored names, and text you type into the
                  search box is sent as typed.
                </p>
              </div>

              <div className="space-y-1.5">
                <label className="flex items-center gap-2 text-sm">
                  <Checkbox
                    checked={currentIncludeNarratorInPath}
                    onCheckedChange={(checked) => setIncludeNarratorInPath(Boolean(checked))}
                    disabled={mutation.isPending}
                  />
                  Include narrator in folder name
                </label>
                <p className="text-muted-foreground text-xs">
                  Adds the narrator to each book&apos;s folder in the form Audiobookshelf reads,
                  e.g.{" "}
                  <span className="font-mono">2010 - The Way of Kings {"{Michael Kramer}"}</span>,
                  so two narrations of the same book can sit side by side. Books without a narrator
                  are unaffected. Turning this on or off changes the expected path of every book
                  that has a narrator: they show up as &ldquo;wrong file path&rdquo; in the
                  consistency check, where resolving them moves the files.
                </p>

                <label
                  htmlFor="max-narrators-in-path"
                  className="mt-2 mb-1 block text-xs font-medium"
                >
                  Maximum narrators in folder name
                </label>
                <Input
                  id="max-narrators-in-path"
                  type="number"
                  inputMode="numeric"
                  min={MIN_NARRATORS_IN_PATH}
                  max={MAX_NARRATORS_IN_PATH}
                  value={currentMaxNarratorsInPathText}
                  onChange={(e) => setMaxNarratorsInPath(e.target.value)}
                  disabled={mutation.isPending || !currentIncludeNarratorInPath}
                  aria-invalid={maxNarratorsInPathBlocksSave}
                  className="w-full sm:w-32"
                />
                <p className="text-muted-foreground text-xs">
                  How many narrators the folder names ({MIN_NARRATORS_IN_PATH}&ndash;
                  {MAX_NARRATORS_IN_PATH}); the first ones in the book&apos;s narrator order. Casts
                  longer than that are cut, so two books that share their first narrators get the
                  same folder name. Changing it moves the books it affects the same way.
                </p>
              </div>

              <div className="space-y-1.5">
                <label className="mb-1 block text-xs font-medium">Default page size</label>
                <Select
                  value={currentDefaultPageSize ?? undefined}
                  onValueChange={(v) => {
                    if (v != null) setDefaultPageSize(v as PageSizeOption);
                  }}
                  items={PAGE_SIZE_SELECT_OPTIONS.map((o) => ({ value: o.value, label: o.label }))}
                  disabled={mutation.isPending}
                >
                  <SelectTrigger className="w-full sm:w-72">
                    <SelectValue placeholder="Select default page size" />
                  </SelectTrigger>
                  <SelectContent>
                    {PAGE_SIZE_SELECT_OPTIONS.map((o) => (
                      <SelectItem key={o.value} value={o.value}>
                        {o.label}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
                <p className="text-muted-foreground text-xs">
                  How many rows a paged list shows before you change it with that list's own rows-
                  per-page dropdown.
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

      <MetadataApplyRulesCard />

      <QualifierIndicatorsCard />

      <div className="flex justify-end">
        <Button
          onClick={handleSave}
          disabled={
            mutation.isPending ||
            !current ||
            !currentPunctuation ||
            !currentDefaultPageSize ||
            !currentSearchInitialsHandling ||
            maxNarratorsInPathBlocksSave ||
            isLoading
          }
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
  );
}

export default LibrarySettingsPage;

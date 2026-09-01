import { useState } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { Settings as SettingsIcon, Plus, Trash2, Edit2, Loader2, BookMarked } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Dialog, DialogContent, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { TypeaheadInput } from "@/components/TypeaheadInput";
import { settingsApi, similarValuesApi } from "@/services/api";
import { handleApiError } from "@/lib/api";
import { toast } from "sonner";
import type { SeriesMapping, SeriesMappingBase } from "@/types/SeriesMapping";

export function Settings() {
  const queryClient = useQueryClient();

  // Dialog state
  const [dialogOpen, setDialogOpen] = useState(false);
  const [editingMapping, setEditingMapping] = useState<SeriesMapping | null>(null);
  const [mappedSeries, setMappedSeries] = useState("");
  const [regex, setRegex] = useState("");
  const [warnAboutPart, setWarnAboutPart] = useState(false);
  const [saving, setSaving] = useState(false);

  const { data: mappings = [], isLoading: loading } = useQuery({
    queryKey: ["seriesMappings"],
    queryFn: () => settingsApi.getSeriesMappings(),
  });

  const { data: seriesNames = [] } = useQuery({
    queryKey: ["similarValueNames", "series"],
    queryFn: () => similarValuesApi.getSeriesNames(),
    staleTime: 5 * 60 * 1000,
  });

  const handleOpenCreate = () => {
    setEditingMapping(null);
    setMappedSeries("");
    setRegex("");
    setWarnAboutPart(false);
    setDialogOpen(true);
  };

  const handleOpenEdit = (m: SeriesMapping) => {
    setEditingMapping(m);
    setMappedSeries(m.mappedSeries);
    setRegex(m.regex);
    setWarnAboutPart(m.warnAboutPart);
    setDialogOpen(true);
  };

  const handleSave = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!mappedSeries.trim() || !regex.trim()) return;

    setSaving(true);
    try {
      if (editingMapping) {
        await settingsApi.updateSeriesMapping(editingMapping.id, {
          id: editingMapping.id,
          mappedSeries: mappedSeries.trim(),
          regex: regex.trim(),
          warnAboutPart,
        });
        toast.success("Series mapping updated");
      } else {
        const payload: SeriesMappingBase = {
          mappedSeries: mappedSeries.trim(),
          regex: regex.trim(),
          warnAboutPart,
        };
        await settingsApi.createSeriesMapping(payload);
        toast.success("Series mapping created");
      }
      setDialogOpen(false);
      void queryClient.invalidateQueries({ queryKey: ["seriesMappings"] });
    } catch (err: unknown) {
      toast.error(handleApiError(err).message);
    } finally {
      setSaving(false);
    }
  };

  const handleDelete = async (id: number) => {
    try {
      await settingsApi.deleteSeriesMapping(id);
      toast.success("Series mapping deleted");
      void queryClient.invalidateQueries({ queryKey: ["seriesMappings"] });
    } catch (err: unknown) {
      toast.error(handleApiError(err).message);
    }
  };

  // Group by mappedSeries
  const grouped = mappings.reduce<Record<string, SeriesMapping[]>>((acc, item) => {
    const list = acc[item.mappedSeries] || [];
    list.push(item);
    acc[item.mappedSeries] = list;
    return acc;
  }, {});

  return (
    <div className="max-w-4xl space-y-6">
      <div className="flex flex-wrap items-center justify-between gap-4">
        <div>
          <h1 className="text-foreground flex items-center gap-2 text-2xl font-bold">
            <SettingsIcon className="text-primary h-6 w-6" />
            Settings
          </h1>
          <p className="text-muted-foreground text-sm">
            Configure series name regular expression mappings.
          </p>
        </div>

        <Button onClick={handleOpenCreate}>
          <Plus className="mr-2 h-4 w-4" />
          Add Series Mapping
        </Button>
      </div>

      <Card>
        <CardHeader>
          <CardTitle className="flex items-center gap-2 text-lg">
            <BookMarked className="text-primary h-5 w-5" />
            Series Regex Mappings ({mappings.length})
          </CardTitle>
        </CardHeader>
        <CardContent>
          <p className="text-muted-foreground mb-4 text-xs">
            Regular expressions match scraped or embedded series names and normalize them to a
            standard canonical series title.
          </p>

          {loading ? (
            <div className="text-muted-foreground flex items-center justify-center py-12">
              <Loader2 className="text-primary mr-2 h-5 w-5 animate-spin" />
              <span className="text-sm">Loading mappings...</span>
            </div>
          ) : mappings.length === 0 ? (
            <div className="text-muted-foreground border-border rounded-lg border border-dashed p-8 text-center text-sm">
              No series mappings configured yet.
            </div>
          ) : (
            <div className="space-y-4">
              {Object.entries(grouped).map(([canonical, items]) => (
                <div
                  key={canonical}
                  className="border-border bg-card space-y-2 rounded-lg border p-4"
                >
                  <div className="text-foreground text-sm font-semibold">Target: {canonical}</div>
                  <div className="space-y-1.5 pl-2">
                    {items.map((item) => (
                      <div
                        key={item.id}
                        className="bg-muted/40 flex flex-col justify-between gap-2 rounded px-3 py-2 text-xs sm:flex-row sm:items-center"
                      >
                        <div className="text-muted-foreground min-w-0 flex-1 font-mono break-all">
                          Pattern:{" "}
                          <span className="text-foreground font-semibold">{item.regex}</span>
                        </div>
                        <div className="flex shrink-0 items-center gap-1 self-end sm:self-center">
                          <Button
                            variant="ghost"
                            size="icon"
                            className="h-7 w-7"
                            onClick={() => handleOpenEdit(item)}
                          >
                            <Edit2 className="h-3.5 w-3.5" />
                          </Button>
                          <Button
                            variant="ghost"
                            size="icon"
                            className="text-destructive h-7 w-7"
                            onClick={() => {
                              void handleDelete(item.id);
                            }}
                          >
                            <Trash2 className="h-3.5 w-3.5" />
                          </Button>
                        </div>
                      </div>
                    ))}
                  </div>
                </div>
              ))}
            </div>
          )}
        </CardContent>
      </Card>

      <Dialog open={dialogOpen} onOpenChange={setDialogOpen}>
        <DialogContent className="w-[calc(100vw-2rem)] p-4 sm:max-w-md sm:p-6">
          <DialogHeader>
            <DialogTitle>
              {editingMapping ? "Edit Series Mapping" : "Create Series Mapping"}
            </DialogTitle>
          </DialogHeader>

          <form
            onSubmit={(e) => {
              void handleSave(e);
            }}
            className="space-y-4 py-2"
          >
            <div className="space-y-1">
              <label className="text-muted-foreground text-xs font-semibold uppercase">
                Target Series Name <span className="text-destructive">*</span>
              </label>
              <TypeaheadInput
                placeholder="The Wheel of Time"
                value={mappedSeries}
                onValueChange={setMappedSeries}
                candidates={seriesNames}
                required
              />
            </div>

            <div className="space-y-1">
              <label className="text-muted-foreground text-xs font-semibold uppercase">
                Regex Pattern <span className="text-destructive">*</span>
              </label>
              <Input
                placeholder="(?i)^wheel of time.*"
                value={regex}
                onChange={(e) => setRegex(e.target.value)}
                className="font-mono"
                required
              />
            </div>

            <div className="flex items-center space-x-2 pt-1">
              <input
                type="checkbox"
                id="warnAboutPart"
                checked={warnAboutPart}
                onChange={(e) => setWarnAboutPart(e.target.checked)}
                className="border-border h-4 w-4 rounded"
              />
              <label
                htmlFor="warnAboutPart"
                className="text-muted-foreground cursor-pointer text-xs"
              >
                Warn if series part is found
              </label>
            </div>

            <div className="border-border flex flex-col-reverse justify-end gap-2 border-t pt-4 sm:flex-row">
              <Button
                type="button"
                variant="outline"
                className="w-full sm:w-auto"
                onClick={() => setDialogOpen(false)}
              >
                Cancel
              </Button>
              <Button type="submit" className="w-full sm:w-auto" disabled={saving}>
                {saving ? <Loader2 className="mr-1.5 h-4 w-4 animate-spin" /> : null}
                {editingMapping ? "Save Changes" : "Create Mapping"}
              </Button>
            </div>
          </form>
        </DialogContent>
      </Dialog>
    </div>
  );
}

export default Settings;

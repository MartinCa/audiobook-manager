import React, { useState, useEffect } from "react";
import { Settings as SettingsIcon, Save } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { api, handleApiError } from "@/lib/api";
import { toast } from "sonner";

export const Settings: React.FC = () => {
  const [importPath, setImportPath] = useState("");
  const [libraryPath, setLibraryPath] = useState("");
  const [hardcoverApiKey, setHardcoverApiKey] = useState("");
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);

  useEffect(() => {
    api
      .get("/settings")
      .then((res) => {
        setImportPath(res.data?.audiobookImportPath || "");
        setLibraryPath(res.data?.audiobookLibraryPath || "");
        setHardcoverApiKey(res.data?.hardcoverApiKey || "");
      })
      .catch((err) => toast.error(handleApiError(err).message))
      .finally(() => setLoading(false));
  }, []);

  const handleSave = async (e: React.FormEvent) => {
    e.preventDefault();
    setSaving(true);
    try {
      await api.post("/settings", {
        audiobookImportPath: importPath,
        audiobookLibraryPath: libraryPath,
        hardcoverApiKey: hardcoverApiKey || undefined,
      });
      toast.success("Settings updated successfully");
    } catch (err) {
      toast.error(handleApiError(err).message);
    } finally {
      setSaving(false);
    }
  };

  if (loading) {
    return (
      <div className="text-center py-12 text-muted-foreground text-sm">
        Loading settings...
      </div>
    );
  }

  return (
    <div className="space-y-6 max-w-2xl">
      <div>
        <h1 className="text-2xl font-bold flex items-center gap-2">
          <SettingsIcon className="h-6 w-6 text-primary" />
          Settings
        </h1>
        <p className="text-sm text-muted-foreground">
          Configure storage paths and API integrations for metadata providers.
        </p>
      </div>

      <Card>
        <CardHeader>
          <CardTitle className="text-lg">Application Configuration</CardTitle>
        </CardHeader>
        <CardContent>
          <form
            onSubmit={handleSave}
            className="space-y-4"
          >
            <div>
              <label className="text-xs font-semibold uppercase text-muted-foreground block mb-1">
                Import Path
              </label>
              <Input
                value={importPath}
                onChange={(e) => setImportPath(e.target.value)}
                placeholder="/data/import"
                required
              />
            </div>

            <div>
              <label className="text-xs font-semibold uppercase text-muted-foreground block mb-1">
                Library Path
              </label>
              <Input
                value={libraryPath}
                onChange={(e) => setLibraryPath(e.target.value)}
                placeholder="/data/library"
                required
              />
            </div>

            <div>
              <label className="text-xs font-semibold uppercase text-muted-foreground block mb-1">
                Hardcover API Key
              </label>
              <Input
                type="password"
                value={hardcoverApiKey}
                onChange={(e) => setHardcoverApiKey(e.target.value)}
                placeholder="Optional GraphQL API Key"
              />
            </div>

            <div className="flex justify-end pt-4">
              <Button
                type="submit"
                disabled={saving}
              >
                <Save className="h-4 w-4 mr-2" />
                {saving ? "Saving..." : "Save Settings"}
              </Button>
            </div>
          </form>
        </CardContent>
      </Card>
    </div>
  );
};
export default Settings;

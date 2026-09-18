import { useQuery } from "@tanstack/react-query";
import { Link } from "@tanstack/react-router";
import { Settings as SettingsIcon, Info, ExternalLink } from "lucide-react";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { settingsApi } from "@/services/api";
import { queryKeys } from "@/lib/queryKeys";
import { formatVersion, getReleaseUrl } from "@/helpers/versionHelpers";

/**
 * System information. Series mapping patterns used to be maintained here as global
 * regex -> target rules; they are owned by a series now and live on that series' detail page
 * (the Management section), so only the system/about card remains.
 */
export function Settings() {
  const { data: systemInfo } = useQuery({
    queryKey: queryKeys.systemInfo(),
    queryFn: () => settingsApi.getSystemInfo(),
    staleTime: 60 * 60 * 1000,
  });

  return (
    <div className="max-w-4xl space-y-6">
      <div>
        <h1 className="text-foreground flex items-center gap-2 text-2xl font-bold">
          <SettingsIcon className="text-primary h-6 w-6" />
          System Information
        </h1>
        <p className="text-muted-foreground text-sm">
          System information and about this installation. Library-wide settings live on the{" "}
          <Link to="/settings/library" className="text-primary hover:underline">
            Library Settings
          </Link>{" "}
          page.
        </p>
      </div>

      <Card>
        <CardHeader>
          <CardTitle className="flex items-center gap-2 text-base">
            <Info className="text-primary h-4 w-4" />
            About & System Information
          </CardTitle>
        </CardHeader>
        <CardContent className="space-y-4">
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
            <div className="border-border bg-muted/40 space-y-1 rounded-lg border p-3">
              <span className="text-muted-foreground text-xs font-semibold uppercase">
                App Version
              </span>
              <div className="flex items-center gap-2">
                <span className="text-foreground font-mono text-sm font-medium">
                  {formatVersion(systemInfo?.version || __APP_VERSION__)}
                </span>
                <a
                  href={getReleaseUrl(systemInfo?.version || __APP_VERSION__)}
                  target="_blank"
                  rel="noreferrer"
                  className="text-primary flex items-center gap-1 text-xs hover:underline"
                >
                  Release Notes <ExternalLink className="h-3 w-3" />
                </a>
              </div>
            </div>

            <div className="border-border bg-muted/40 space-y-1 rounded-lg border p-3">
              <span className="text-muted-foreground text-xs font-semibold uppercase">
                Commit Hash
              </span>
              <div className="text-foreground truncate font-mono text-xs">
                {systemInfo?.commitHash || __COMMIT_HASH__ || "dev"}
              </div>
            </div>

            <div className="border-border bg-muted/40 space-y-1 rounded-lg border p-3">
              <span className="text-muted-foreground text-xs font-semibold uppercase">
                Backend Runtime
              </span>
              <div className="text-foreground text-xs font-medium">
                {systemInfo?.dotNetVersion || ".NET"}
              </div>
            </div>

            <div className="border-border bg-muted/40 space-y-1 rounded-lg border p-3">
              <span className="text-muted-foreground text-xs font-semibold uppercase">
                Database
              </span>
              <div className="text-foreground text-xs font-medium">SQLite (via EF Core)</div>
            </div>
          </div>
        </CardContent>
      </Card>
    </div>
  );
}

export default Settings;

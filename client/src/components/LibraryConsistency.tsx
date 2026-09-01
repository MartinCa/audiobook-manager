import React, { useState, useEffect } from "react";
import { AlertTriangle, Play, CheckCircle2, Wrench } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader } from "@/components/ui/card";
import { Checkbox } from "@/components/ui/checkbox";
import { Badge } from "@/components/ui/badge";
import { api, handleApiError } from "@/lib/api";
import { type ConsistencyIssue } from "@/types/domain";
import OperationProgressBar from "./OperationProgressBar";
import { toast } from "sonner";

export const LibraryConsistency: React.FC = () => {
  const [issues, setIssues] = useState<ConsistencyIssue[]>([]);
  const [loading, setLoading] = useState(true);
  const [selectedIds, setSelectedIds] = useState<number[]>([]);
  const [resolving, setResolving] = useState(false);
  const [progress, setProgress] = useState<{
    processed: number;
    total: number;
  }>({
    processed: 0,
    total: 0,
  });

  const fetchIssues = async () => {
    setLoading(true);
    try {
      const res = await api.get<ConsistencyIssue[]>("/consistency/issues");
      setIssues(res.data || []);
    } catch (err) {
      toast.error(handleApiError(err).message);
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    fetchIssues();
  }, []);

  const handleRunCheck = async () => {
    setLoading(true);
    try {
      await api.post("/consistency/check");
      toast.info("Library consistency check started");
    } catch (err) {
      toast.error(handleApiError(err).message);
      setLoading(false);
    }
  };

  const handleResolveSelected = async () => {
    if (selectedIds.length === 0) return;
    setResolving(true);
    setProgress({ processed: 0, total: selectedIds.length });
    try {
      await api.post("/consistency/resolve-batch", { ids: selectedIds });
      toast.success("Batch resolution initiated");
      setSelectedIds([]);
    } catch (err) {
      toast.error(handleApiError(err).message);
    } finally {
      setResolving(false);
    }
  };

  const handleToggleSelectAll = (checked: boolean) => {
    if (checked) {
      setSelectedIds(issues.map((i) => i.id));
    } else {
      setSelectedIds([]);
    }
  };

  const handleToggleSelect = (id: number, checked: boolean) => {
    if (checked) {
      setSelectedIds((prev) => [...prev, id]);
    } else {
      setSelectedIds((prev) => prev.filter((i) => i !== id));
    }
  };

  return (
    <div className="space-y-6">
      <div className="flex justify-between items-center flex-wrap gap-4">
        <div>
          <h1 className="text-2xl font-bold flex items-center gap-2">
            <AlertTriangle className="h-6 w-6 text-amber-500" />
            Library Consistency
          </h1>
          <p className="text-sm text-muted-foreground">
            Check for missing sidecar files, broken paths, or mismatched tags in
            your library.
          </p>
        </div>
        <div className="flex gap-2">
          <Button
            variant="outline"
            onClick={handleRunCheck}
            disabled={loading || resolving}
          >
            <Play className="h-4 w-4 mr-2" />
            Run Check
          </Button>
          <Button
            onClick={handleResolveSelected}
            disabled={selectedIds.length === 0 || resolving}
          >
            <Wrench className="h-4 w-4 mr-2" />
            Resolve Selected ({selectedIds.length})
          </Button>
        </div>
      </div>

      {resolving && (
        <OperationProgressBar
          processed={progress.processed}
          total={progress.total}
          label="Resolving issues..."
        />
      )}

      {loading ? (
        <div className="text-center py-12 text-muted-foreground text-sm">
          Loading consistency issues...
        </div>
      ) : issues.length === 0 ? (
        <Card>
          <CardContent className="py-12 text-center space-y-3">
            <CheckCircle2 className="h-12 w-12 text-emerald-500 mx-auto" />
            <h3 className="font-semibold text-lg">
              No Consistency Issues Found
            </h3>
            <p className="text-sm text-muted-foreground">
              Your library files, sidecars, and database entries are completely
              consistent!
            </p>
          </CardContent>
        </Card>
      ) : (
        <Card>
          <CardHeader className="py-3 px-4 flex flex-row items-center justify-between border-b border-border">
            <div className="flex items-center space-x-3">
              <Checkbox
                checked={
                  selectedIds.length === issues.length && issues.length > 0
                }
                onCheckedChange={handleToggleSelectAll}
              />
              <span className="text-sm font-semibold">Select All Issues</span>
            </div>
            <span className="text-xs text-muted-foreground">
              Total Issues: {issues.length}
            </span>
          </CardHeader>
          <CardContent className="p-0 divide-y divide-border">
            {issues.map((issue) => (
              <div
                key={issue.id}
                className="p-4 flex items-start space-x-3 hover:bg-muted/30 transition-colors"
              >
                <Checkbox
                  checked={selectedIds.includes(issue.id)}
                  onCheckedChange={(checked) =>
                    handleToggleSelect(issue.id, !!checked)
                  }
                  className="mt-1"
                />
                <div className="flex-1 min-w-0">
                  <div className="flex items-center gap-2 mb-1">
                    <span className="font-semibold text-sm">
                      {issue.audiobookName}
                    </span>
                    <Badge
                      variant="outline"
                      className="text-xs"
                    >
                      {issue.type}
                    </Badge>
                  </div>
                  <p className="text-xs text-muted-foreground font-mono">
                    {issue.details}
                  </p>
                </div>
              </div>
            ))}
          </CardContent>
        </Card>
      )}
    </div>
  );
};
export default LibraryConsistency;

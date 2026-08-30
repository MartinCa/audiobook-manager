import React, { useState, useEffect } from "react";
import { Layers, RefreshCw, CheckCircle2, ArrowRight } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import { Tabs, TabsList, TabsTrigger, TabsContent } from "@/components/ui/tabs";
import { Badge } from "@/components/ui/badge";
import { api, handleApiError } from "@/lib/api";
import { SimilarValueGroup } from "@/types/domain";
import DuplicateTargetDialog from "./DuplicateTargetDialog";
import OperationProgressBar from "./OperationProgressBar";
import { toast } from "sonner";

export const SimilarValues: React.FC = () => {
  const [activeTab, setActiveTab] = useState<"authors" | "series">("authors");
  const [groups, setGroups] = useState<SimilarValueGroup[]>([]);
  const [loading, setLoading] = useState(true);
  const [selectedGroup, setSelectedGroup] = useState<SimilarValueGroup | null>(null);
  const [aligning, setAligning] = useState(false);
  const [progress] = useState<{ processed: number; total: number }>({
    processed: 0,
    total: 0,
  });

  const fetchSimilar = async (type: "authors" | "series") => {
    setLoading(true);
    try {
      const endpoint = type === "authors" ? "/similar-values/similar-authors" : "/similar-values/similar-series";
      const res = await api.get<SimilarValueGroup[]>(endpoint);
      setGroups(res.data || []);
    } catch (err) {
      toast.error(handleApiError(err).message);
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    fetchSimilar(activeTab);
  }, [activeTab]);

  const handleAlign = async (targetValue: string) => {
    if (!selectedGroup) return;
    setAligning(true);
    try {
      const endpoint = activeTab === "authors" ? "/similar-values/align-authors" : "/similar-values/align-series";
      await api.post(endpoint, {
        targetValue,
        sourceValues: selectedGroup.candidates,
      });
      toast.success(`Alignment started for "${targetValue}"`);
      setGroups((prev) => prev.filter((g) => g !== selectedGroup));
    } catch (err) {
      toast.error(handleApiError(err).message);
    } finally {
      setAligning(false);
      setSelectedGroup(null);
    }
  };

  return (
    <div className="space-y-6">
      <div className="flex justify-between items-center flex-wrap gap-4">
        <div>
          <h1 className="text-2xl font-bold flex items-center gap-2">
            <Layers className="h-6 w-6 text-primary" />
            Similar Values Alignment
          </h1>
          <p className="text-sm text-muted-foreground">
            Detect and merge near-duplicate author names and series titles across your library.
          </p>
        </div>
        <Button variant="outline" onClick={() => fetchSimilar(activeTab)} disabled={loading}>
          <RefreshCw className={`h-4 w-4 mr-2 ${loading ? "animate-spin" : ""}`} />
          Refresh Detection
        </Button>
      </div>

      <Tabs value={activeTab} onValueChange={(val) => setActiveTab(val as "authors" | "series")}>
        <TabsList className="mb-4">
          <TabsTrigger value="authors">Similar Authors</TabsTrigger>
          <TabsTrigger value="series">Similar Series</TabsTrigger>
        </TabsList>

        <TabsContent value={activeTab}>
          {aligning && (
            <OperationProgressBar
              processed={progress.processed}
              total={progress.total}
              label="Aligning values..."
            />
          )}

          {loading ? (
            <div className="text-center py-12 text-muted-foreground text-sm">
              Detecting similar {activeTab}...
            </div>
          ) : groups.length === 0 ? (
            <Card>
              <CardContent className="py-12 text-center space-y-3">
                <CheckCircle2 className="h-12 w-12 text-emerald-500 mx-auto" />
                <h3 className="font-semibold text-lg">No Duplicate {activeTab} Found</h3>
                <p className="text-sm text-muted-foreground">
                  All {activeTab} names in your library appear unique and properly formatted!
                </p>
              </CardContent>
            </Card>
          ) : (
            <div className="space-y-4">
              {groups.map((group, idx) => (
                <Card key={idx}>
                  <CardContent className="p-4 flex items-center justify-between flex-wrap gap-4">
                    <div className="space-y-1">
                      <span className="text-xs font-semibold uppercase text-muted-foreground">
                        Matching Group ({group.candidates.length} variants)
                      </span>
                      <div className="flex flex-wrap gap-2 items-center">
                        {group.candidates.map((cand) => (
                          <Badge key={cand} variant="secondary">
                            {cand}
                          </Badge>
                        ))}
                      </div>
                    </div>
                    <Button onClick={() => setSelectedGroup(group)}>
                      Align Values
                      <ArrowRight className="h-4 w-4 ml-2" />
                    </Button>
                  </CardContent>
                </Card>
              ))}
            </div>
          )}
        </TabsContent>
      </Tabs>

      {selectedGroup && (
        <DuplicateTargetDialog
          open={!!selectedGroup}
          onOpenChange={(open) => !open && setSelectedGroup(null)}
          candidates={selectedGroup.candidates}
          onConfirm={handleAlign}
        />
      )}
    </div>
  );
};
export default SimilarValues;

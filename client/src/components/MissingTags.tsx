import React, { useState, useEffect } from "react";
import { Tag, RefreshCw, CheckCircle2 } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import { Checkbox } from "@/components/ui/checkbox";
import { Badge } from "@/components/ui/badge";
import { api, handleApiError } from "@/lib/api";
import { MissingTag } from "@/types/domain";
import { toast } from "sonner";

export const MissingTags: React.FC = () => {
  const [fields, setFields] = useState<{ key: string; label: string }[]>([]);
  const [selectedFields, setSelectedFields] = useState<string[]>([]);
  const [missingBooks, setMissingBooks] = useState<MissingTag[]>([]);
  const [loading, setLoading] = useState(true);
  const [backfilling, setBackfilling] = useState(false);

  useEffect(() => {
    api
      .get<{ key: string; label: string }[]>("/missing-tags/fields")
      .then((res) => {
        setFields(res.data || []);
        const keys = (res.data || []).map((f) => f.key);
        setSelectedFields(keys);
        fetchMissing(keys);
      })
      .catch((err) => toast.error(handleApiError(err).message));
  }, []);

  const fetchMissing = async (fieldKeys: string[]) => {
    setLoading(true);
    try {
      const res = await api.get<MissingTag[]>("/missing-tags/audiobooks", {
        params: { fields: fieldKeys.join(",") },
      });
      setMissingBooks(res.data || []);
    } catch (err) {
      toast.error(handleApiError(err).message);
    } finally {
      setLoading(false);
    }
  };

  const handleToggleField = (key: string) => {
    const updated = selectedFields.includes(key)
      ? selectedFields.filter((f) => f !== key)
      : [...selectedFields, key];
    setSelectedFields(updated);
    fetchMissing(updated);
  };

  const handleBackfillLanguage = async () => {
    setBackfilling(true);
    try {
      await api.post("/missing-tags/backfill-language");
      toast.success("Language backfill operation started");
    } catch (err) {
      toast.error(handleApiError(err).message);
    } finally {
      setBackfilling(false);
    }
  };

  return (
    <div className="space-y-6">
      <div className="flex justify-between items-center flex-wrap gap-4">
        <div>
          <h1 className="text-2xl font-bold flex items-center gap-2">
            <Tag className="h-6 w-6 text-primary" />
            Missing Tags
          </h1>
          <p className="text-sm text-muted-foreground">
            Find audiobooks with incomplete tag metadata and backfill tags from
            embedded file data.
          </p>
        </div>
        <Button
          variant="outline"
          onClick={handleBackfillLanguage}
          disabled={backfilling}
        >
          <RefreshCw
            className={`h-4 w-4 mr-2 ${backfilling ? "animate-spin" : ""}`}
          />
          Backfill Embedded Languages
        </Button>
      </div>

      <Card className="p-4">
        <h3 className="text-xs font-semibold uppercase text-muted-foreground mb-3">
          Filter by Missing Tag Fields
        </h3>
        <div className="flex flex-wrap gap-4">
          {fields.map((f) => (
            <div
              key={f.key}
              className="flex items-center space-x-2"
            >
              <Checkbox
                id={f.key}
                checked={selectedFields.includes(f.key)}
                onCheckedChange={() => handleToggleField(f.key)}
              />
              <label
                htmlFor={f.key}
                className="text-sm cursor-pointer"
              >
                {f.label}
              </label>
            </div>
          ))}
        </div>
      </Card>

      {loading ? (
        <div className="text-center py-12 text-muted-foreground text-sm">
          Loading books with missing tags...
        </div>
      ) : missingBooks.length === 0 ? (
        <Card>
          <CardContent className="py-12 text-center space-y-3">
            <CheckCircle2 className="h-12 w-12 text-emerald-500 mx-auto" />
            <h3 className="font-semibold text-lg">No Missing Tags Found</h3>
            <p className="text-sm text-muted-foreground">
              All audiobooks in your library have complete metadata for the
              selected fields!
            </p>
          </CardContent>
        </Card>
      ) : (
        <Card>
          <CardContent className="p-0 divide-y divide-border">
            {missingBooks.map((item) => (
              <div
                key={item.audiobookId}
                className="p-4 flex items-center justify-between"
              >
                <div>
                  <h4 className="font-semibold text-sm">
                    {item.audiobookName}
                  </h4>
                  <div className="flex flex-wrap gap-1 mt-1">
                    {item.missingFields.map((field) => (
                      <Badge
                        key={field}
                        variant="destructive"
                        className="text-xs"
                      >
                        Missing {field}
                      </Badge>
                    ))}
                  </div>
                </div>
              </div>
            ))}
          </CardContent>
        </Card>
      )}
    </div>
  );
};
export default MissingTags;

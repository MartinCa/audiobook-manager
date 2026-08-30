import { useState, type ChangeEvent } from "react";
import { Upload, X, Image as ImageIcon, Link as LinkIcon } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Dialog, DialogContent, DialogHeader, DialogTitle } from "@/components/ui/dialog";

interface CoverEditorProps {
  base64Data?: string;
  mimeType?: string;
  coverUrl?: string;
  onCoverChange: (base64Data: string | undefined, mimeType: string | undefined) => void;
}

export function CoverEditor({
  base64Data,
  mimeType = "image/jpeg",
  coverUrl,
  onCoverChange,
}: CoverEditorProps) {
  const [dialogOpen, setDialogOpen] = useState(false);
  const [urlInput, setUrlInput] = useState("");
  const [fetchingUrl, setFetchingUrl] = useState(false);
  const [urlError, setUrlError] = useState<string | null>(null);

  const currentSrc = base64Data ? `data:${mimeType};base64,${base64Data}` : coverUrl;

  const handleFileUpload = (e: ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0];
    if (!file) return;

    const fileMime = file.type || "image/jpeg";
    const reader = new FileReader();
    reader.onloadend = () => {
      const result = reader.result as string;
      const base64Index = result.indexOf(";base64,");
      const cleanBase64 = base64Index !== -1 ? result.substring(base64Index + 8) : result;
      onCoverChange(cleanBase64, fileMime);
      setDialogOpen(false);
    };
    reader.readAsDataURL(file);
  };

  const handleFetchUrl = async () => {
    if (!urlInput.trim()) return;
    setFetchingUrl(true);
    setUrlError(null);

    try {
      const proxyUrl = `/api/metadata-search/proxy-image?url=${encodeURIComponent(urlInput.trim())}`;
      const response = await fetch(proxyUrl);
      if (!response.ok) {
        throw new Error(`Failed to fetch image: status ${response.status}`);
      }
      const blob = await response.blob();
      const contentType = blob.type || "image/jpeg";

      const reader = new FileReader();
      reader.onloadend = () => {
        const result = reader.result as string;
        const base64Index = result.indexOf(";base64,");
        const cleanBase64 = base64Index !== -1 ? result.substring(base64Index + 8) : result;
        onCoverChange(cleanBase64, contentType);
        setUrlInput("");
        setDialogOpen(false);
      };
      reader.readAsDataURL(blob);
    } catch (err: unknown) {
      setUrlError(err instanceof Error ? err.message : "Failed to load image from URL");
    } finally {
      setFetchingUrl(false);
    }
  };

  const handleRemove = () => {
    onCoverChange(undefined, undefined);
  };

  return (
    <div className="flex flex-col items-center space-y-3">
      <div
        className="group border-border bg-muted hover:border-primary/50 relative flex h-48 w-48 cursor-pointer items-center justify-center overflow-hidden rounded-lg border-2 border-dashed transition-colors"
        onClick={() => setDialogOpen(true)}
      >
        {currentSrc ? (
          <>
            <img src={currentSrc} alt="Cover Preview" className="h-full w-full object-cover" />
            <Button
              variant="destructive"
              size="icon"
              className="absolute top-2 right-2 opacity-0 transition-opacity group-hover:opacity-100"
              onClick={(e) => {
                e.stopPropagation();
                handleRemove();
              }}
              type="button"
              aria-label="Remove cover"
            >
              <X className="h-4 w-4" />
            </Button>
          </>
        ) : (
          <div className="text-muted-foreground flex flex-col items-center p-4 text-center">
            <ImageIcon className="mb-2 h-10 w-10" />
            <span className="text-xs font-medium">Click to set cover</span>
          </div>
        )}
      </div>

      <Button variant="outline" size="sm" type="button" onClick={() => setDialogOpen(true)}>
        <Upload className="mr-2 h-4 w-4" />
        {currentSrc ? "Change Cover" : "Upload Cover"}
      </Button>

      <Dialog open={dialogOpen} onOpenChange={setDialogOpen}>
        <DialogContent className="max-w-md">
          <DialogHeader>
            <DialogTitle>Audiobook Cover</DialogTitle>
          </DialogHeader>

          <div className="space-y-4 py-2">
            {currentSrc && (
              <div className="flex justify-center">
                <img
                  src={currentSrc}
                  alt="Cover preview"
                  className="border-border max-h-48 rounded-md border object-contain shadow-sm"
                />
              </div>
            )}

            <div className="space-y-2">
              <label className="text-muted-foreground text-xs font-semibold uppercase">
                Fetch from URL
              </label>
              <div className="flex gap-2">
                <Input
                  placeholder="https://example.com/cover.jpg"
                  value={urlInput}
                  onChange={(e) => setUrlInput(e.target.value)}
                  onKeyDown={(e) => {
                    if (e.key === "Enter") {
                      e.preventDefault();
                      void handleFetchUrl();
                    }
                  }}
                />
                <Button
                  type="button"
                  variant="secondary"
                  disabled={fetchingUrl || !urlInput.trim()}
                  onClick={() => {
                    void handleFetchUrl();
                  }}
                >
                  <LinkIcon className="mr-1 h-3.5 w-3.5" />
                  Fetch
                </Button>
              </div>
              {urlError && <p className="text-destructive text-xs">{urlError}</p>}
            </div>

            <div className="space-y-2">
              <label className="text-muted-foreground text-xs font-semibold uppercase">
                Upload image file
              </label>
              <Input type="file" accept="image/*" onChange={handleFileUpload} />
            </div>

            <div className="border-border flex justify-between border-t pt-4">
              {currentSrc ? (
                <Button
                  type="button"
                  variant="destructive"
                  size="sm"
                  onClick={() => {
                    handleRemove();
                    setDialogOpen(false);
                  }}
                >
                  Remove Cover
                </Button>
              ) : (
                <div />
              )}
              <Button type="button" variant="outline" onClick={() => setDialogOpen(false)}>
                Close
              </Button>
            </div>
          </div>
        </DialogContent>
      </Dialog>
    </div>
  );
}

export default CoverEditor;

import React, { useState } from "react";
import { Upload, X, Image as ImageIcon } from "lucide-react";
import { Button } from "@/components/ui/button";

interface CoverEditorProps {
  coverUrl?: string;
  onCoverChange: (base64Cover: string | undefined) => void;
}

export const CoverEditor: React.FC<CoverEditorProps> = ({ coverUrl, onCoverChange }) => {
  const [preview, setPreview] = useState<string | undefined>(coverUrl);

  const handleFileChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0];
    if (file) {
      const reader = new FileReader();
      reader.onloadend = () => {
        const result = reader.result as string;
        setPreview(result);
        onCoverChange(result);
      };
      reader.readAsDataURL(file);
    }
  };

  const handleRemove = () => {
    setPreview(undefined);
    onCoverChange(undefined);
  };

  return (
    <div className="flex flex-col items-center space-y-3">
      <div className="w-48 h-48 border-2 border-dashed border-border rounded-lg overflow-hidden flex items-center justify-center bg-muted relative group">
        {preview ? (
          <>
            <img src={preview} alt="Cover Preview" className="w-full h-full object-cover" />
            <Button
              variant="destructive"
              size="icon"
              className="absolute top-2 right-2 opacity-0 group-hover:opacity-100 transition-opacity"
              onClick={handleRemove}
              type="button"
            >
              <X className="h-4 w-4" />
            </Button>
          </>
        ) : (
          <div className="flex flex-col items-center text-muted-foreground p-4 text-center">
            <ImageIcon className="h-10 w-10 mb-2" />
            <span className="text-xs">No cover image</span>
          </div>
        )}
      </div>
      <label className="cursor-pointer">
        <input type="file" accept="image/*" className="hidden" onChange={handleFileChange} />
        <Button variant="outline" size="sm" type="button" asChild>
          <span>
            <Upload className="h-4 w-4 mr-2" />
            Select Image
          </span>
        </Button>
      </label>
    </div>
  );
};
export default CoverEditor;

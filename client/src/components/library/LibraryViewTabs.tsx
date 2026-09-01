import { useNavigate } from "@tanstack/react-router";
import { BookOpen, BookMarked, Users } from "lucide-react";
import { Tabs, TabsList, TabsTrigger } from "@/components/ui/tabs";

export type LibraryViewTab = "books" | "series" | "authors";

export interface LibraryViewTabsProps {
  activeTab: LibraryViewTab;
  className?: string;
}

export function LibraryViewTabs({ activeTab, className }: LibraryViewTabsProps) {
  const navigate = useNavigate();

  const handleTabChange = (value: string | number) => {
    if (value === "books") {
      void navigate({ to: "/library" });
    } else if (value === "series") {
      void navigate({ to: "/library/series" });
    } else if (value === "authors") {
      void navigate({ to: "/library/authors" });
    }
  };

  return (
    <Tabs value={activeTab} onValueChange={handleTabChange} className={className}>
      <TabsList className="h-9">
        <TabsTrigger value="books" className="text-xs">
          <BookOpen className="mr-1.5 h-3.5 w-3.5" />
          Books
        </TabsTrigger>
        <TabsTrigger value="series" className="text-xs">
          <BookMarked className="mr-1.5 h-3.5 w-3.5" />
          Series
        </TabsTrigger>
        <TabsTrigger value="authors" className="text-xs">
          <Users className="mr-1.5 h-3.5 w-3.5" />
          Authors
        </TabsTrigger>
      </TabsList>
    </Tabs>
  );
}

export default LibraryViewTabs;

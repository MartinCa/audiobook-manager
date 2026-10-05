import { Link } from "@tanstack/react-router";
import { BookOpen, BookMarked, Users, CalendarClock } from "lucide-react";
import { Tabs, TabsList, TabsTrigger } from "@/components/ui/tabs";

export type LibraryViewTab = "books" | "series" | "authors" | "releases";

export interface LibraryViewTabsProps {
  activeTab: LibraryViewTab;
  className?: string;
}

// Route tabs: each trigger renders a real link so middle-click and "Open in new tab" work
// (see DESIGN.md section 3); a tab is a native <button> by default, hence nativeButton={false}.
export function LibraryViewTabs({ activeTab, className }: LibraryViewTabsProps) {
  return (
    <Tabs value={activeTab} className={className}>
      <TabsList className="h-9">
        <TabsTrigger
          value="books"
          nativeButton={false}
          render={<Link to="/library" />}
          className="text-xs"
        >
          <BookOpen className="mr-1.5 h-3.5 w-3.5" />
          Books
        </TabsTrigger>
        <TabsTrigger
          value="series"
          nativeButton={false}
          render={<Link to="/library/series" />}
          className="text-xs"
        >
          <BookMarked className="mr-1.5 h-3.5 w-3.5" />
          Series
        </TabsTrigger>
        <TabsTrigger
          value="authors"
          nativeButton={false}
          render={<Link to="/library/authors" />}
          className="text-xs"
        >
          <Users className="mr-1.5 h-3.5 w-3.5" />
          Authors
        </TabsTrigger>
        <TabsTrigger
          value="releases"
          nativeButton={false}
          render={<Link to="/library/upcoming-releases" />}
          className="text-xs"
        >
          <CalendarClock className="mr-1.5 h-3.5 w-3.5" />
          Releases
        </TabsTrigger>
      </TabsList>
    </Tabs>
  );
}

export default LibraryViewTabs;

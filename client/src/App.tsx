import { Routes, Route, Link, useLocation, Navigate } from "react-router-dom";
import {
  BookOpen,
  FolderInput,
  Library,
  Settings as SettingsIcon,
  ShieldAlert,
  Tag,
  Layers,
} from "lucide-react";
import { ThemeProvider } from "@/components/theme-provider";
import { ThemeToggle } from "@/components/theme-toggle";
import { SignalRProvider } from "@/components/SignalRProvider";
import { Toaster } from "@/components/ui/sonner";
import { TooltipProvider } from "@/components/ui/tooltip";

import BookList from "@/components/BookList";
import BookLibrary from "@/components/BookLibrary";
import BookDetail from "@/components/library/BookDetail";
import DiscoveredAudiobooks from "@/components/library/DiscoveredAudiobooks";
import SeriesOverviewPage from "@/components/library/SeriesOverview";
import SeriesDetail from "@/components/library/SeriesDetail";
import AuthorsList from "@/components/library/AuthorsList";
import AuthorDetail from "@/components/library/AuthorDetail";
import LibraryConsistency from "@/components/LibraryConsistency";
import MissingTags from "@/components/MissingTags";
import SimilarValues from "@/components/SimilarValues";
import Settings from "@/components/settings/Settings";
import LibrarySearch from "@/components/LibrarySearch";

function AppContent() {
  const location = useLocation();

  const navItems = [
    { path: "/", label: "Organize Queue", icon: FolderInput },
    { path: "/library", label: "Library", icon: Library },
    { path: "/library/consistency", label: "Consistency", icon: ShieldAlert },
    { path: "/library/missing-tags", label: "Missing Tags", icon: Tag },
    { path: "/library/similar-values", label: "Similar Values", icon: Layers },
    { path: "/settings", label: "Settings", icon: SettingsIcon },
  ];

  return (
    <div className="bg-background text-foreground flex min-h-screen flex-col">
      <header className="border-border bg-card sticky top-0 z-40 border-b">
        <div className="mx-auto flex h-16 max-w-7xl items-center justify-between gap-4 px-4 sm:px-6 lg:px-8">
          <Link to="/" className="flex shrink-0 items-center space-x-2">
            <BookOpen className="text-primary h-6 w-6" />
            <span className="text-foreground hidden text-lg font-bold sm:inline">
              Audiobook Manager
            </span>
          </Link>

          <div className="mx-2 max-w-sm flex-1">
            <LibrarySearch />
          </div>

          <div className="flex shrink-0 items-center space-x-2">
            <nav className="flex space-x-1 overflow-x-auto">
              {navItems.map((item) => {
                const Icon = item.icon;
                const isActive =
                  item.path === "/"
                    ? location.pathname === "/"
                    : location.pathname === item.path ||
                      (item.path === "/library" &&
                        location.pathname.startsWith("/library") &&
                        !navItems.some(
                          (other) =>
                            other.path !== "/library" && location.pathname.startsWith(other.path),
                        ));

                return (
                  <Link
                    key={item.path}
                    to={item.path}
                    className={`flex items-center space-x-1.5 rounded-md px-3 py-1.5 text-xs font-medium transition-colors ${
                      isActive
                        ? "bg-primary text-primary-foreground"
                        : "text-muted-foreground hover:bg-muted hover:text-foreground"
                    }`}
                  >
                    <Icon className="h-3.5 w-3.5" />
                    <span className="hidden md:inline">{item.label}</span>
                  </Link>
                );
              })}
            </nav>

            <ThemeToggle />
          </div>
        </div>
      </header>

      <main className="mx-auto w-full max-w-7xl flex-1 px-4 py-6 sm:px-6 lg:px-8">
        <Routes>
          <Route path="/" element={<BookList />} />
          <Route path="/library" element={<BookLibrary />} />
          <Route path="/library/book/:bookId" element={<BookDetail />} />
          <Route path="/library/discovered" element={<DiscoveredAudiobooks />} />
          <Route path="/library/series" element={<SeriesOverviewPage />} />
          <Route path="/library/series/:seriesName" element={<SeriesDetail />} />
          <Route path="/library/authors" element={<AuthorsList />} />
          <Route path="/library/authors/:authorId" element={<AuthorDetail />} />
          <Route path="/library/consistency" element={<LibraryConsistency />} />
          <Route path="/library/similar-values" element={<SimilarValues />} />
          <Route path="/library/missing-tags" element={<MissingTags />} />
          <Route path="/settings" element={<Settings />} />

          {/* Backward compatibility route redirects */}
          <Route path="/consistency" element={<Navigate to="/library/consistency" replace />} />
          <Route
            path="/similar-values"
            element={<Navigate to="/library/similar-values" replace />}
          />
          <Route path="/missing-tags" element={<Navigate to="/library/missing-tags" replace />} />
        </Routes>
      </main>

      <Toaster richColors />
    </div>
  );
}

export default function App() {
  return (
    <ThemeProvider defaultTheme="system" storageKey="theme">
      <SignalRProvider>
        <TooltipProvider>
          <AppContent />
        </TooltipProvider>
      </SignalRProvider>
    </ThemeProvider>
  );
}

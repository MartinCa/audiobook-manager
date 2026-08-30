import { Routes, Route, Link, useLocation } from "react-router-dom";
import {
  BookOpen,
  FolderInput,
  AlertTriangle,
  Tag,
  Layers,
  Settings as SettingsIcon,
} from "lucide-react";
import BookOrganize from "@/components/BookOrganize";
import BookLibrary from "@/components/BookLibrary";
import LibraryConsistency from "@/components/LibraryConsistency";
import MissingTags from "@/components/MissingTags";
import SimilarValues from "@/components/SimilarValues";
import Settings from "@/components/settings/Settings";

export default function App() {
  const location = useLocation();

  const navItems = [
    { path: "/", label: "Organize", icon: FolderInput },
    { path: "/library", label: "Library", icon: BookOpen },
    { path: "/consistency", label: "Consistency", icon: AlertTriangle },
    { path: "/missing-tags", label: "Missing Tags", icon: Tag },
    { path: "/similar-values", label: "Similar Values", icon: Layers },
    { path: "/settings", label: "Settings", icon: SettingsIcon },
  ];

  return (
    <div className="min-h-screen bg-background text-foreground flex flex-col">
      <header className="border-b border-border bg-card">
        <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8 flex items-center justify-between h-16">
          <div className="flex items-center space-x-3">
            <BookOpen className="h-6 w-6 text-primary" />
            <span className="font-bold text-lg">Audiobook Manager</span>
          </div>
          <nav className="flex space-x-1 sm:space-x-4">
            {navItems.map((item) => {
              const Icon = item.icon;
              const isActive =
                item.path === "/"
                  ? location.pathname === "/"
                  : location.pathname.startsWith(item.path);
              return (
                <Link
                  key={item.path}
                  to={item.path}
                  className={`flex items-center space-x-2 px-3 py-2 rounded-md text-sm font-medium transition-colors ${
                    isActive
                      ? "bg-primary text-primary-foreground"
                      : "text-muted-foreground hover:bg-muted hover:text-foreground"
                  }`}
                >
                  <Icon className="h-4 w-4" />
                  <span>{item.label}</span>
                </Link>
              );
            })}
          </nav>
        </div>
      </header>

      <main className="flex-1 max-w-7xl w-full mx-auto px-4 sm:px-6 lg:px-8 py-6">
        <Routes>
          <Route
            path="/"
            element={<BookOrganize />}
          />
          <Route
            path="/library/*"
            element={<BookLibrary />}
          />
          <Route
            path="/consistency"
            element={<LibraryConsistency />}
          />
          <Route
            path="/missing-tags"
            element={<MissingTags />}
          />
          <Route
            path="/similar-values"
            element={<SimilarValues />}
          />
          <Route
            path="/settings"
            element={<Settings />}
          />
        </Routes>
      </main>
    </div>
  );
}

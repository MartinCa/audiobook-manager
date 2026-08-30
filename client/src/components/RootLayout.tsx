import { Link, Outlet, useLocation } from "@tanstack/react-router";
import {
  BookOpen,
  FolderInput,
  Library,
  Settings as SettingsIcon,
  ShieldAlert,
  Tag,
  Layers,
} from "lucide-react";
import { ThemeToggle } from "@/components/theme-toggle";
import { Toaster } from "@/components/ui/sonner";
import LibrarySearch from "@/components/LibrarySearch";

const navItems = [
  { path: "/", label: "Organize Queue", icon: FolderInput },
  { path: "/library", label: "Library", icon: Library },
  { path: "/library/consistency", label: "Consistency", icon: ShieldAlert },
  { path: "/library/missing-tags", label: "Missing Tags", icon: Tag },
  { path: "/library/similar-values", label: "Similar Values", icon: Layers },
  { path: "/settings", label: "Settings", icon: SettingsIcon },
];

export function RootLayout() {
  const location = useLocation();

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
        <Outlet />
      </main>

      <Toaster richColors />
    </div>
  );
}

export default RootLayout;

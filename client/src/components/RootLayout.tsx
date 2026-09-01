import { Link, Outlet, useLocation, useNavigate } from "@tanstack/react-router";
import {
  BookOpen,
  FolderInput,
  FolderSearch,
  Library,
  Settings as SettingsIcon,
  ShieldAlert,
  Tag,
  Layers,
  Wrench,
  ChevronDown,
  Menu,
  BookMarked,
  Users,
} from "lucide-react";
import { Button } from "@/components/ui/button";
import {
  DropdownMenu,
  DropdownMenuTrigger,
  DropdownMenuContent,
  DropdownMenuGroup,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
} from "@/components/ui/dropdown-menu";
import { ThemeToggle } from "@/components/theme-toggle";
import { Toaster } from "@/components/ui/sonner";
import LibrarySearch from "@/components/LibrarySearch";
import { formatVersion, getReleaseUrl } from "@/helpers/versionHelpers";

export function RootLayout() {
  const location = useLocation();
  const navigate = useNavigate();
  const pathname = location.pathname;

  const isLibraryActive =
    pathname === "/library" ||
    pathname.startsWith("/library/series") ||
    pathname.startsWith("/library/authors") ||
    pathname.startsWith("/library/book");

  const isOrganizeActive = pathname === "/";
  const isDiscoveredActive = pathname === "/library/discovered";

  const isToolsActive =
    pathname.startsWith("/library/consistency") ||
    pathname.startsWith("/library/missing-tags") ||
    pathname.startsWith("/library/similar-values");

  const isSettingsActive = pathname === "/settings";

  return (
    <div className="bg-background text-foreground flex min-h-screen flex-col">
      <header className="border-border bg-card sticky top-0 z-40 border-b">
        <div className="mx-auto flex h-16 max-w-7xl items-center justify-between gap-2 px-3 sm:gap-4 sm:px-6 lg:px-8">
          <Link to="/" className="flex shrink-0 items-center space-x-2">
            <BookOpen className="text-primary h-6 w-6" />
            <span className="text-foreground hidden text-lg font-bold sm:inline">
              Audiobook Manager
            </span>
          </Link>

          <div className="mx-1 max-w-sm flex-1 sm:mx-2">
            <LibrarySearch />
          </div>

          {/* Desktop Navigation */}
          <div className="hidden items-center space-x-1 md:flex lg:space-x-2">
            <nav className="flex items-center space-x-1">
              <Link
                to="/library"
                className={`flex items-center space-x-1.5 rounded-md px-3 py-1.5 text-xs font-medium transition-colors ${
                  isLibraryActive
                    ? "bg-primary text-primary-foreground"
                    : "text-muted-foreground hover:bg-muted hover:text-foreground"
                }`}
              >
                <Library className="h-3.5 w-3.5" />
                <span>Library</span>
              </Link>

              <Link
                to="/"
                className={`flex items-center space-x-1.5 rounded-md px-3 py-1.5 text-xs font-medium transition-colors ${
                  isOrganizeActive
                    ? "bg-primary text-primary-foreground"
                    : "text-muted-foreground hover:bg-muted hover:text-foreground"
                }`}
              >
                <FolderInput className="h-3.5 w-3.5" />
                <span>Organize Queue</span>
              </Link>

              <Link
                to="/library/discovered"
                className={`flex items-center space-x-1.5 rounded-md px-3 py-1.5 text-xs font-medium transition-colors ${
                  isDiscoveredActive
                    ? "bg-primary text-primary-foreground"
                    : "text-muted-foreground hover:bg-muted hover:text-foreground"
                }`}
              >
                <FolderSearch className="h-3.5 w-3.5" />
                <span>Discovered Files</span>
              </Link>

              <DropdownMenu>
                <DropdownMenuTrigger
                  render={
                    <Button
                      variant="ghost"
                      size="sm"
                      className={`h-auto px-3 py-1.5 text-xs font-medium ${
                        isToolsActive
                          ? "bg-primary text-primary-foreground hover:bg-primary/90 hover:text-primary-foreground"
                          : "text-muted-foreground hover:bg-muted hover:text-foreground"
                      }`}
                    >
                      <Wrench className="mr-1.5 h-3.5 w-3.5" />
                      <span>Tools</span>
                      <ChevronDown className="ml-1 h-3 w-3 opacity-60" />
                    </Button>
                  }
                />
                <DropdownMenuContent align="end" className="w-48">
                  <DropdownMenuGroup>
                    <DropdownMenuLabel className="text-muted-foreground text-xs font-semibold uppercase">
                      Maintenance Tools
                    </DropdownMenuLabel>
                  </DropdownMenuGroup>
                  <DropdownMenuSeparator />
                  <DropdownMenuItem
                    onClick={() => {
                      void navigate({ to: "/library/consistency" });
                    }}
                    className="cursor-pointer text-xs"
                  >
                    <ShieldAlert className="text-primary mr-2 h-4 w-4" />
                    <span>Consistency Check</span>
                  </DropdownMenuItem>
                  <DropdownMenuItem
                    onClick={() => {
                      void navigate({ to: "/library/missing-tags" });
                    }}
                    className="cursor-pointer text-xs"
                  >
                    <Tag className="text-primary mr-2 h-4 w-4" />
                    <span>Missing Tags</span>
                  </DropdownMenuItem>
                  <DropdownMenuItem
                    onClick={() => {
                      void navigate({ to: "/library/similar-values" });
                    }}
                    className="cursor-pointer text-xs"
                  >
                    <Layers className="text-primary mr-2 h-4 w-4" />
                    <span>Similar Values</span>
                  </DropdownMenuItem>
                </DropdownMenuContent>
              </DropdownMenu>

              <Link
                to="/settings"
                className={`flex items-center space-x-1.5 rounded-md px-3 py-1.5 text-xs font-medium transition-colors ${
                  isSettingsActive
                    ? "bg-primary text-primary-foreground"
                    : "text-muted-foreground hover:bg-muted hover:text-foreground"
                }`}
              >
                <SettingsIcon className="h-3.5 w-3.5" />
                <span>Settings</span>
              </Link>
            </nav>

            <ThemeToggle />
          </div>

          {/* Mobile Navigation */}
          <div className="flex items-center space-x-1 md:hidden">
            <ThemeToggle />

            <DropdownMenu>
              <DropdownMenuTrigger
                render={
                  <Button variant="ghost" size="icon" className="h-9 w-9">
                    <Menu className="h-5 w-5" />
                    <span className="sr-only">Toggle navigation menu</span>
                  </Button>
                }
              />
              <DropdownMenuContent align="end" className="w-56">
                <DropdownMenuGroup>
                  <DropdownMenuLabel className="text-muted-foreground text-xs font-semibold uppercase">
                    Library
                  </DropdownMenuLabel>
                  <DropdownMenuItem
                    onClick={() => {
                      void navigate({ to: "/library" });
                    }}
                    className="cursor-pointer text-xs"
                  >
                    <BookOpen className="text-primary mr-2 h-4 w-4" />
                    <span>Books</span>
                  </DropdownMenuItem>
                  <DropdownMenuItem
                    onClick={() => {
                      void navigate({ to: "/library/series" });
                    }}
                    className="cursor-pointer text-xs"
                  >
                    <BookMarked className="text-primary mr-2 h-4 w-4" />
                    <span>Series</span>
                  </DropdownMenuItem>
                  <DropdownMenuItem
                    onClick={() => {
                      void navigate({ to: "/library/authors" });
                    }}
                    className="cursor-pointer text-xs"
                  >
                    <Users className="text-primary mr-2 h-4 w-4" />
                    <span>Authors</span>
                  </DropdownMenuItem>
                </DropdownMenuGroup>

                <DropdownMenuSeparator />

                <DropdownMenuGroup>
                  <DropdownMenuLabel className="text-muted-foreground text-xs font-semibold uppercase">
                    Intake & Organize
                  </DropdownMenuLabel>
                  <DropdownMenuItem
                    onClick={() => {
                      void navigate({ to: "/" });
                    }}
                    className="cursor-pointer text-xs"
                  >
                    <FolderInput className="text-primary mr-2 h-4 w-4" />
                    <span>Organize Queue</span>
                  </DropdownMenuItem>
                  <DropdownMenuItem
                    onClick={() => {
                      void navigate({ to: "/library/discovered" });
                    }}
                    className="cursor-pointer text-xs"
                  >
                    <FolderSearch className="text-primary mr-2 h-4 w-4" />
                    <span>Discovered Files</span>
                  </DropdownMenuItem>
                </DropdownMenuGroup>

                <DropdownMenuSeparator />

                <DropdownMenuGroup>
                  <DropdownMenuLabel className="text-muted-foreground text-xs font-semibold uppercase">
                    Tools
                  </DropdownMenuLabel>
                  <DropdownMenuItem
                    onClick={() => {
                      void navigate({ to: "/library/consistency" });
                    }}
                    className="cursor-pointer text-xs"
                  >
                    <ShieldAlert className="text-primary mr-2 h-4 w-4" />
                    <span>Consistency Check</span>
                  </DropdownMenuItem>
                  <DropdownMenuItem
                    onClick={() => {
                      void navigate({ to: "/library/missing-tags" });
                    }}
                    className="cursor-pointer text-xs"
                  >
                    <Tag className="text-primary mr-2 h-4 w-4" />
                    <span>Missing Tags</span>
                  </DropdownMenuItem>
                  <DropdownMenuItem
                    onClick={() => {
                      void navigate({ to: "/library/similar-values" });
                    }}
                    className="cursor-pointer text-xs"
                  >
                    <Layers className="text-primary mr-2 h-4 w-4" />
                    <span>Similar Values</span>
                  </DropdownMenuItem>
                </DropdownMenuGroup>

                <DropdownMenuSeparator />

                <DropdownMenuGroup>
                  <DropdownMenuItem
                    onClick={() => {
                      void navigate({ to: "/settings" });
                    }}
                    className="cursor-pointer text-xs"
                  >
                    <SettingsIcon className="text-primary mr-2 h-4 w-4" />
                    <span>Settings</span>
                  </DropdownMenuItem>
                </DropdownMenuGroup>

                <DropdownMenuSeparator />

                <div className="text-muted-foreground flex items-center justify-between px-2 py-1.5 text-xs">
                  <span>Version</span>
                  <a
                    href={getReleaseUrl(__APP_VERSION__)}
                    target="_blank"
                    rel="noreferrer"
                    className="hover:text-foreground font-mono transition-colors"
                  >
                    {formatVersion(__APP_VERSION__)}
                  </a>
                </div>
              </DropdownMenuContent>
            </DropdownMenu>
          </div>
        </div>
      </header>

      <main className="mx-auto w-full max-w-7xl min-w-0 flex-1 px-4 py-6 sm:px-6 lg:px-8">
        <Outlet />
      </main>

      <footer className="border-border text-muted-foreground border-t py-4 text-xs">
        <div className="mx-auto flex max-w-7xl items-center justify-between px-4 sm:px-6 lg:px-8">
          <span>Audiobook Manager</span>
          <a
            href={getReleaseUrl(__APP_VERSION__)}
            target="_blank"
            rel="noreferrer"
            className="hover:text-foreground font-mono transition-colors"
          >
            {formatVersion(__APP_VERSION__)}
          </a>
        </div>
      </footer>

      <Toaster richColors />
    </div>
  );
}

export default RootLayout;

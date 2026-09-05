import { createFileRoute } from "@tanstack/react-router";
import LibrarySettingsPage from "@/components/settings/LibrarySettingsPage";

export const Route = createFileRoute("/settings/library")({
  component: LibrarySettingsPage,
});

import { createFileRoute } from "@tanstack/react-router";
import ScheduledTasksPage from "@/components/settings/ScheduledTasksPage";

export const Route = createFileRoute("/settings/tasks")({
  component: ScheduledTasksPage,
});

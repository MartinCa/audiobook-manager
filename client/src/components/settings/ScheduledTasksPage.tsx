import { useQuery } from "@tanstack/react-query";
import { CalendarClock, Loader2 } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import { settingsApi } from "@/services/api";
import { queryKeys } from "@/lib/queryKeys";
import { formatDateTime, formatDuration } from "@/helpers/formatHelpers";

/**
 * Every registered scheduled background task (currently just the upcoming-releases refresh),
 * its cron schedule and its most recent run - read-only. The schedule itself is edited on the
 * Library Settings page; this is where to check whether it is actually running and succeeding.
 */
export function ScheduledTasksPage() {
  const { data, isLoading } = useQuery({
    queryKey: queryKeys.scheduledTasks(),
    queryFn: () => settingsApi.getScheduledTasks(),
    // Runs can be minutes apart and their status is not urgent to the second, but the page is
    // still useful as a lightweight "is it stuck" check when left open - refetch occasionally.
    refetchInterval: 30_000,
  });

  return (
    <div className="max-w-4xl space-y-6">
      <div>
        <h1 className="text-foreground flex items-center gap-2 text-2xl font-bold">
          <CalendarClock className="text-primary h-6 w-6" />
          Tasks
        </h1>
        <p className="text-muted-foreground text-sm">
          Scheduled background tasks, their cron schedule and their most recent run.
        </p>
      </div>

      <Card>
        <CardHeader>
          <CardTitle className="flex items-center gap-2 text-lg">
            <CalendarClock className="text-primary h-5 w-5" />
            Scheduled Tasks
          </CardTitle>
          <CardDescription>
            Every task's schedule is UTC. "Next Run" is blank when the task is disabled.
          </CardDescription>
        </CardHeader>
        <CardContent>
          {isLoading ? (
            <div className="text-muted-foreground flex items-center justify-center py-8">
              <Loader2 className="text-primary mr-2 h-5 w-5 animate-spin" />
              <span className="text-sm">Loading tasks...</span>
            </div>
          ) : !data || data.length === 0 ? (
            <p className="text-muted-foreground py-8 text-center text-sm">
              No scheduled tasks are registered.
            </p>
          ) : (
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Task</TableHead>
                  <TableHead>Cron Schedule</TableHead>
                  <TableHead>Last Run</TableHead>
                  <TableHead>Duration</TableHead>
                  <TableHead>Status</TableHead>
                  <TableHead>Next Run</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {data.map((task) => (
                  <TableRow key={task.key}>
                    <TableCell className="font-medium">{task.name}</TableCell>
                    <TableCell className="font-mono text-xs">{task.cronSchedule}</TableCell>
                    <TableCell>
                      {task.lastRunAt ? formatDateTime(task.lastRunAt) : "Never"}
                    </TableCell>
                    <TableCell>
                      {task.lastRunDurationMs != null
                        ? formatDuration(task.lastRunDurationMs / 1000)
                        : "—"}
                    </TableCell>
                    <TableCell>
                      {task.lastRunStatus === "Success" ? (
                        <Badge variant="secondary">Success</Badge>
                      ) : task.lastRunStatus === "Failed" ? (
                        <Badge variant="destructive">Failed</Badge>
                      ) : (
                        "—"
                      )}
                    </TableCell>
                    <TableCell>
                      {task.enabled && task.nextRunAt ? formatDateTime(task.nextRunAt) : "—"}
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          )}
        </CardContent>
      </Card>
    </div>
  );
}

export default ScheduledTasksPage;

import type { components } from "@/lib/api-types";
import type { Require } from "@/lib/dto";

// One row of GET api/settings/tasks. Require<> keys checked against
// AudiobookManager.Api.Dtos.ScheduledTaskDto - key/name/cronSchedule/enabled are always sent;
// the last-run/next-run fields are genuinely nullable (never run yet, or disabled/unparsable
// cron), so they stay optional/nullable as the generated type already has them.
export type ScheduledTask = Require<
  components["schemas"]["ScheduledTaskDto"],
  "key" | "name" | "cronSchedule" | "enabled"
>;

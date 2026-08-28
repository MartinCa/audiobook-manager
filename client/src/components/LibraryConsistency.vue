<template>
  <v-container>
    <v-row class="text-center">
      <v-col class="mb-5">
        <h2 class="headline font-weight-bold mb-5">Library Consistency</h2>
      </v-col>
    </v-row>
    <v-row>
      <v-col cols="12">
        <v-btn
          class="mr-3"
          to="/library"
          prepend-icon="mdi-arrow-left"
        >
          Back to Library
        </v-btn>
        <v-btn
          :disabled="checking"
          :loading="checking"
          @click="startCheck()"
          prepend-icon="mdi-check-decagram"
        >
          Run Check
        </v-btn>
        <div class="text-caption text-medium-emphasis mt-2">
          Verifies that every book in the library has the correct file path,
          sidecar metadata files (desc.txt, reader.txt), and a cover image, and
          flags leftover folders with no audio file.
        </div>
      </v-col>
    </v-row>
    <v-row v-if="checking">
      <v-col cols="12">
        <OperationProgressBar
          class="mt-3"
          :processed="checkBooksChecked"
          :total="checkTotalBooks"
        />
        <div class="text-caption mt-1">{{ checkMessage }}</div>
        <div class="text-caption">Issues found: {{ checkIssuesFound }}</div>
      </v-col>
    </v-row>
    <v-row v-if="checkComplete">
      <v-col cols="12">
        <v-alert
          type="info"
          class="mt-3"
          closable
          @click:close="checkComplete = false"
        >
          Check complete: {{ completeTotalBooks }} books checked,
          {{ completeTotalIssues }} issues found.
        </v-alert>
      </v-col>
    </v-row>
    <v-row v-if="issues.length > 0">
      <v-col cols="12">
        <h3 class="text-h6 mb-3">Issues ({{ issues.length }})</h3>
        <v-expansion-panels>
          <v-expansion-panel
            v-for="group in groupedByType"
            :key="group.issueType"
          >
            <v-expansion-panel-title>
              <v-row align="center">
                <v-col class="d-flex align-center">
                  <v-icon
                    :icon="getIssueIcon(group.issueType)"
                    class="mr-2"
                  />
                  {{ getIssueTypeLabel(group.issueType) }}
                  <v-icon
                    icon="mdi-information-outline"
                    size="small"
                    class="ml-2 text-medium-emphasis"
                    @click.stop
                  />
                  <v-tooltip
                    activator="parent"
                    location="bottom"
                    max-width="320"
                  >
                    {{ getBulkResolveDescription(group.issueType) }}
                  </v-tooltip>
                </v-col>
                <v-col cols="auto">
                  <v-chip
                    size="small"
                    color="warning"
                  >
                    {{ group.issues.length }}
                  </v-chip>
                </v-col>
              </v-row>
            </v-expansion-panel-title>
            <v-expansion-panel-text>
              <div
                class="d-flex align-center justify-space-between mb-2 flex-wrap ga-2"
              >
                <div class="d-flex align-center">
                  <v-checkbox
                    :model-value="isGroupFullySelected(group)"
                    :indeterminate="isGroupPartiallySelected(group)"
                    density="compact"
                    hide-details
                    label="Select all visible"
                    @update:model-value="toggleSelectAllVisible(group)"
                  />
                  <span
                    v-if="selectedCountInGroup(group) > 0"
                    class="text-caption text-medium-emphasis ml-2"
                  >
                    {{ selectedCountInGroup(group) }} selected
                  </span>
                </div>
                <div>
                  <v-btn
                    v-if="selectedCountInGroup(group) > 0"
                    size="small"
                    variant="outlined"
                    color="primary"
                    class="mr-2"
                    :loading="resolvingSelectedTypes.has(group.issueType)"
                    :disabled="resolvingSelectedTypes.has(group.issueType)"
                    @click.stop="onResolveSelectedClick(group)"
                  >
                    Resolve Selected ({{ selectedCountInGroup(group) }})
                  </v-btn>
                  <v-btn
                    size="small"
                    variant="outlined"
                    :loading="resolvingTypes.has(group.issueType)"
                    :disabled="resolvingTypes.has(group.issueType)"
                    @click.stop="onBulkResolveClick(group.issueType)"
                  >
                    Resolve All {{ group.issues.length }}
                  </v-btn>
                </div>
              </div>
              <v-list density="compact">
                <v-list-item
                  v-for="issue in group.visibleIssues"
                  :key="issue.id"
                  class="issue-item"
                >
                  <template v-slot:prepend>
                    <v-checkbox
                      :model-value="selectedIssueIds.has(issue.id)"
                      density="compact"
                      hide-details
                      class="mr-1"
                      @click.stop
                      @update:model-value="toggleIssueSelected(issue.id)"
                    />
                    <v-icon :icon="getIssueIcon(issue.issueType)" />
                  </template>
                  <v-list-item-title class="text-wrap">
                    <router-link
                      :to="`/library/book/${issue.audiobookId}`"
                      class="text-decoration-none"
                    >
                      {{ issue.authors.join(", ") }} &mdash;
                      {{ issue.bookName }}
                    </router-link>
                  </v-list-item-title>
                  <v-list-item-subtitle class="issue-subtitle text-wrap">
                    <div>{{ issue.description }}</div>
                    <DiffDisplay
                      v-if="issue.expectedValue && issue.actualValue"
                      :expected="issue.expectedValue"
                      :actual="issue.actualValue"
                    />
                    <template v-else>
                      <div
                        v-if="issue.expectedValue"
                        class="text-wrap"
                      >
                        Expected: {{ issue.expectedValue }}
                      </div>
                      <div
                        v-if="issue.actualValue"
                        class="text-wrap"
                      >
                        Actual: {{ issue.actualValue }}
                      </div>
                    </template>
                  </v-list-item-subtitle>
                  <template v-slot:append>
                    <v-btn
                      size="small"
                      variant="outlined"
                      :loading="resolvingIds.has(issue.id)"
                      @click.stop="onResolveClick(issue)"
                    >
                      Resolve
                    </v-btn>
                    <v-tooltip
                      activator="parent"
                      location="left"
                      max-width="320"
                    >
                      {{ getBulkResolveDescription(issue.issueType) }}
                    </v-tooltip>
                  </template>
                </v-list-item>
              </v-list>
              <div
                v-if="group.issues.length > group.displayCount"
                class="text-center mt-2"
              >
                <v-btn
                  variant="text"
                  size="small"
                  @click="showMore(group.issueType)"
                >
                  Show more ({{ group.issues.length - group.displayCount }}
                  remaining)
                </v-btn>
              </div>
            </v-expansion-panel-text>
          </v-expansion-panel>
        </v-expansion-panels>
      </v-col>
    </v-row>
    <v-row v-if="orphanDirectories.length > 0">
      <v-col cols="12">
        <h3 class="text-h6 mb-3">
          Orphaned Directories ({{ orphanDirectories.length }})
        </h3>
        <v-card variant="outlined">
          <v-card-text>
            <div class="d-flex justify-end mb-2">
              <v-btn
                size="small"
                variant="outlined"
                :loading="resolvingAllOrphans"
                :disabled="resolvingAllOrphans"
                @click="onBulkResolveOrphansClick()"
              >
                Delete All {{ orphanDirectories.length }}
              </v-btn>
            </div>
            <v-list density="compact">
              <v-list-item
                v-for="dir in orphanDirectories"
                :key="dir.id"
              >
                <template v-slot:prepend>
                  <v-icon icon="mdi-folder-remove" />
                </template>
                <v-list-item-title class="text-wrap">
                  {{ dir.directoryPath }}
                </v-list-item-title>
                <template v-slot:append>
                  <v-btn
                    size="small"
                    variant="outlined"
                    :loading="resolvingOrphanIds.has(dir.id)"
                    @click.stop="onResolveOrphanClick(dir)"
                  >
                    Delete
                  </v-btn>
                </template>
              </v-list-item>
            </v-list>
          </v-card-text>
        </v-card>
      </v-col>
    </v-row>
    <v-row
      v-if="
        !checking &&
        !checkComplete &&
        issues.length === 0 &&
        orphanDirectories.length === 0
      "
    >
      <v-col cols="12">
        <div class="text-center mt-5">
          Run a consistency check to find issues in your library.
        </div>
      </v-col>
    </v-row>

    <v-dialog
      v-model="orphanConfirmDialog"
      max-width="500"
    >
      <v-card>
        <v-card-title>Confirm Deletion</v-card-title>
        <v-card-text>
          <template v-if="pendingOrphanBulk">
            This will permanently delete
            <strong>all {{ orphanDirectories.length }}</strong>
            orphaned directories (folders with no audio file) and everything in
            them. This action cannot be undone.
          </template>
          <template v-else-if="pendingOrphanDirectory">
            This will permanently delete
            <strong>{{ pendingOrphanDirectory.directoryPath }}</strong>
            and everything in it. This action cannot be undone.
          </template>
        </v-card-text>
        <v-card-actions>
          <v-spacer />
          <v-btn @click="cancelOrphanConfirm()">Cancel</v-btn>
          <v-btn
            color="error"
            @click="confirmOrphanResolve()"
          >
            Delete
          </v-btn>
        </v-card-actions>
      </v-card>
    </v-dialog>

    <v-dialog
      v-model="confirmDialog"
      max-width="500"
    >
      <v-card>
        <v-card-title>Confirm Resolution</v-card-title>
        <v-card-text>
          <template
            v-if="
              pendingSelectedType === 'MissingMediaFile' ||
              pendingBulkType === 'MissingMediaFile'
            "
          >
            This will remove
            <strong>
              {{ pendingSelectedType ? "the selected" : "all" }}
              {{
                pendingSelectedType
                  ? pendingSelectedIssueIds.length
                  : pendingBulkCount
              }}
              audiobooks
            </strong>
            with missing media files from the database and clean up empty
            directories. This action cannot be undone.
          </template>
          <template v-else-if="pendingSelectedType">
            This will resolve the selected
            <strong>{{ pendingSelectedIssueIds.length }}</strong>
            {{ getIssueTypeLabel(pendingSelectedType) }} issue{{
              pendingSelectedIssueIds.length === 1 ? "" : "s"
            }}.
            {{ getBulkResolveDescription(pendingSelectedType) }}
          </template>
          <template v-else-if="pendingBulkType">
            This will resolve all
            <strong>{{ pendingBulkCount }}</strong>
            {{ getIssueTypeLabel(pendingBulkType) }} issues.
            {{ getBulkResolveDescription(pendingBulkType) }}
          </template>
          <template v-else>
            This will remove the audiobook from the database and clean up empty
            directories. This action cannot be undone.
          </template>
        </v-card-text>
        <v-card-actions>
          <v-spacer />
          <v-btn @click="cancelConfirm()">Cancel</v-btn>
          <v-btn
            color="error"
            @click="confirmResolve()"
          >
            {{
              pendingSelectedType
                ? "Resolve Selected"
                : pendingBulkType
                  ? "Resolve All"
                  : "Remove"
            }}
          </v-btn>
        </v-card-actions>
      </v-card>
    </v-dialog>

    <v-snackbar
      v-model="snackbar"
      :timeout="3000"
    >
      {{ snackbarText }}
    </v-snackbar>
  </v-container>
</template>

<script setup lang="ts">
import { computed, Ref, ref, onMounted, reactive } from "vue";
import ConsistencyService from "../services/ConsistencyService";
import ConsistencyIssue from "../types/ConsistencyIssue";
import OrphanDirectory from "../types/OrphanDirectory";
import DiffDisplay from "./DiffDisplay.vue";
import OperationProgressBar from "./OperationProgressBar.vue";
import { HubEventToken } from "@/signalr/hub";
import { useOperationProgress } from "../composables/useOperationProgress";
import { ConsistencyCheckProgress } from "../signalr/ConsistencyCheckProgress";
import { ConsistencyCheckComplete } from "../signalr/ConsistencyCheckComplete";
import { getIssueIcon } from "../helpers/consistencyIssueDisplay";

const PAGE_SIZE = 50;

const ConsistencyCheckProgressToken: HubEventToken<ConsistencyCheckProgress> =
  "ConsistencyCheckProgress";
const ConsistencyCheckCompleteToken: HubEventToken<ConsistencyCheckComplete> =
  "ConsistencyCheckComplete";

const issues: Ref<ConsistencyIssue[]> = ref([]);
const checkMessage: Ref<string> = ref("");
const checkIssuesFound: Ref<number> = ref(0);
const checkComplete: Ref<boolean> = ref(false);
const completeTotalBooks: Ref<number> = ref(0);
const completeTotalIssues: Ref<number> = ref(0);
const resolvingIds: Ref<Set<number>> = ref(new Set());
const resolvingTypes: Ref<Set<string>> = ref(new Set());
const confirmDialog: Ref<boolean> = ref(false);
const pendingResolveIssue: Ref<ConsistencyIssue | null> = ref(null);
const pendingBulkType: Ref<string | null> = ref(null);
const pendingBulkCount: Ref<number> = ref(0);
const selectedIssueIds: Ref<Set<number>> = ref(new Set());
const resolvingSelectedTypes: Ref<Set<string>> = ref(new Set());
const pendingSelectedType: Ref<string | null> = ref(null);
const pendingSelectedIssueIds: Ref<number[]> = ref([]);
const snackbar: Ref<boolean> = ref(false);
const snackbarText: Ref<string> = ref("");
const displayCounts: Record<string, number> = reactive({});

const orphanDirectories: Ref<OrphanDirectory[]> = ref([]);
const resolvingOrphanIds: Ref<Set<number>> = ref(new Set());
const resolvingAllOrphans: Ref<boolean> = ref(false);
const orphanConfirmDialog: Ref<boolean> = ref(false);
const pendingOrphanDirectory: Ref<OrphanDirectory | null> = ref(null);
const pendingOrphanBulk: Ref<boolean> = ref(false);

interface TypeGroup {
  issueType: string;
  issues: ConsistencyIssue[];
  displayCount: number;
  visibleIssues: ConsistencyIssue[];
}

// Depends only on `issues`, so paginating one group ("show more", which only touches
// `displayCounts`) doesn't force the whole issues array to be re-grouped by type.
const issuesByType = computed((): Map<string, ConsistencyIssue[]> => {
  const groups = new Map<string, ConsistencyIssue[]>();
  for (const issue of issues.value) {
    if (!groups.has(issue.issueType)) {
      groups.set(issue.issueType, []);
    }
    groups.get(issue.issueType)!.push(issue);
  }
  return groups;
});

const groupedByType = computed((): TypeGroup[] => {
  return Array.from(issuesByType.value.entries()).map(
    ([issueType, typeIssues]) => {
      const displayCount = displayCounts[issueType] || PAGE_SIZE;
      return {
        issueType,
        issues: typeIssues,
        displayCount,
        visibleIssues: typeIssues.slice(0, displayCount),
      };
    },
  );
});

const showMore = (issueType: string) => {
  const current = displayCounts[issueType] || PAGE_SIZE;
  displayCounts[issueType] = current + PAGE_SIZE;
};

const getIssueTypeLabel = (issueType: string): string => {
  switch (issueType) {
    case "MissingMediaFile":
      return "Missing Media Files";
    case "WrongFilePath":
      return "Wrong File Paths";
    case "MissingDescTxt":
      return "Missing Description Files";
    case "IncorrectDescTxt":
      return "Incorrect Description Files";
    case "MissingReaderTxt":
      return "Missing Reader Files";
    case "IncorrectReaderTxt":
      return "Incorrect Reader Files";
    case "MissingCoverFile":
      return "Missing Cover Files";
    case "MissingOpfFile":
      return "Missing OPF Files";
    case "IncorrectOpfFile":
      return "Incorrect OPF Files";
    case "TagMismatch":
      return "Tag Mismatches";
    default:
      return issueType;
  }
};

const {
  isRunning: checking,
  processed: checkBooksChecked,
  total: checkTotalBooks,
  start: startChecking,
} = useOperationProgress<ConsistencyCheckProgress, ConsistencyCheckComplete>({
  key: "consistency-check",
  progressToken: ConsistencyCheckProgressToken,
  completeToken: ConsistencyCheckCompleteToken,
  getProcessed: (arg) => arg.booksChecked,
  getTotal: (arg) => arg.totalBooks,
  onProgress: (arg) => {
    checkMessage.value = arg.message;
    checkIssuesFound.value = arg.issuesFound;
  },
  onComplete: (arg) => {
    checkComplete.value = true;
    completeTotalBooks.value = arg.totalBooksChecked;
    completeTotalIssues.value = arg.totalIssuesFound;
    loadIssues();
    loadOrphanDirectories();
  },
});

const startCheck = async () => {
  startChecking();
  checkComplete.value = false;
  checkIssuesFound.value = 0;
  checkMessage.value = "";
  issues.value = [];
  await ConsistencyService.startCheck();
};

// These are awaited from click handlers and fired from SignalR completion callbacks, and both
// overwrite shared state, so they need the two standard guards: swallow the failure (an
// unhandled rejection out of a `finally` would otherwise escape the caller that has already
// reported its own result), and ignore a response that a newer load has superseded.
let loadIssuesRequestId = 0;

const loadIssues = async () => {
  const requestId = ++loadIssuesRequestId;
  try {
    const loaded = await ConsistencyService.getIssues();
    if (requestId !== loadIssuesRequestId) return;
    issues.value = loaded;
  } catch {
    // Keep the list we already have rather than blanking it on a transient failure.
    snackbarText.value = "Failed to refresh the issue list";
    snackbar.value = true;
  }
};

let loadOrphansRequestId = 0;

const loadOrphanDirectories = async () => {
  const requestId = ++loadOrphansRequestId;
  try {
    const loaded = await ConsistencyService.getOrphanDirectories();
    if (requestId !== loadOrphansRequestId) return;
    orphanDirectories.value = loaded;
  } catch {
    snackbarText.value = "Failed to refresh the orphaned directory list";
    snackbar.value = true;
  }
};

const getBulkResolveDescription = (issueType: string): string => {
  switch (issueType) {
    case "WrongFilePath":
      return "Each audiobook file will be moved to its correct location based on library metadata.";
    case "MissingDescTxt":
    case "IncorrectDescTxt":
      return "A desc.txt sidecar file containing the book description will be created or updated for each affected book.";
    case "MissingReaderTxt":
    case "IncorrectReaderTxt":
      return "A reader.txt sidecar file containing narrator information will be created or updated for each affected book.";
    case "MissingCoverFile":
      return "The cover image will be extracted from each affected audiobook file.";
    case "MissingOpfFile":
    case "IncorrectOpfFile":
      return "A metadata.opf sidecar file will be created or updated for each affected book.";
    case "TagMismatch":
      return "Each audiobook file's m4b tags will be rewritten to match the library metadata (author, series, series part, year, etc.), and the file relocated if that changes its path.";
    default:
      return "Continue?";
  }
};

const onResolveClick = (issue: ConsistencyIssue) => {
  if (issue.issueType === "MissingMediaFile") {
    pendingResolveIssue.value = issue;
    pendingBulkType.value = null;
    confirmDialog.value = true;
  } else {
    resolveIssue(issue);
  }
};

const onBulkResolveClick = (issueType: string) => {
  const group = groupedByType.value.find((g) => g.issueType === issueType);
  if (!group) return;

  pendingBulkType.value = issueType;
  pendingBulkCount.value = group.issues.length;
  pendingResolveIssue.value = null;
  pendingSelectedType.value = null;
  pendingSelectedIssueIds.value = [];
  confirmDialog.value = true;
};

// Selection state for every group, computed once per selection change. These used to be plain
// functions called from the template - `selectedCountInGroup` twice per group - so ticking a
// single checkbox re-scanned every group's full issue array four or more times.
interface GroupSelection {
  selected: number;
  fullySelected: boolean;
  partiallySelected: boolean;
}

const groupSelectionState = computed((): Map<string, GroupSelection> => {
  const state = new Map<string, GroupSelection>();
  for (const group of groupedByType.value) {
    let selected = 0;
    for (const issue of group.issues) {
      if (selectedIssueIds.value.has(issue.id)) selected++;
    }

    let visibleSelected = 0;
    for (const issue of group.visibleIssues) {
      if (selectedIssueIds.value.has(issue.id)) visibleSelected++;
    }

    const fullySelected =
      group.visibleIssues.length > 0 &&
      visibleSelected === group.visibleIssues.length;

    state.set(group.issueType, {
      selected,
      fullySelected,
      partiallySelected: !fullySelected && visibleSelected > 0,
    });
  }
  return state;
});

const EMPTY_SELECTION: GroupSelection = {
  selected: 0,
  fullySelected: false,
  partiallySelected: false,
};

const groupSelection = (group: TypeGroup): GroupSelection =>
  groupSelectionState.value.get(group.issueType) ?? EMPTY_SELECTION;

const selectedCountInGroup = (group: TypeGroup): number =>
  groupSelection(group).selected;

const isGroupFullySelected = (group: TypeGroup): boolean =>
  groupSelection(group).fullySelected;

const isGroupPartiallySelected = (group: TypeGroup): boolean =>
  groupSelection(group).partiallySelected;

const toggleIssueSelected = (issueId: number) => {
  if (selectedIssueIds.value.has(issueId)) {
    selectedIssueIds.value.delete(issueId);
  } else {
    selectedIssueIds.value.add(issueId);
  }
};

const toggleSelectAllVisible = (group: TypeGroup) => {
  if (isGroupFullySelected(group)) {
    for (const issue of group.visibleIssues) {
      selectedIssueIds.value.delete(issue.id);
    }
  } else {
    for (const issue of group.visibleIssues) {
      selectedIssueIds.value.add(issue.id);
    }
  }
};

const onResolveSelectedClick = (group: TypeGroup) => {
  const ids = group.issues
    .filter((i) => selectedIssueIds.value.has(i.id))
    .map((i) => i.id);
  if (ids.length === 0) return;

  pendingSelectedType.value = group.issueType;
  pendingSelectedIssueIds.value = ids;
  pendingBulkType.value = null;
  pendingResolveIssue.value = null;
  confirmDialog.value = true;
};

const cancelConfirm = () => {
  confirmDialog.value = false;
  pendingResolveIssue.value = null;
  pendingBulkType.value = null;
  pendingSelectedType.value = null;
  pendingSelectedIssueIds.value = [];
};

const confirmResolve = () => {
  if (pendingSelectedType.value && pendingSelectedIssueIds.value.length > 0) {
    resolveSelected(pendingSelectedType.value, pendingSelectedIssueIds.value);
  } else if (pendingBulkType.value) {
    bulkResolve(pendingBulkType.value);
  } else if (pendingResolveIssue.value) {
    resolveIssue(pendingResolveIssue.value);
  }
  confirmDialog.value = false;
  pendingResolveIssue.value = null;
  pendingBulkType.value = null;
  pendingSelectedType.value = null;
  pendingSelectedIssueIds.value = [];
};

const resolveSelected = async (issueType: string, ids: number[]) => {
  resolvingSelectedTypes.value.add(issueType);
  try {
    const result = await ConsistencyService.resolveSelectedIssues(ids);
    for (const id of ids) {
      selectedIssueIds.value.delete(id);
    }
    let msg = `Resolved ${result.resolved} issues`;
    if (result.failed > 0) {
      msg += ` (${result.failed} failed)`;
    }
    snackbarText.value = msg;
    snackbar.value = true;
  } catch {
    snackbarText.value = "Failed to resolve selected issues";
    snackbar.value = true;
  } finally {
    resolvingSelectedTypes.value.delete(issueType);
    await loadIssues();
  }
};

const bulkResolve = async (issueType: string) => {
  resolvingTypes.value.add(issueType);
  try {
    const result = await ConsistencyService.resolveByType(issueType);
    let msg = `Resolved ${result.resolved} issues`;
    if (result.failed > 0) {
      msg += ` (${result.failed} failed)`;
    }
    snackbarText.value = msg;
    snackbar.value = true;
  } catch {
    snackbarText.value = "Failed to bulk resolve issues";
    snackbar.value = true;
  } finally {
    resolvingTypes.value.delete(issueType);
    // Re-read the authoritative list rather than removing the whole type optimistically: a
    // bulk resolve reports `failed`, and the issues behind those failures are still real. The
    // old client-side filter hid them until the next full check.
    await loadIssues();
  }
};

const resolveIssue = async (issue: ConsistencyIssue) => {
  resolvingIds.value.add(issue.id);
  try {
    await ConsistencyService.resolveIssue(issue.id);
    issues.value = issues.value.filter((i) => {
      if (
        issue.issueType === "MissingMediaFile" ||
        issue.issueType === "WrongFilePath"
      ) {
        return i.audiobookId !== issue.audiobookId;
      }
      if (
        issue.issueType === "MissingDescTxt" ||
        issue.issueType === "IncorrectDescTxt" ||
        issue.issueType === "MissingReaderTxt" ||
        issue.issueType === "IncorrectReaderTxt" ||
        issue.issueType === "MissingOpfFile" ||
        issue.issueType === "IncorrectOpfFile"
      ) {
        return !(
          i.audiobookId === issue.audiobookId &&
          (i.issueType === "MissingDescTxt" ||
            i.issueType === "IncorrectDescTxt" ||
            i.issueType === "MissingReaderTxt" ||
            i.issueType === "IncorrectReaderTxt" ||
            i.issueType === "MissingOpfFile" ||
            i.issueType === "IncorrectOpfFile")
        );
      }
      return i.id !== issue.id;
    });
    snackbarText.value = "Issue resolved successfully";
    snackbar.value = true;
  } catch {
    snackbarText.value = "Failed to resolve issue";
    snackbar.value = true;
    // The optimistic removal above only runs on success, so on failure the list is unchanged -
    // but the server may still have partially resolved, so re-read rather than guess.
    await loadIssues();
  } finally {
    resolvingIds.value.delete(issue.id);
  }
};

const onResolveOrphanClick = (dir: OrphanDirectory) => {
  pendingOrphanDirectory.value = dir;
  pendingOrphanBulk.value = false;
  orphanConfirmDialog.value = true;
};

const onBulkResolveOrphansClick = () => {
  pendingOrphanDirectory.value = null;
  pendingOrphanBulk.value = true;
  orphanConfirmDialog.value = true;
};

const cancelOrphanConfirm = () => {
  orphanConfirmDialog.value = false;
  pendingOrphanDirectory.value = null;
  pendingOrphanBulk.value = false;
};

const confirmOrphanResolve = () => {
  if (pendingOrphanBulk.value) {
    bulkResolveOrphans();
  } else if (pendingOrphanDirectory.value) {
    resolveOrphanDirectory(pendingOrphanDirectory.value);
  }
  orphanConfirmDialog.value = false;
  pendingOrphanDirectory.value = null;
  pendingOrphanBulk.value = false;
};

const resolveOrphanDirectory = async (dir: OrphanDirectory) => {
  resolvingOrphanIds.value.add(dir.id);
  try {
    await ConsistencyService.resolveOrphanDirectory(dir.id);
    orphanDirectories.value = orphanDirectories.value.filter(
      (d) => d.id !== dir.id,
    );
    snackbarText.value = "Directory deleted successfully";
    snackbar.value = true;
  } catch {
    snackbarText.value = "Failed to delete directory";
    snackbar.value = true;
  } finally {
    resolvingOrphanIds.value.delete(dir.id);
  }
};

const bulkResolveOrphans = async () => {
  resolvingAllOrphans.value = true;
  try {
    const result = await ConsistencyService.resolveAllOrphanDirectories();
    let msg = `Deleted ${result.resolved} directories`;
    if (result.failed > 0) {
      msg += ` (${result.failed} failed)`;
    }
    snackbarText.value = msg;
    snackbar.value = true;
  } catch {
    snackbarText.value = "Failed to bulk delete orphaned directories";
    snackbar.value = true;
  } finally {
    resolvingAllOrphans.value = false;
    await loadOrphanDirectories();
  }
};

onMounted(() => {
  loadIssues();
  loadOrphanDirectories();
});
</script>

<style scoped>
.issue-subtitle {
  white-space: normal !important;
  -webkit-line-clamp: unset !important;
  overflow: visible !important;
}

.issue-item :deep(.v-list-item-subtitle) {
  white-space: normal !important;
  -webkit-line-clamp: unset !important;
  overflow: visible !important;
}
</style>

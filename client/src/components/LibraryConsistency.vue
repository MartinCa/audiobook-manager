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
      </v-col>
    </v-row>
    <v-row v-if="checking">
      <v-col cols="12">
        <v-progress-linear
          class="mt-3"
          :model-value="
            checkTotalBooks > 0
              ? (checkBooksChecked / checkTotalBooks) * 100
              : 0
          "
          color="primary"
          height="20"
          striped
        >
          <template v-slot:default>
            {{ checkBooksChecked }} / {{ checkTotalBooks }}
          </template>
        </v-progress-linear>
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
              <div class="d-flex justify-end mb-2">
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
              <v-list density="compact">
                <v-list-item
                  v-for="issue in group.visibleIssues"
                  :key="issue.id"
                  class="issue-item"
                >
                  <template v-slot:prepend>
                    <v-icon :icon="getIssueIcon(issue.issueType)" />
                  </template>
                  <v-list-item-title class="text-wrap">
                    {{ issue.authors.join(", ") }} &mdash;
                    {{ issue.bookName }}
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
    <v-row v-else-if="!checking && !checkComplete">
      <v-col cols="12">
        <div class="text-center mt-5">
          Run a consistency check to find issues in your library.
        </div>
      </v-col>
    </v-row>

    <v-dialog
      v-model="confirmDialog"
      max-width="500"
    >
      <v-card>
        <v-card-title>Confirm Resolution</v-card-title>
        <v-card-text>
          <template v-if="pendingBulkType === 'MissingMediaFile'">
            This will remove
            <strong>all {{ pendingBulkCount }} audiobooks</strong> with missing
            media files from the database and clean up empty directories. This
            action cannot be undone.
          </template>
          <template v-else-if="pendingBulkType">
            This will resolve all
            <strong>{{ pendingBulkCount }}</strong>
            {{ getIssueTypeLabel(pendingBulkType) }} issues. Continue?
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
            {{ pendingBulkType ? "Resolve All" : "Remove" }}
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
import DiffDisplay from "./DiffDisplay.vue";
import { useSignalR, HubEventToken } from "@quangdao/vue-signalr";
import { ConsistencyCheckProgress } from "../signalr/ConsistencyCheckProgress";
import { ConsistencyCheckComplete } from "../signalr/ConsistencyCheckComplete";

const PAGE_SIZE = 50;

const ConsistencyCheckProgressToken: HubEventToken<ConsistencyCheckProgress> =
  "ConsistencyCheckProgress";
const ConsistencyCheckCompleteToken: HubEventToken<ConsistencyCheckComplete> =
  "ConsistencyCheckComplete";

const signalR = useSignalR();

const issues: Ref<ConsistencyIssue[]> = ref([]);
const checking: Ref<boolean> = ref(false);
const checkMessage: Ref<string> = ref("");
const checkBooksChecked: Ref<number> = ref(0);
const checkTotalBooks: Ref<number> = ref(0);
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
const snackbar: Ref<boolean> = ref(false);
const snackbarText: Ref<string> = ref("");
const displayCounts: Record<string, number> = reactive({});

interface TypeGroup {
  issueType: string;
  issues: ConsistencyIssue[];
  displayCount: number;
  visibleIssues: ConsistencyIssue[];
}

const groupedByType = computed((): TypeGroup[] => {
  const groups = new Map<string, ConsistencyIssue[]>();
  for (const issue of issues.value) {
    if (!groups.has(issue.issueType)) {
      groups.set(issue.issueType, []);
    }
    groups.get(issue.issueType)!.push(issue);
  }
  return Array.from(groups.entries()).map(([issueType, typeIssues]) => {
    const displayCount = displayCounts[issueType] || PAGE_SIZE;
    return {
      issueType,
      issues: typeIssues,
      displayCount,
      visibleIssues: typeIssues.slice(0, displayCount),
    };
  });
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
    default:
      return issueType;
  }
};

signalR.on(ConsistencyCheckProgressToken, (arg) => {
  checkMessage.value = arg.message;
  checkBooksChecked.value = arg.booksChecked;
  checkTotalBooks.value = arg.totalBooks;
  checkIssuesFound.value = arg.issuesFound;
});

signalR.on(ConsistencyCheckCompleteToken, (arg) => {
  checking.value = false;
  checkComplete.value = true;
  completeTotalBooks.value = arg.totalBooksChecked;
  completeTotalIssues.value = arg.totalIssuesFound;
  loadIssues();
});

const startCheck = async () => {
  checking.value = true;
  checkComplete.value = false;
  checkBooksChecked.value = 0;
  checkTotalBooks.value = 0;
  checkIssuesFound.value = 0;
  checkMessage.value = "";
  issues.value = [];
  await ConsistencyService.startCheck();
};

const loadIssues = async () => {
  issues.value = await ConsistencyService.getIssues();
};

const getIssueIcon = (issueType: string): string => {
  switch (issueType) {
    case "MissingMediaFile":
      return "mdi-file-remove";
    case "WrongFilePath":
      return "mdi-swap-horizontal";
    case "MissingDescTxt":
    case "IncorrectDescTxt":
    case "MissingReaderTxt":
    case "IncorrectReaderTxt":
      return "mdi-text-box-remove";
    case "MissingCoverFile":
      return "mdi-image-remove";
    default:
      return "mdi-alert";
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
  confirmDialog.value = true;
};

const cancelConfirm = () => {
  confirmDialog.value = false;
  pendingResolveIssue.value = null;
  pendingBulkType.value = null;
};

const confirmResolve = () => {
  if (pendingBulkType.value) {
    bulkResolve(pendingBulkType.value);
  } else if (pendingResolveIssue.value) {
    resolveIssue(pendingResolveIssue.value);
  }
  confirmDialog.value = false;
  pendingResolveIssue.value = null;
  pendingBulkType.value = null;
};

const bulkResolve = async (issueType: string) => {
  resolvingTypes.value.add(issueType);
  try {
    const result = await ConsistencyService.resolveByType(issueType);
    issues.value = issues.value.filter((i) => {
      if (issueType === "MissingMediaFile" || issueType === "WrongFilePath") {
        const resolvedAudiobookIds = new Set(
          issues.value
            .filter((x) => x.issueType === issueType)
            .map((x) => x.audiobookId),
        );
        return !resolvedAudiobookIds.has(i.audiobookId);
      }
      if (
        issueType === "MissingDescTxt" ||
        issueType === "IncorrectDescTxt" ||
        issueType === "MissingReaderTxt" ||
        issueType === "IncorrectReaderTxt"
      ) {
        const metadataTypes = [
          "MissingDescTxt",
          "IncorrectDescTxt",
          "MissingReaderTxt",
          "IncorrectReaderTxt",
        ];
        const resolvedAudiobookIds = new Set(
          issues.value
            .filter((x) => x.issueType === issueType)
            .map((x) => x.audiobookId),
        );
        if (resolvedAudiobookIds.has(i.audiobookId)) {
          return !metadataTypes.includes(i.issueType);
        }
        return true;
      }
      return i.issueType !== issueType;
    });
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
        issue.issueType === "IncorrectReaderTxt"
      ) {
        return !(
          i.audiobookId === issue.audiobookId &&
          (i.issueType === "MissingDescTxt" ||
            i.issueType === "IncorrectDescTxt" ||
            i.issueType === "MissingReaderTxt" ||
            i.issueType === "IncorrectReaderTxt")
        );
      }
      return i.id !== issue.id;
    });
    snackbarText.value = "Issue resolved successfully";
    snackbar.value = true;
  } catch {
    snackbarText.value = "Failed to resolve issue";
    snackbar.value = true;
  } finally {
    resolvingIds.value.delete(issue.id);
  }
};

onMounted(() => {
  loadIssues();
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

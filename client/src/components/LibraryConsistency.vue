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
            v-for="group in groupedIssues"
            :key="group.audiobookId"
          >
            <v-expansion-panel-title>
              <v-row align="center">
                <v-col>
                  {{ group.authors.join(", ") }} &mdash; {{ group.bookName }}
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
              <v-list density="compact">
                <v-list-item
                  v-for="issue in group.issues"
                  :key="issue.id"
                >
                  <template v-slot:prepend>
                    <v-icon :icon="getIssueIcon(issue.issueType)" />
                  </template>
                  <v-list-item-title>{{ issue.description }}</v-list-item-title>
                  <v-list-item-subtitle
                    v-if="issue.expectedValue || issue.actualValue"
                  >
                    <span v-if="issue.expectedValue"
                      >Expected: {{ truncate(issue.expectedValue, 120) }}</span
                    >
                    <span v-if="issue.expectedValue && issue.actualValue">
                      |
                    </span>
                    <span v-if="issue.actualValue"
                      >Actual: {{ truncate(issue.actualValue, 120) }}</span
                    >
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
          This will remove the audiobook from the database and clean up empty
          directories. This action cannot be undone.
        </v-card-text>
        <v-card-actions>
          <v-spacer />
          <v-btn @click="confirmDialog = false">Cancel</v-btn>
          <v-btn
            color="error"
            @click="confirmResolve()"
          >
            Remove
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
import { computed, Ref, ref, onMounted } from "vue";
import ConsistencyService from "../services/ConsistencyService";
import ConsistencyIssue from "../types/ConsistencyIssue";
import { useSignalR, HubEventToken } from "@quangdao/vue-signalr";
import { ConsistencyCheckProgress } from "../signalr/ConsistencyCheckProgress";
import { ConsistencyCheckComplete } from "../signalr/ConsistencyCheckComplete";

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
const confirmDialog: Ref<boolean> = ref(false);
const pendingResolveIssue: Ref<ConsistencyIssue | null> = ref(null);
const snackbar: Ref<boolean> = ref(false);
const snackbarText: Ref<string> = ref("");

interface IssueGroup {
  audiobookId: number;
  bookName: string;
  authors: string[];
  issues: ConsistencyIssue[];
}

const groupedIssues = computed((): IssueGroup[] => {
  const groups = new Map<number, IssueGroup>();
  for (const issue of issues.value) {
    if (!groups.has(issue.audiobookId)) {
      groups.set(issue.audiobookId, {
        audiobookId: issue.audiobookId,
        bookName: issue.bookName,
        authors: issue.authors,
        issues: [],
      });
    }
    groups.get(issue.audiobookId)!.issues.push(issue);
  }
  return Array.from(groups.values());
});

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

const truncate = (text: string, maxLength: number): string => {
  if (text.length <= maxLength) return text;
  return text.substring(0, maxLength) + "...";
};

const onResolveClick = (issue: ConsistencyIssue) => {
  if (issue.issueType === "MissingMediaFile") {
    pendingResolveIssue.value = issue;
    confirmDialog.value = true;
  } else {
    resolveIssue(issue);
  }
};

const confirmResolve = () => {
  if (pendingResolveIssue.value) {
    resolveIssue(pendingResolveIssue.value);
  }
  confirmDialog.value = false;
  pendingResolveIssue.value = null;
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

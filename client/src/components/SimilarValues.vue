<template>
  <v-container>
    <v-row class="text-center">
      <v-col class="mb-5">
        <h2 class="headline font-weight-bold mb-5">Similar Values</h2>
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
          :disabled="loading"
          :loading="loading"
          @click="loadGroups()"
          prepend-icon="mdi-refresh"
        >
          Refresh
        </v-btn>
        <div class="text-caption text-medium-emphasis mt-2">
          Finds authors and series that are likely the same value recorded with
          small textual differences (initials, punctuation, "&amp;" vs "and",
          stray whitespace, typos), and lets you align them all to one value.
        </div>
      </v-col>
    </v-row>

    <v-row v-if="aligning">
      <v-col cols="12">
        <OperationProgressBar
          class="mt-3"
          :processed="alignProcessed"
          :total="alignTotal"
        />
        <div class="text-caption">
          Succeeded: {{ alignSucceeded }}, Failed: {{ alignFailed }}
        </div>
      </v-col>
    </v-row>

    <v-row>
      <v-col cols="12">
        <div class="d-flex align-center mb-3">
          <v-btn
            icon
            variant="text"
            density="comfortable"
            :aria-label="
              authorsCollapsed
                ? 'Expand similar authors'
                : 'Collapse similar authors'
            "
            @click="authorsCollapsed = !authorsCollapsed"
          >
            <v-icon>{{
              authorsCollapsed ? "mdi-chevron-right" : "mdi-chevron-down"
            }}</v-icon>
          </v-btn>
          <h3 class="text-h6">Similar Authors ({{ authorGroups.length }})</h3>
        </div>
        <div
          v-if="!loading && authorGroups.length === 0"
          class="text-caption text-medium-emphasis mb-5"
        >
          No similar author groups found.
        </div>
        <div v-show="!authorsCollapsed">
          <v-card
            v-for="(group, index) in authorGroups"
            :key="`author-${index}`"
            class="mb-4"
            variant="outlined"
          >
            <v-card-text>
              <v-radio-group
                v-model="authorSelections[index].target"
                hide-details
              >
                <v-radio
                  v-for="candidate in group.candidates"
                  :key="candidate.value"
                  :value="candidate.value"
                >
                  <template v-slot:label>
                    {{ candidate.value }}
                    <v-chip
                      size="small"
                      class="ml-2"
                      >{{ candidate.bookCount }} book{{
                        candidate.bookCount === 1 ? "" : "s"
                      }}</v-chip
                    >
                    <v-btn
                      size="x-small"
                      variant="text"
                      class="ml-1"
                      @click.stop.prevent="
                        toggleCandidateBooks('author', index, candidate.value)
                      "
                    >
                      {{
                        isCandidateExpanded("author", index, candidate.value)
                          ? "hide books"
                          : "show books"
                      }}
                    </v-btn>
                  </template>
                </v-radio>
              </v-radio-group>
              <div
                v-for="candidate in group.candidates"
                :key="`${candidate.value}-books`"
              >
                <v-expand-transition>
                  <ul
                    v-if="isCandidateExpanded('author', index, candidate.value)"
                    class="text-caption text-medium-emphasis mb-2"
                  >
                    <li
                      v-for="book in candidate.books"
                      :key="book.id"
                    >
                      {{ book.bookName }}
                    </li>
                  </ul>
                </v-expand-transition>
              </div>
              <v-text-field
                v-model="authorSelections[index].customValue"
                label="Or enter a different value"
                density="compact"
                hide-details
                class="mt-2"
                @update:model-value="
                  onCustomValueEntered(authorSelections[index])
                "
              />
            </v-card-text>
            <v-card-actions>
              <v-spacer />
              <v-btn
                color="primary"
                :disabled="!authorSelections[index].target || aligning"
                @click="onApplyClick('author', group, authorSelections[index])"
              >
                Apply
              </v-btn>
            </v-card-actions>
          </v-card>
        </div>
      </v-col>
    </v-row>

    <v-row>
      <v-col cols="12">
        <div class="d-flex align-center mb-3">
          <v-btn
            icon
            variant="text"
            density="comfortable"
            :aria-label="
              seriesCollapsed
                ? 'Expand similar series'
                : 'Collapse similar series'
            "
            @click="seriesCollapsed = !seriesCollapsed"
          >
            <v-icon>{{
              seriesCollapsed ? "mdi-chevron-right" : "mdi-chevron-down"
            }}</v-icon>
          </v-btn>
          <h3 class="text-h6">Similar Series ({{ seriesGroups.length }})</h3>
        </div>
        <div
          v-if="!loading && seriesGroups.length === 0"
          class="text-caption text-medium-emphasis mb-5"
        >
          No similar series groups found.
        </div>
        <div v-show="!seriesCollapsed">
          <v-card
            v-for="(group, index) in seriesGroups"
            :key="`series-${index}`"
            class="mb-4"
            variant="outlined"
          >
            <v-card-text>
              <v-radio-group
                v-model="seriesSelections[index].target"
                hide-details
              >
                <v-radio
                  v-for="candidate in group.candidates"
                  :key="candidate.value"
                  :value="candidate.value"
                >
                  <template v-slot:label>
                    {{ candidate.value }}
                    <v-chip
                      size="small"
                      class="ml-2"
                      >{{ candidate.bookCount }} book{{
                        candidate.bookCount === 1 ? "" : "s"
                      }}</v-chip
                    >
                    <v-btn
                      size="x-small"
                      variant="text"
                      class="ml-1"
                      @click.stop.prevent="
                        toggleCandidateBooks('series', index, candidate.value)
                      "
                    >
                      {{
                        isCandidateExpanded("series", index, candidate.value)
                          ? "hide books"
                          : "show books"
                      }}
                    </v-btn>
                  </template>
                </v-radio>
              </v-radio-group>
              <div
                v-for="candidate in group.candidates"
                :key="`${candidate.value}-books`"
              >
                <v-expand-transition>
                  <ul
                    v-if="isCandidateExpanded('series', index, candidate.value)"
                    class="text-caption text-medium-emphasis mb-2"
                  >
                    <li
                      v-for="book in candidate.books"
                      :key="book.id"
                    >
                      {{ book.bookName }}
                    </li>
                  </ul>
                </v-expand-transition>
              </div>
              <v-text-field
                v-model="seriesSelections[index].customValue"
                label="Or enter a different value"
                density="compact"
                hide-details
                class="mt-2"
                @update:model-value="
                  onCustomValueEntered(seriesSelections[index])
                "
              />
            </v-card-text>
            <v-card-actions>
              <v-spacer />
              <v-btn
                color="primary"
                :disabled="!seriesSelections[index].target || aligning"
                @click="onApplyClick('series', group, seriesSelections[index])"
              >
                Apply
              </v-btn>
            </v-card-actions>
          </v-card>
        </div>
      </v-col>
    </v-row>

    <v-dialog
      v-model="confirmDialog"
      max-width="500"
    >
      <v-card>
        <v-card-title>Confirm Alignment</v-card-title>
        <v-card-text v-if="pending">
          This will update
          <strong
            >{{ pendingBookCount }} book{{
              pendingBookCount === 1 ? "" : "s"
            }}</strong
          >
          to use
          <strong>"{{ pending.selection.target }}"</strong>
          as the {{ pending.valueType }}. Each affected book's m4b tags will be
          rewritten and the file relocated if needed. This action cannot be
          undone.
        </v-card-text>
        <v-card-actions>
          <v-spacer />
          <v-btn @click="cancelConfirm()">Cancel</v-btn>
          <v-btn
            color="primary"
            @click="confirmApply()"
          >
            Apply
          </v-btn>
        </v-card-actions>
      </v-card>
    </v-dialog>

    <v-snackbar
      v-model="snackbar"
      :timeout="4000"
    >
      {{ snackbarText }}
    </v-snackbar>
  </v-container>
</template>

<script setup lang="ts">
import { onMounted, Ref, ref } from "vue";
import SimilarValueService from "../services/SimilarValueService";
import { SimilarValueGroup } from "../types/SimilarValue";
import OperationProgressBar from "./OperationProgressBar.vue";
import { HubEventToken } from "@/signalr/hub";
import { useOperationProgress } from "../composables/useOperationProgress";
import { SimilarValueAlignProgress } from "../signalr/SimilarValueAlignProgress";
import { SimilarValueAlignComplete } from "../signalr/SimilarValueAlignComplete";

interface Selection {
  target: string | null;
  customValue: string;
}

const SimilarValueAlignProgressToken: HubEventToken<SimilarValueAlignProgress> =
  "SimilarValueAlignProgress";
const SimilarValueAlignCompleteToken: HubEventToken<SimilarValueAlignComplete> =
  "SimilarValueAlignComplete";

const loading: Ref<boolean> = ref(false);
const authorGroups: Ref<SimilarValueGroup[]> = ref([]);
const seriesGroups: Ref<SimilarValueGroup[]> = ref([]);
const authorSelections: Ref<Selection[]> = ref([]);
const seriesSelections: Ref<Selection[]> = ref([]);

const authorsCollapsed: Ref<boolean> = ref(false);
const seriesCollapsed: Ref<boolean> = ref(false);

const expandedCandidates: Ref<Set<string>> = ref(new Set());

const candidateKey = (
  valueType: "author" | "series",
  groupIndex: number,
  candidateValue: string,
): string => `${valueType}-${groupIndex}-${candidateValue}`;

const isCandidateExpanded = (
  valueType: "author" | "series",
  groupIndex: number,
  candidateValue: string,
): boolean =>
  expandedCandidates.value.has(
    candidateKey(valueType, groupIndex, candidateValue),
  );

const toggleCandidateBooks = (
  valueType: "author" | "series",
  groupIndex: number,
  candidateValue: string,
) => {
  const key = candidateKey(valueType, groupIndex, candidateValue);
  const next = new Set(expandedCandidates.value);
  if (next.has(key)) {
    next.delete(key);
  } else {
    next.add(key);
  }
  expandedCandidates.value = next;
};

const alignSucceeded: Ref<number> = ref(0);
const alignFailed: Ref<number> = ref(0);

const confirmDialog: Ref<boolean> = ref(false);
const pending: Ref<{
  valueType: "author" | "series";
  group: SimilarValueGroup;
  selection: Selection;
} | null> = ref(null);
const pendingBookCount: Ref<number> = ref(0);

const snackbar: Ref<boolean> = ref(false);
const snackbarText: Ref<string> = ref("");

const makeSelections = (groups: SimilarValueGroup[]): Selection[] =>
  groups.map((group) => ({
    target: group.candidates[0]?.value ?? null,
    customValue: "",
  }));

const onCustomValueEntered = (selection: Selection) => {
  if (selection.customValue) {
    selection.target = selection.customValue;
  }
};

const onApplyClick = (
  valueType: "author" | "series",
  group: SimilarValueGroup,
  selection: Selection,
) => {
  if (!selection.target) return;

  pending.value = { valueType, group, selection };
  const sourceValues = group.candidates.map((c) => c.value);
  pendingBookCount.value = group.candidates
    .filter((c) => sourceValues.includes(c.value))
    .reduce((sum, c) => sum + c.bookCount, 0);
  confirmDialog.value = true;
};

const cancelConfirm = () => {
  confirmDialog.value = false;
  pending.value = null;
};

const {
  isRunning: aligning,
  processed: alignProcessed,
  total: alignTotal,
  start: startAligning,
} = useOperationProgress<SimilarValueAlignProgress, SimilarValueAlignComplete>({
  key: "similar-value-align",
  progressToken: SimilarValueAlignProgressToken,
  completeToken: SimilarValueAlignCompleteToken,
  getProcessed: (arg) => arg.processed,
  getTotal: (arg) => arg.total,
  onProgress: (arg) => {
    alignSucceeded.value = arg.succeeded;
    alignFailed.value = arg.failed;
  },
  onComplete: (arg) => {
    let msg = `Alignment complete: ${arg.totalSucceeded} of ${arg.totalProcessed} books updated`;
    if (arg.totalFailed > 0) {
      msg += ` (${arg.totalFailed} failed)`;
    }
    snackbarText.value = msg;
    snackbar.value = true;
    SimilarValueService.invalidateNameCaches();
    loadGroups();
  },
});

const confirmApply = async () => {
  const current = pending.value;
  confirmDialog.value = false;
  pending.value = null;
  if (!current || !current.selection.target) return;

  const sourceValues = current.group.candidates.map((c) => c.value);

  startAligning();
  alignSucceeded.value = 0;
  alignFailed.value = 0;

  try {
    await SimilarValueService.startAlign(
      current.valueType,
      sourceValues,
      current.selection.target,
    );
  } catch (e: any) {
    aligning.value = false;
    snackbarText.value = `Failed to start alignment: ${e?.response?.data ?? e.message}`;
    snackbar.value = true;
  }
};

const loadGroups = async () => {
  loading.value = true;
  try {
    const [authors, series] = await Promise.all([
      SimilarValueService.getSimilarAuthors(),
      SimilarValueService.getSimilarSeries(),
    ]);
    authorGroups.value = authors;
    seriesGroups.value = series;
    authorSelections.value = makeSelections(authors);
    seriesSelections.value = makeSelections(series);
  } catch {
    snackbarText.value = "Failed to load similar value groups";
    snackbar.value = true;
  } finally {
    loading.value = false;
  }
};

onMounted(() => {
  loadGroups();
});
</script>

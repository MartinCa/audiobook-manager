<template>
  <v-container>
    <v-row class="text-center">
      <v-col class="mb-5">
        <h2 class="headline font-weight-bold mb-5">Missing Tags</h2>
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
          :disabled="loading || selectedFields.length === 0"
          :loading="loading"
          @click="loadResults()"
          prepend-icon="mdi-refresh"
        >
          Refresh
        </v-btn>
        <div class="text-caption text-medium-emphasis mt-2">
          Finds audiobooks missing the selected fields in the database. Series
          and Series Part are optional by design and aren't checked by default.
        </div>
      </v-col>
    </v-row>

    <v-row>
      <v-col cols="12">
        <v-card
          variant="outlined"
          class="mb-4"
        >
          <v-card-text>
            <div class="d-flex align-center flex-wrap ga-3">
              <v-btn
                :disabled="backfillRunning"
                :loading="backfillRunning"
                @click="startBackfill()"
                prepend-icon="mdi-translate"
              >
                Backfill language from file tags
              </v-btn>
              <div class="text-caption text-medium-emphasis flex-grow-1">
                Reads the language already embedded in each m4b for books that
                have none recorded, and stores it as a language code. Books
                whose file has no usable language tag are left empty and stay in
                the list below. The m4b files themselves aren't rewritten
                &mdash; backfilled books will show up as tag mismatches in
                <router-link to="/library/consistency">Consistency</router-link
                >, where a bulk resolve updates the tags and metadata.opf.
              </div>
            </div>
            <OperationProgressBar
              v-if="backfillRunning"
              class="mt-3"
              :processed="backfillProcessed"
              :total="backfillTotal"
            />
          </v-card-text>
        </v-card>
      </v-col>
    </v-row>

    <v-row>
      <v-col cols="12">
        <v-card
          variant="outlined"
          class="mb-4"
        >
          <v-card-text>
            <div class="d-flex align-center mb-2">
              <span class="text-subtitle-2 mr-3">Fields to check</span>
              <v-btn
                size="x-small"
                variant="text"
                @click="selectCriticalOnly()"
              >
                Critical only
              </v-btn>
              <v-btn
                size="x-small"
                variant="text"
                @click="selectAll()"
              >
                Select all
              </v-btn>
              <v-btn
                size="x-small"
                variant="text"
                @click="clearSelection()"
              >
                Clear
              </v-btn>
            </div>
            <v-chip-group
              v-model="selectedFields"
              multiple
              column
            >
              <v-chip
                v-for="field in fields"
                :key="field.key"
                :value="field.key"
                filter
              >
                {{ field.label }}
                <v-icon
                  v-if="field.isCriticalByDefault"
                  size="x-small"
                  class="ml-1"
                  title="Critical for path generation"
                >
                  mdi-star
                </v-icon>
              </v-chip>
            </v-chip-group>
          </v-card-text>
        </v-card>
      </v-col>
    </v-row>

    <v-row>
      <v-col cols="12">
        <h3 class="text-h6 mb-3">Results ({{ results.length }})</h3>
        <div
          v-if="!loading && selectedFields.length === 0"
          class="text-caption text-medium-emphasis mb-5"
        >
          Select at least one field to check.
        </div>
        <div
          v-else-if="!loading && results.length === 0"
          class="text-caption text-medium-emphasis mb-5"
        >
          No audiobooks are missing the selected fields.
        </div>
        <v-card
          v-for="result in results"
          :key="result.audiobookId"
          class="mb-3"
          variant="outlined"
        >
          <v-card-text class="d-flex align-center flex-wrap">
            <div class="flex-grow-1">
              <div class="text-subtitle-1">
                {{ result.bookName || "(untitled)" }}
                <span
                  v-if="result.authors.length"
                  class="text-caption text-medium-emphasis"
                >
                  — {{ result.authors.join(", ") }}
                </span>
              </div>
              <div class="mt-1">
                <v-chip
                  v-for="key in result.missingFields"
                  :key="key"
                  size="small"
                  color="warning"
                  variant="tonal"
                  class="mr-1 mb-1"
                >
                  {{ fieldLabel(key) }}
                </v-chip>
              </div>
            </div>
            <v-btn
              :to="`/library/book/${result.audiobookId}`"
              color="primary"
              variant="tonal"
              prepend-icon="mdi-pencil"
            >
              Edit
            </v-btn>
          </v-card-text>
        </v-card>
      </v-col>
    </v-row>

    <v-snackbar
      v-model="snackbar"
      :timeout="4000"
    >
      {{ snackbarText }}
    </v-snackbar>
  </v-container>
</template>

<script setup lang="ts">
import { debounce } from "lodash";
import { nextTick, onMounted, onUnmounted, Ref, ref, watch } from "vue";
import MissingTagService from "../services/MissingTagService";
import OperationsService from "../services/OperationsService";
import OperationProgressBar from "./OperationProgressBar.vue";
import { AudiobookMissingTags, MissingTagField } from "../types/MissingTag";
import { useMissingTagSelection } from "../composables/useMissingTagSelection";

const loading: Ref<boolean> = ref(false);
const fields: Ref<MissingTagField[]> = ref([]);
const results: Ref<AudiobookMissingTags[]> = ref([]);
const selectedFields = useMissingTagSelection(fields);

const snackbar: Ref<boolean> = ref(false);
const snackbarText: Ref<string> = ref("");

const BACKFILL_OPERATION_KEY = "language-backfill";
const BACKFILL_POLL_INTERVAL_MS = 1000;

const backfillRunning: Ref<boolean> = ref(false);
const backfillProcessed: Ref<number> = ref(0);
const backfillTotal: Ref<number> = ref(0);
let backfillPollTimer: ReturnType<typeof setInterval> | null = null;

const stopBackfillPolling = () => {
  if (backfillPollTimer !== null) {
    clearInterval(backfillPollTimer);
    backfillPollTimer = null;
  }
};

/**
 * The backfill publishes no SignalR event (it's a one-shot maintenance pass), so its progress is
 * read from the operation status registry the runner already maintains.
 */
const pollBackfillStatus = async () => {
  try {
    const status = await OperationsService.getStatus(BACKFILL_OPERATION_KEY);
    backfillProcessed.value = status.processed;
    backfillTotal.value = status.total;

    if (!status.isRunning) {
      stopBackfillPolling();
      backfillRunning.value = false;
      snackbarText.value = "Language backfill finished";
      snackbar.value = true;
      await loadResults();
    }
  } catch {
    // A failed poll is not a failed backfill; stop following it rather than spinning forever.
    stopBackfillPolling();
    backfillRunning.value = false;
    snackbarText.value =
      "Lost track of the language backfill — refresh to see the result";
    snackbar.value = true;
  }
};

const startBackfill = async () => {
  backfillRunning.value = true;
  backfillProcessed.value = 0;
  backfillTotal.value = 0;

  try {
    await MissingTagService.startLanguageBackfill();
  } catch {
    backfillRunning.value = false;
    snackbarText.value = "Failed to start the language backfill";
    snackbar.value = true;
    return;
  }

  stopBackfillPolling();
  backfillPollTimer = setInterval(
    pollBackfillStatus,
    BACKFILL_POLL_INTERVAL_MS,
  );
};

const fieldLabel = (key: string): string =>
  fields.value.find((f) => f.key === key)?.label ?? key;

const selectCriticalOnly = () => {
  selectedFields.value = fields.value
    .filter((f) => f.isCriticalByDefault)
    .map((f) => f.key);
};

const selectAll = () => {
  selectedFields.value = fields.value.map((f) => f.key);
};

const clearSelection = () => {
  selectedFields.value = [];
};

// Only the newest request may write to the list. Ticking chips debounces a scan of the whole
// library, so a slower earlier request routinely lands after a faster later one - and rendering
// its rows shows results for a field selection the user has already changed.
let loadRequestId = 0;

const loadResults = async () => {
  const requestId = ++loadRequestId;

  if (selectedFields.value.length === 0) {
    results.value = [];
    loading.value = false;
    return;
  }

  loading.value = true;
  try {
    const loaded = await MissingTagService.getAudiobooksMissingTags(
      selectedFields.value,
    );
    if (requestId !== loadRequestId) return;
    results.value = loaded;
  } catch {
    if (requestId !== loadRequestId) return;
    snackbarText.value = "Failed to load missing tags";
    snackbar.value = true;
  } finally {
    // A superseded request must not clear the spinner the newer one is still showing.
    if (requestId === loadRequestId) {
      loading.value = false;
    }
  }
};

const debouncedLoadResults = debounce(loadResults, 500);

// Without this the debounced scan fires after the component is gone, mutating dead refs and
// issuing a request nobody reads.
onUnmounted(() => {
  debouncedLoadResults.cancel();
  // Same pairing rule as the debounced load: left running, the poll keeps issuing requests and
  // mutating refs after the component is gone.
  stopBackfillPolling();
});

watch(
  selectedFields,
  () => {
    debouncedLoadResults();
  },
  { deep: true },
);

/**
 * Pick a backfill that is already in flight back up — it outlives this page, so a reload (or a
 * run started from another tab) must show its progress rather than offering to start a second.
 */
const resumeRunningBackfill = async () => {
  try {
    const status = await OperationsService.getStatus(BACKFILL_OPERATION_KEY);
    if (!status.isRunning) {
      return;
    }
    backfillRunning.value = true;
    backfillProcessed.value = status.processed;
    backfillTotal.value = status.total;
    backfillPollTimer = setInterval(
      pollBackfillStatus,
      BACKFILL_POLL_INTERVAL_MS,
    );
  } catch {
    // Non-critical: the button is simply available again.
  }
};

onMounted(async () => {
  loading.value = true;
  void resumeRunningBackfill();
  try {
    fields.value = await MissingTagService.getFields();
  } catch {
    snackbarText.value = "Failed to load taggable fields";
    snackbar.value = true;
    loading.value = false;
    return;
  }
  // Setting fields.value triggers useMissingTagSelection's watcher (which restores/defaults
  // selectedFields), which in turn triggers the watch above to run the initial scan.
  await nextTick();
  if (selectedFields.value.length === 0) {
    loading.value = false;
  }
});
</script>

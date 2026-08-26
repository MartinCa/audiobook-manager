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
import { nextTick, onMounted, Ref, ref, watch } from "vue";
import MissingTagService from "../services/MissingTagService";
import { AudiobookMissingTags, MissingTagField } from "../types/MissingTag";
import { useMissingTagSelection } from "../composables/useMissingTagSelection";

const loading: Ref<boolean> = ref(false);
const fields: Ref<MissingTagField[]> = ref([]);
const results: Ref<AudiobookMissingTags[]> = ref([]);
const selectedFields = useMissingTagSelection(fields);

const snackbar: Ref<boolean> = ref(false);
const snackbarText: Ref<string> = ref("");

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

const loadResults = async () => {
  if (selectedFields.value.length === 0) {
    results.value = [];
    return;
  }

  loading.value = true;
  try {
    results.value = await MissingTagService.getAudiobooksMissingTags(
      selectedFields.value,
    );
  } catch {
    snackbarText.value = "Failed to load missing tags";
    snackbar.value = true;
  } finally {
    loading.value = false;
  }
};

const debouncedLoadResults = debounce(loadResults, 500);

watch(
  selectedFields,
  () => {
    debouncedLoadResults();
  },
  { deep: true },
);

onMounted(async () => {
  loading.value = true;
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

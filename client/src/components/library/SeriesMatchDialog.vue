<template>
  <v-dialog
    :model-value="modelValue"
    max-width="900"
    scrollable
    @update:model-value="$emit('update:modelValue', $event)"
  >
    <v-card>
      <v-card-title>Bulk match series</v-card-title>
      <v-card-subtitle class="text-wrap">
        Each unmatched series is looked up at every metadata source that
        supports series lookups. Only series whose best candidate scores at or
        above the threshold are matched; the rest are skipped.
      </v-card-subtitle>

      <v-card-text>
        <v-row>
          <v-col
            cols="12"
            md="6"
          >
            <v-slider
              v-model="threshold"
              label="Confidence threshold"
              :min="0.5"
              :max="1"
              :step="0.01"
              hide-details
              thumb-label
            >
              <template v-slot:append>
                <span class="text-body-2">
                  {{ Math.round(threshold * 100) }}%
                </span>
              </template>
            </v-slider>
          </v-col>
          <v-col
            cols="12"
            md="6"
            class="d-flex align-center ga-2"
          >
            <v-btn
              :disabled="loadingSuggestions || matching || series.length === 0"
              :loading="loadingSuggestions"
              prepend-icon="mdi-magnify"
              @click="loadSuggestions()"
            >
              Preview suggestions
            </v-btn>
            <v-btn
              variant="text"
              :disabled="matching"
              @click="toggleAll()"
            >
              {{ allSelected ? "Deselect all" : "Select all" }}
            </v-btn>
          </v-col>
        </v-row>

        <OperationProgressBar
          v-if="matching"
          class="my-3"
          :processed="processed"
          :total="total"
        />
        <div
          v-if="matching"
          class="text-caption mb-2"
        >
          Succeeded: {{ succeeded }}, Failed: {{ failed }}
        </div>

        <div
          v-if="series.length === 0"
          class="text-caption text-medium-emphasis"
        >
          Every series is already matched.
        </div>

        <v-list v-else>
          <v-list-item
            v-for="item in series"
            :key="item.name"
          >
            <template v-slot:prepend>
              <v-checkbox-btn
                :model-value="selected.includes(item.name)"
                :disabled="matching"
                @update:model-value="toggle(item.name)"
              />
            </template>
            <v-list-item-title>{{ item.name }}</v-list-item-title>
            <v-list-item-subtitle>
              <span>{{ item.authors.join(", ") || "Unknown author" }}</span>
              <span> &middot; {{ item.ownedBookCount }} owned</span>
              <template v-if="suggestions[item.name] !== undefined">
                <span v-if="suggestions[item.name] === null">
                  &middot; no candidates found
                </span>
                <span v-else>
                  &middot; best:
                  <strong>{{ suggestions[item.name]!.seriesName }}</strong>
                  ({{ suggestions[item.name]!.sourceName }},
                  {{ Math.round(suggestions[item.name]!.confidence * 100) }}%)
                  <v-chip
                    v-if="suggestions[item.name]!.confidence < threshold"
                    size="x-small"
                    class="ml-1"
                  >
                    below threshold
                  </v-chip>
                </span>
              </template>
            </v-list-item-subtitle>
          </v-list-item>
        </v-list>
      </v-card-text>

      <v-card-actions>
        <v-spacer />
        <v-btn
          :disabled="matching"
          @click="close()"
        >
          Close
        </v-btn>
        <v-btn
          color="primary"
          :disabled="matching || selected.length === 0"
          @click="startMatch()"
        >
          Match selected ({{ selected.length }})
        </v-btn>
      </v-card-actions>
    </v-card>

    <v-snackbar
      v-model="snackbar"
      :timeout="4000"
    >
      {{ snackbarText }}
    </v-snackbar>
  </v-dialog>
</template>

<script setup lang="ts">
import { computed, Ref, ref, watch } from "vue";
import SeriesService from "../../services/SeriesService";
import { SeriesMatchCandidate, SeriesOverview } from "../../types/Series";
import OperationProgressBar from "../OperationProgressBar.vue";
import { HubEventToken } from "@/signalr/hub";
import { useOperationProgress } from "../../composables/useOperationProgress";
import { SeriesMatchProgress } from "../../signalr/SeriesMatchProgress";
import { SeriesMatchComplete } from "../../signalr/SeriesMatchComplete";

const props = defineProps<{
  modelValue: boolean;
  series: SeriesOverview[];
}>();

const emit = defineEmits<{
  (e: "update:modelValue", value: boolean): void;
  (e: "matched"): void;
}>();

const SeriesMatchProgressToken: HubEventToken<SeriesMatchProgress> =
  "SeriesMatchProgress";
const SeriesMatchCompleteToken: HubEventToken<SeriesMatchComplete> =
  "SeriesMatchComplete";

const threshold = ref(0.85);
const selected: Ref<string[]> = ref([]);
// null means "looked up, nothing found" - distinct from "not looked up yet" (undefined).
const suggestions: Ref<Record<string, SeriesMatchCandidate | null>> = ref({});
const loadingSuggestions = ref(false);

const succeeded = ref(0);
const failed = ref(0);

const snackbar = ref(false);
const snackbarText = ref("");

const allSelected = computed(
  () =>
    props.series.length > 0 && selected.value.length === props.series.length,
);

watch(
  () => props.modelValue,
  (open) => {
    if (open) {
      selected.value = props.series.map((s) => s.name);
    }
  },
);

const toggle = (name: string) => {
  selected.value = selected.value.includes(name)
    ? selected.value.filter((n) => n !== name)
    : [...selected.value, name];
};

const toggleAll = () => {
  selected.value = allSelected.value ? [] : props.series.map((s) => s.name);
};

const loadSuggestions = async () => {
  loadingSuggestions.value = true;
  try {
    // Sequential on purpose: each lookup hits an external metadata API.
    for (const item of props.series) {
      try {
        const candidates = await SeriesService.getMatchCandidates(item.name);
        suggestions.value = {
          ...suggestions.value,
          [item.name]: candidates.length > 0 ? candidates[0] : null,
        };
      } catch {
        suggestions.value = { ...suggestions.value, [item.name]: null };
      }
    }
  } finally {
    loadingSuggestions.value = false;
  }
};

const {
  isRunning: matching,
  processed,
  total,
  start: startMatching,
} = useOperationProgress<SeriesMatchProgress, SeriesMatchComplete>({
  key: "series-match",
  progressToken: SeriesMatchProgressToken,
  completeToken: SeriesMatchCompleteToken,
  getProcessed: (arg) => arg.processed,
  getTotal: (arg) => arg.total,
  onProgress: (arg) => {
    succeeded.value = arg.succeeded;
    failed.value = arg.failed;
  },
  onComplete: (arg) => {
    let msg = arg.stopReason
      ? `Matching stopped after ${arg.totalProcessed} series: ${arg.stopReason}`
      : `Matching complete: ${arg.totalSucceeded} of ${arg.totalProcessed} series matched`;
    if (arg.totalFailed > 0) {
      msg += ` (${arg.totalFailed} failed)`;
    }
    snackbarText.value = msg;
    snackbar.value = true;
    emit("matched");
  },
});

const startMatch = async () => {
  startMatching();
  succeeded.value = 0;
  failed.value = 0;

  try {
    await SeriesService.startBulkMatch(threshold.value, selected.value);
  } catch (e: any) {
    matching.value = false;
    snackbarText.value = `Failed to start matching: ${e?.response?.data ?? e.message}`;
    snackbar.value = true;
  }
};

const close = () => emit("update:modelValue", false);
</script>

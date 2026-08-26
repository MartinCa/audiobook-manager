<template>
  <v-container>
    <v-row>
      <v-col>
        <v-btn
          variant="text"
          prepend-icon="mdi-arrow-left"
          to="/library"
        >
          Back to Library
        </v-btn>
      </v-col>
    </v-row>
    <v-row>
      <v-col>
        <h2 class="text-h5 mb-1">Series</h2>
        <div class="text-caption text-medium-emphasis">
          Every series in your library. Match a series to a metadata source to
          see which of its books you are missing.
        </div>
      </v-col>
    </v-row>

    <v-row>
      <v-col
        cols="12"
        md="6"
      >
        <v-text-field
          v-model="filter"
          label="Filter series"
          prepend-inner-icon="mdi-magnify"
          clearable
          hide-details
          density="compact"
        />
      </v-col>
      <v-col
        cols="12"
        md="6"
        class="d-flex align-center flex-wrap ga-2"
      >
        <v-btn
          :disabled="loading"
          :loading="loading"
          prepend-icon="mdi-refresh"
          @click="loadSeries()"
        >
          Reload
        </v-btn>
        <v-btn
          :disabled="busy || unmatchedSeries.length === 0"
          prepend-icon="mdi-link-variant"
          @click="matchDialogOpen = true"
        >
          Bulk match ({{ unmatchedSeries.length }})
        </v-btn>
        <v-btn
          :disabled="busy || matchedCount === 0"
          prepend-icon="mdi-cloud-refresh"
          @click="refreshAll()"
        >
          Refresh all series
        </v-btn>
      </v-col>
    </v-row>

    <v-row v-if="refreshing">
      <v-col cols="12">
        <v-progress-linear
          class="mt-3"
          :model-value="
            refreshTotal > 0 ? (refreshProcessed / refreshTotal) * 100 : 0
          "
          color="primary"
          height="20"
          striped
        >
          <template v-slot:default>
            {{ refreshProcessed }} / {{ refreshTotal }}
          </template>
        </v-progress-linear>
        <div class="text-caption">
          Succeeded: {{ refreshSucceeded }}, Failed: {{ refreshFailed }}
        </div>
      </v-col>
    </v-row>

    <v-row>
      <v-col>
        <v-table v-if="filteredSeries.length">
          <thead>
            <tr>
              <th class="text-left">Series</th>
              <th class="text-left">Author</th>
              <th class="text-right">Owned</th>
              <th class="text-right">Missing</th>
              <th class="text-left">Match</th>
            </tr>
          </thead>
          <tbody>
            <tr
              v-for="series in filteredSeries"
              :key="series.name"
              class="cursor-pointer"
              @click="openSeries(series)"
            >
              <td>{{ series.name }}</td>
              <td class="text-medium-emphasis">
                {{ series.authors.join(", ") || "—" }}
              </td>
              <td class="text-right">{{ series.ownedBookCount }}</td>
              <td class="text-right">
                <v-chip
                  v-if="series.isMatched && series.missingBookCount > 0"
                  size="small"
                  color="warning"
                >
                  {{ series.missingBookCount }}
                </v-chip>
                <span v-else-if="series.isMatched">0</span>
                <span
                  v-else
                  class="text-medium-emphasis"
                  >—</span
                >
              </td>
              <td>
                <v-chip
                  v-if="series.isMatched"
                  size="small"
                  color="success"
                  variant="tonal"
                >
                  {{ series.matchedSourceName }}
                  <span
                    v-if="series.matchConfidence != null"
                    class="ml-1"
                  >
                    ({{ Math.round(series.matchConfidence * 100) }}%)
                  </span>
                </v-chip>
                <v-chip
                  v-else
                  size="small"
                  variant="tonal"
                >
                  Unmatched
                </v-chip>
              </td>
            </tr>
          </tbody>
        </v-table>
        <div
          v-else-if="loading"
          class="text-center"
        >
          <v-progress-circular indeterminate />
        </div>
        <div
          v-else
          class="text-center"
        >
          No series found
        </div>
      </v-col>
    </v-row>

    <SeriesMatchDialog
      v-model="matchDialogOpen"
      :series="unmatchedSeries"
      @matched="loadSeries()"
    />

    <v-snackbar
      v-model="snackbar"
      :timeout="4000"
    >
      {{ snackbarText }}
    </v-snackbar>
  </v-container>
</template>

<script setup lang="ts">
import { computed, onMounted, onUnmounted, Ref, ref } from "vue";
import { useRouter } from "vue-router";
import SeriesService from "../../services/SeriesService";
import { SeriesOverview } from "../../types/Series";
import SeriesMatchDialog from "./SeriesMatchDialog.vue";
import { HubEventToken, useSignalR } from "@/signalr/hub";
import { SeriesRefreshProgress } from "../../signalr/SeriesRefreshProgress";
import { SeriesRefreshComplete } from "../../signalr/SeriesRefreshComplete";

const SeriesRefreshProgressToken: HubEventToken<SeriesRefreshProgress> =
  "SeriesRefreshProgress";
const SeriesRefreshCompleteToken: HubEventToken<SeriesRefreshComplete> =
  "SeriesRefreshComplete";

const router = useRouter();
const signalR = useSignalR();

const series: Ref<SeriesOverview[]> = ref([]);
const filter = ref("");
const loading = ref(false);

const refreshing = ref(false);
const refreshProcessed = ref(0);
const refreshTotal = ref(0);
const refreshSucceeded = ref(0);
const refreshFailed = ref(0);

const matchDialogOpen = ref(false);
const snackbar = ref(false);
const snackbarText = ref("");

const busy = computed(() => loading.value || refreshing.value);

const filteredSeries = computed(() => {
  if (!filter.value) return series.value;
  const q = filter.value.toLowerCase();
  return series.value.filter(
    (s) =>
      s.name.toLowerCase().includes(q) ||
      s.authors.some((a) => a.toLowerCase().includes(q)),
  );
});

const unmatchedSeries = computed(() =>
  series.value.filter((s) => !s.isMatched),
);

const matchedCount = computed(
  () => series.value.filter((s) => s.isMatched).length,
);

const openSeries = (s: SeriesOverview) => {
  router.push(`/library/series/${encodeURIComponent(s.name)}`);
};

const loadSeries = async () => {
  loading.value = true;
  try {
    series.value = await SeriesService.getAllSeries();
  } catch {
    snackbarText.value = "Failed to load series";
    snackbar.value = true;
  } finally {
    loading.value = false;
  }
};

const refreshAll = async () => {
  refreshing.value = true;
  refreshProcessed.value = 0;
  refreshTotal.value = 0;
  refreshSucceeded.value = 0;
  refreshFailed.value = 0;

  try {
    await SeriesService.startRefreshAll();
  } catch (e: any) {
    refreshing.value = false;
    snackbarText.value = `Failed to start refresh: ${e?.response?.data ?? e.message}`;
    snackbar.value = true;
  }
};

const onRefreshProgress = (arg: SeriesRefreshProgress) => {
  refreshProcessed.value = arg.processed;
  refreshTotal.value = arg.total;
  refreshSucceeded.value = arg.succeeded;
  refreshFailed.value = arg.failed;
};

const onRefreshComplete = (arg: SeriesRefreshComplete) => {
  refreshing.value = false;
  let msg = `Refresh complete: ${arg.totalSucceeded} of ${arg.totalProcessed} series updated`;
  if (arg.totalFailed > 0) {
    msg += ` (${arg.totalFailed} failed)`;
  }
  snackbarText.value = msg;
  snackbar.value = true;
  loadSeries();
};

signalR.on(SeriesRefreshProgressToken, onRefreshProgress);
signalR.on(SeriesRefreshCompleteToken, onRefreshComplete);

onUnmounted(() => {
  signalR.off(SeriesRefreshProgressToken, onRefreshProgress);
  signalR.off(SeriesRefreshCompleteToken, onRefreshComplete);
});

onMounted(() => {
  loadSeries();
});
</script>

<style scoped>
.cursor-pointer {
  cursor: pointer;
}
</style>

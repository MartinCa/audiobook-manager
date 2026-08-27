<template>
  <v-container>
    <v-row>
      <v-col>
        <v-btn
          variant="text"
          prepend-icon="mdi-arrow-left"
          @click="goBack"
        >
          Back
        </v-btn>
      </v-col>
    </v-row>
    <v-row>
      <v-col>
        <h2 class="text-h5">{{ seriesName }}</h2>
        <div class="text-subtitle-1">
          {{ books.length }} {{ books.length === 1 ? "book" : "books" }} owned
          <span v-if="overview?.isMatched && overview.missingBookCount > 0">
            &middot; {{ overview.missingBookCount }} missing
          </span>
        </div>
      </v-col>
    </v-row>

    <v-row>
      <v-col cols="12">
        <v-card variant="outlined">
          <v-card-text>
            <div class="d-flex align-center flex-wrap ga-2">
              <template v-if="overview?.isMatched">
                <v-chip
                  size="small"
                  color="success"
                  variant="tonal"
                >
                  Matched to {{ overview.matchedSourceName }}
                </v-chip>
                <a
                  v-if="overview.matchedSourceUrl"
                  :href="overview.matchedSourceUrl"
                  target="_blank"
                  rel="noopener"
                  class="text-caption"
                >
                  View at source
                </a>
                <span
                  v-if="overview.matchConfidence != null"
                  class="text-caption text-medium-emphasis"
                >
                  Confidence
                  {{ Math.round(overview.matchConfidence * 100) }}%
                </span>
                <span
                  v-if="overview.lastRefreshedAt"
                  class="text-caption text-medium-emphasis"
                >
                  Last refreshed
                  {{ new Date(overview.lastRefreshedAt).toLocaleString() }}
                </span>
              </template>
              <v-chip
                v-else
                size="small"
                variant="tonal"
              >
                Not matched to a metadata source
              </v-chip>

              <v-spacer />

              <v-btn
                size="small"
                :disabled="busy"
                :loading="loadingCandidates"
                prepend-icon="mdi-link-variant"
                @click="loadCandidates()"
              >
                {{
                  overview?.isMatched ? "Re-match to source" : "Match to source"
                }}
              </v-btn>
              <v-btn
                v-if="overview?.isMatched"
                size="small"
                :disabled="busy"
                prepend-icon="mdi-cloud-refresh"
                @click="refreshSeries()"
              >
                Refresh metadata
              </v-btn>
            </div>

            <v-checkbox
              v-model="includeOmnibusEditions"
              label="Include omnibus/box-set editions"
              hint="When off, bundles like a 'Books 1-4' omnibus are left out of the missing-books list."
              persistent-hint
              density="compact"
              hide-details="auto"
              class="mt-2"
              :disabled="busy || updatingOmnibusSetting"
              @update:model-value="onIncludeOmnibusEditionsChanged"
            />

            <div class="d-flex align-center flex-wrap ga-2 mt-3">
              <v-text-field
                v-model="manualQuery"
                label="Search title/author, or paste a series URL"
                hint="e.g. https://hardcover.app/series/harry-potter"
                density="compact"
                variant="outlined"
                hide-details
                clearable
                style="min-width: 280px; flex: 1 1 280px"
                :disabled="busy"
                @keydown.enter="searchManualCandidates()"
              />
              <v-btn
                size="small"
                :disabled="busy || !manualQuery?.trim()"
                :loading="searchingManually"
                prepend-icon="mdi-magnify"
                @click="searchManualCandidates()"
              >
                Search
              </v-btn>
            </div>

            <OperationProgressBar
              v-if="refreshing"
              class="mt-3"
              :processed="refreshProcessed"
              :total="refreshTotal"
            />

            <div
              v-if="candidates.length"
              class="mt-4"
            >
              <div class="text-subtitle-2 mb-2">Candidates</div>
              <v-list density="compact">
                <v-list-item
                  v-for="candidate in candidates"
                  :key="`${candidate.sourceName}-${candidate.sourceId}`"
                >
                  <v-list-item-title>
                    {{ candidate.seriesName }}
                    <v-chip
                      size="x-small"
                      class="ml-2"
                    >
                      {{ Math.round(candidate.confidence * 100) }}%
                    </v-chip>
                  </v-list-item-title>
                  <v-list-item-subtitle>
                    {{ candidate.sourceName }}
                    <span v-if="candidate.authors.length">
                      &middot; {{ candidate.authors.join(", ") }}
                    </span>
                    <span v-if="candidate.bookCount != null">
                      &middot; {{ candidate.bookCount }} books
                    </span>
                  </v-list-item-subtitle>
                  <template v-slot:append>
                    <v-btn
                      size="small"
                      color="primary"
                      :disabled="busy"
                      @click="applyMatch(candidate)"
                    >
                      Use
                    </v-btn>
                  </template>
                </v-list-item>
              </v-list>
            </div>
            <div
              v-else-if="candidatesLoaded"
              class="text-caption text-medium-emphasis mt-3"
            >
              No candidates found. A source that supports series lookups
              (currently Hardcover) must be configured with an API key.
            </div>
          </v-card-text>
        </v-card>
      </v-col>
    </v-row>

    <v-row>
      <v-col>
        <h3 class="text-h6 mb-2">Owned books</h3>
        <v-list v-if="books.length">
          <v-list-item
            v-for="book in books"
            :key="book.id"
            :to="`/library/book/${book.id}`"
          >
            <v-list-item-title>
              <span v-if="book.seriesPart"
                >Part {{ book.seriesPart }} &mdash;
              </span>
              {{ book.bookName }}
            </v-list-item-title>
            <v-list-item-subtitle>
              <span v-if="book.authors.length">{{
                book.authors.join(", ")
              }}</span>
              <span v-if="book.year"> &middot; {{ book.year }}</span>
              <span v-if="book.narrators.length">
                &middot; Narrated by {{ book.narrators.join(", ") }}
              </span>
              <span v-if="book.durationInSeconds">
                &middot; {{ formatDuration(book.durationInSeconds) }}
              </span>
            </v-list-item-subtitle>
            <template v-slot:append>
              <v-icon>mdi-chevron-right</v-icon>
            </template>
          </v-list-item>
        </v-list>
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
          No books found in this series
        </div>
      </v-col>
    </v-row>

    <v-row v-if="missingBooks.length">
      <v-col>
        <h3 class="text-h6 mb-2">Missing books ({{ missingBooks.length }})</h3>
        <v-list>
          <v-list-item
            v-for="book in missingBooks"
            :key="book.id"
          >
            <v-list-item-title>
              <span v-if="book.position"
                >Part {{ book.position }} &mdash;
              </span>
              {{ book.title }}
            </v-list-item-title>
            <v-list-item-subtitle>
              <span v-if="book.year">{{ book.year }}</span>
              <a
                v-if="book.sourceUrl"
                :href="book.sourceUrl"
                target="_blank"
                rel="noopener"
                class="ml-2"
              >
                View at source
              </a>
            </v-list-item-subtitle>
            <template v-slot:append>
              <v-btn
                size="small"
                variant="text"
                :disabled="busy"
                @click="setIgnored(book, true)"
              >
                Ignore
              </v-btn>
            </template>
          </v-list-item>
        </v-list>
      </v-col>
    </v-row>

    <v-row v-if="ignoredBooks.length">
      <v-col>
        <div class="d-flex align-center mb-2">
          <v-btn
            icon
            variant="text"
            density="comfortable"
            :aria-label="
              ignoredCollapsed
                ? 'Expand ignored books'
                : 'Collapse ignored books'
            "
            @click="ignoredCollapsed = !ignoredCollapsed"
          >
            <v-icon>{{
              ignoredCollapsed ? "mdi-chevron-right" : "mdi-chevron-down"
            }}</v-icon>
          </v-btn>
          <h3 class="text-h6">Ignored books ({{ ignoredBooks.length }})</h3>
        </div>
        <v-list v-show="!ignoredCollapsed">
          <v-list-item
            v-for="book in ignoredBooks"
            :key="book.id"
          >
            <v-list-item-title class="text-medium-emphasis">
              <span v-if="book.position"
                >Part {{ book.position }} &mdash;
              </span>
              {{ book.title }}
            </v-list-item-title>
            <template v-slot:append>
              <v-btn
                size="small"
                variant="text"
                :disabled="busy"
                @click="setIgnored(book, false)"
              >
                Unignore
              </v-btn>
            </template>
          </v-list-item>
        </v-list>
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
import { computed, onMounted, Ref, ref } from "vue";
import { useRoute, useRouter } from "vue-router";
import SeriesService from "../../services/SeriesService";
import { formatDuration } from "../../helpers/formatHelpers";
import {
  SeriesExpectedBook,
  SeriesMatchCandidate,
  SeriesOverview,
  SeriesOwnedBook,
} from "../../types/Series";
import OperationProgressBar from "../OperationProgressBar.vue";
import { HubEventToken } from "@/signalr/hub";
import { useOperationProgress } from "../../composables/useOperationProgress";
import { SeriesRefreshProgress } from "../../signalr/SeriesRefreshProgress";
import { SeriesRefreshComplete } from "../../signalr/SeriesRefreshComplete";

const SeriesRefreshProgressToken: HubEventToken<SeriesRefreshProgress> =
  "SeriesRefreshProgress";
const SeriesRefreshCompleteToken: HubEventToken<SeriesRefreshComplete> =
  "SeriesRefreshComplete";

const route = useRoute();
const router = useRouter();

const books: Ref<SeriesOwnedBook[]> = ref([]);
const missingBooks: Ref<SeriesExpectedBook[]> = ref([]);
const ignoredBooks: Ref<SeriesExpectedBook[]> = ref([]);
const overview: Ref<SeriesOverview | null> = ref(null);
const loading = ref(false);
const seriesName = ref("");
const ignoredCollapsed = ref(true);

const candidates: Ref<SeriesMatchCandidate[]> = ref([]);
const candidatesLoaded = ref(false);
const loadingCandidates = ref(false);
const matchingCandidate = ref(false);

const includeOmnibusEditions = ref(false);
const updatingOmnibusSetting = ref(false);

const manualQuery = ref("");
const searchingManually = ref(false);

const snackbar = ref(false);
const snackbarText = ref("");

const busy = computed(
  () =>
    loading.value ||
    refreshing.value ||
    loadingCandidates.value ||
    matchingCandidate.value ||
    searchingManually.value ||
    updatingOmnibusSetting.value,
);

const goBack = () => {
  const authorId = route.query.authorId;
  if (authorId) {
    router.push(`/library/authors/${authorId}`);
  } else {
    router.push("/library/series");
  }
};

const loadDetail = async () => {
  loading.value = true;
  try {
    const detail = await SeriesService.getSeriesDetail(seriesName.value);
    overview.value = detail.overview;
    books.value = detail.ownedBooks;
    missingBooks.value = detail.missingBooks;
    ignoredBooks.value = detail.ignoredBooks;
    includeOmnibusEditions.value = detail.overview.includeOmnibusEditions;
  } catch {
    snackbarText.value = "Failed to load series";
    snackbar.value = true;
  } finally {
    loading.value = false;
  }
};

const loadCandidates = async () => {
  loadingCandidates.value = true;
  try {
    candidates.value = await SeriesService.getMatchCandidates(seriesName.value);
    candidatesLoaded.value = true;
  } catch (e: any) {
    snackbarText.value = `Failed to fetch candidates: ${e?.response?.data ?? e.message}`;
    snackbar.value = true;
  } finally {
    loadingCandidates.value = false;
  }
};

const searchManualCandidates = async () => {
  const query = manualQuery.value?.trim();
  if (!query) {
    return;
  }

  searchingManually.value = true;
  try {
    candidates.value = await SeriesService.searchMatchCandidates(
      seriesName.value,
      query,
    );
    candidatesLoaded.value = true;
    if (candidates.value.length === 0) {
      snackbarText.value = "No candidates found for that search";
      snackbar.value = true;
    }
  } catch (e: any) {
    snackbarText.value = `Search failed: ${e?.response?.data ?? e.message}`;
    snackbar.value = true;
  } finally {
    searchingManually.value = false;
  }
};

const applyMatch = async (candidate: SeriesMatchCandidate) => {
  matchingCandidate.value = true;
  try {
    await SeriesService.matchSeries(
      seriesName.value,
      candidate.sourceName,
      candidate.sourceId,
      candidate.confidence,
      includeOmnibusEditions.value,
    );
    candidates.value = [];
    candidatesLoaded.value = false;
    snackbarText.value = `Matched to ${candidate.seriesName}`;
    snackbar.value = true;
    await loadDetail();
  } catch (e: any) {
    snackbarText.value = `Failed to match: ${e?.response?.data ?? e.message}`;
    snackbar.value = true;
  } finally {
    matchingCandidate.value = false;
  }
};

const setIgnored = async (book: SeriesExpectedBook, ignored: boolean) => {
  try {
    if (ignored) {
      await SeriesService.ignoreExpectedBook(seriesName.value, book);
    } else {
      await SeriesService.unignoreExpectedBook(seriesName.value, book);
    }
    await loadDetail();
  } catch (e: any) {
    snackbarText.value = `Failed to update: ${e?.response?.data ?? e.message}`;
    snackbar.value = true;
  }
};

const onIncludeOmnibusEditionsChanged = async (value: boolean | null) => {
  updatingOmnibusSetting.value = true;
  try {
    await SeriesService.setIncludeOmnibusEditions(
      seriesName.value,
      value ?? false,
    );
    await loadDetail();
  } catch (e: any) {
    snackbarText.value = `Failed to update: ${e?.response?.data ?? e.message}`;
    snackbar.value = true;
    includeOmnibusEditions.value = !value;
  } finally {
    updatingOmnibusSetting.value = false;
  }
};

const {
  isRunning: refreshing,
  processed: refreshProcessed,
  total: refreshTotal,
  start: startRefreshing,
} = useOperationProgress<SeriesRefreshProgress, SeriesRefreshComplete>({
  key: "series-refresh",
  progressToken: SeriesRefreshProgressToken,
  completeToken: SeriesRefreshCompleteToken,
  getProcessed: (arg) => arg.processed,
  getTotal: (arg) => arg.total,
  onComplete: (arg) => {
    snackbarText.value = arg.stopReason
      ? `Refresh stopped: ${arg.stopReason}`
      : arg.totalFailed > 0
        ? `Refresh finished with ${arg.totalFailed} failure(s)`
        : "Refresh complete";
    snackbar.value = true;
    loadDetail();
  },
});

const refreshSeries = async () => {
  startRefreshing();

  try {
    await SeriesService.startRefreshSeries(seriesName.value);
  } catch (e: any) {
    refreshing.value = false;
    snackbarText.value = `Failed to start refresh: ${e?.response?.data ?? e.message}`;
    snackbar.value = true;
  }
};

onMounted(async () => {
  seriesName.value = route.params.seriesName as string;
  await loadDetail();
});
</script>

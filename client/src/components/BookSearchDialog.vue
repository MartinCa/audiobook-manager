<template>
  <v-card :width="dialogWidth">
    <v-toolbar
      dark
      prominent
    >
      <v-btn
        icon
        dark
        @click="$emit('resultChosen', undefined)"
      >
        <v-icon>mdi-close</v-icon>
      </v-btn>

      <v-text-field
        label="Search term"
        single-line
        hide-details
        clearable
        v-model="searchTerm"
        @keyup.enter="runSearch"
      ></v-text-field>

      <v-btn
        icon
        dark
        :disabled="!searchTerm || !selectedSources.length"
        @click="runSearch"
      >
        <v-icon>mdi-magnify</v-icon>
      </v-btn>
    </v-toolbar>

    <ErrorNotifications
      :errors="errors"
      @error-dismissed="onErrorDismissed"
    />

    <v-card-text>
      <v-row>
        <v-col>
          <template v-if="bookDetails">
            <v-row v-if="bookDetails.fileInfo?.fileName">
              <v-col>
                <v-chip-group column>
                  <v-chip
                    v-for="t in existingTags"
                    class="ma-1"
                    title="Add to search"
                    @click="addSearchTerm(t.value)"
                    label
                  >
                    <span class="existing-name">{{ t.name }} </span>
                    <span class="ml-2 existing-value">{{ t.value }}</span>
                  </v-chip>
                </v-chip-group>
              </v-col>
            </v-row>
          </template>
        </v-col>
      </v-row>

      <v-row>
        <v-col>
          <div class="text-caption text-medium-emphasis mb-1">
            Sources to search
          </div>
          <v-chip-group
            v-model="selectedSources"
            multiple
            column
          >
            <template v-for="service in services">
              <v-tooltip
                v-if="!service.enabled"
                :text="service.disabledReason || 'Unavailable'"
                location="bottom"
              >
                <template v-slot:activator="{ props: tooltipProps }">
                  <v-chip
                    v-bind="tooltipProps"
                    :value="service.name"
                    disabled
                  >
                    {{ service.name }}
                  </v-chip>
                </template>
              </v-tooltip>
              <v-chip
                v-else
                :value="service.name"
                filter
                color="primary"
              >
                {{ service.name }}
              </v-chip>
            </template>
          </v-chip-group>
          <div class="text-caption text-medium-emphasis mt-1">
            <template v-if="selectedSources.length">
              Searching: {{ selectedSources.join(", ") }}
            </template>
            <template v-else> No sources selected. </template>
          </div>
        </v-col>
      </v-row>

      <v-divider></v-divider>
      <template v-if="searching">
        Searching
        <v-progress-linear
          indeterminate
          color="white"
          class="mb-0"
        ></v-progress-linear>
      </template>
      <template v-else-if="gettingDetails">
        <v-row>
          <v-col
            cols="12"
            class="text-center"
          >
            Getting book details
          </v-col>
          <v-col cols="12">
            <v-progress-linear
              indeterminate
              color="white"
              class="mt-1"
            ></v-progress-linear>
          </v-col>
        </v-row>
      </template>
      <template v-else-if="selectedResult">
        <SeriesSelectionTable
          :series="selectedResult.series"
          @series-chosen="chooseSeries"
        />
      </template>
      <template v-else-if="!searchResults?.length && !sourceStatuses?.length">
        Search using the above input.
      </template>
      <template v-else>
        <v-row
          v-if="sourceStatuses?.length"
          class="mt-1"
        >
          <v-col>
            <v-chip
              v-for="status in sourceStatuses"
              :key="status.source"
              class="mr-2 mb-2"
              size="small"
              :color="statusColor(status)"
              :title="status.error"
            >
              {{ status.source }}:
              {{ statusLabel(status) }}
            </v-chip>
          </v-col>
        </v-row>

        <div
          v-if="!searchResults.length"
          class="text-center mt-2"
        >
          No results.
        </div>

        <template v-else-if="smAndDown">
          <v-card
            v-for="(result, i) in searchResults"
            :key="i"
            class="mb-2"
            variant="outlined"
          >
            <v-card-text>
              <div class="d-flex justify-space-between align-start">
                <div>
                  <v-chip
                    size="x-small"
                    class="mb-1"
                    >{{ result.source }}</v-chip
                  >
                  <div class="text-subtitle-1">{{ result.bookName }}</div>
                  <div
                    v-if="result.subtitle"
                    class="text-body-2 text-medium-emphasis"
                  >
                    {{ result.subtitle }}
                  </div>
                </div>
                <v-btn
                  color="primary"
                  size="small"
                  @click="chooseResult(result)"
                >
                  <v-icon>mdi-check</v-icon>
                </v-btn>
              </div>
              <div class="text-body-2 mt-2">
                {{ joinPersons(result.authors) }}
              </div>
              <div
                v-if="result.narrators.length"
                class="text-body-2 text-medium-emphasis"
              >
                Narrated by {{ joinPersons(result.narrators) }}
              </div>
              <div class="text-caption text-medium-emphasis mt-1">
                <span v-if="result.year">{{ result.year }}</span>
                <span v-if="result.duration">
                  &middot; {{ result.duration }}</span
                >
                <span v-if="result.language">
                  &middot; {{ result.language }}</span
                >
              </div>
              <a
                :href="result.url"
                target="_blank"
                class="text-caption"
                >Preview</a
              >
            </v-card-text>
          </v-card>
        </template>

        <v-table
          v-else
          density="compact"
        >
          <thead>
            <tr>
              <th>Source</th>
              <th>Authors</th>
              <th>Narrators</th>
              <th>Name</th>
              <th>Subtitle</th>
              <th>Year</th>
              <th>Duration</th>
              <th>Language</th>
              <th>Number of Ratings</th>
              <th>Link</th>
              <th></th>
            </tr>
          </thead>
          <tbody>
            <tr
              v-for="(result, i) in searchResults"
              :key="i"
            >
              <td>
                <v-chip size="x-small">{{ result.source }}</v-chip>
              </td>
              <td>
                {{ joinPersons(result.authors) }}
              </td>
              <td>
                {{ joinPersons(result.narrators) }}
              </td>
              <td>
                {{ result.bookName }}
              </td>
              <td>
                {{ result.subtitle }}
              </td>
              <td>
                {{ result.year }}
              </td>
              <td>
                {{ result.duration }}
              </td>
              <td>
                {{ result.language }}
              </td>
              <td>
                {{ result.numberOfRatings }}
              </td>
              <td>
                <a
                  :href="result.url"
                  target="_blank"
                  >Preview</a
                >
              </td>
              <td>
                <v-btn
                  color="primary"
                  size="small"
                  @click="chooseResult(result)"
                >
                  <v-icon>mdi-check</v-icon>
                </v-btn>
              </td>
            </tr>
          </tbody>
        </v-table>
      </template>
    </v-card-text>
  </v-card>
</template>

<script setup lang="ts">
import { computed, onMounted, Ref, ref } from "vue";
import { useDisplay } from "vuetify";
import { MetadataSearchResult } from "../types/MetadataSearchResult";
import { MetadataSourceSearchStatus } from "../types/MetadataMultiSourceSearchResult";
import { MetadataSearchServiceInfo } from "../types/MetadataSearchServiceInfo";
import MetadataSearchService from "../services/MetadataSearchService";
import { Audiobook } from "../types/Audiobook";
import ErrorNotifications from "./ErrorNotifications.vue";
import SeriesSelectionTable from "./SeriesSelectionTable.vue";
import { useErrors } from "./errors";
import { useSelectedSearchSources } from "../composables/useSelectedSearchSources";
import { joinPersons } from "../helpers/bookDetailsHelpers";
import { UserNotificationError } from "../types/Errors";

const fileExtRegex = new RegExp(/\.(\w{3,4})(?:$|\?)/);
const props = defineProps<{ bookDetails: Audiobook; dialogWidth?: string }>();
const emit = defineEmits<{
  (e: "resultChosen", result: MetadataSearchResult | undefined): void;
}>();

const { smAndDown } = useDisplay();

const searchTerm = ref("");
const searchResults: Ref<MetadataSearchResult[]> = ref([]);
const sourceStatuses: Ref<MetadataSourceSearchStatus[]> = ref([]);
const selectedResult: Ref<MetadataSearchResult | undefined> = ref(undefined);
const searching = ref(false);
const gettingDetails = ref(false);
const services: Ref<MetadataSearchServiceInfo[]> = ref([]);
const selectedSources = useSelectedSearchSources(services);

const getFileNameExclExt = (fileName: string): string => {
  const regexMatch = fileExtRegex.exec(fileName);
  if (!regexMatch) {
    return "";
  }
  const fileExt = regexMatch[0];
  return fileName.substring(0, fileName.indexOf(fileExt));
};

const addExstingTagIfExists = (
  tagList: { name: string; value: any }[],
  tag: any,
  name: string,
) => {
  if (tag) {
    tagList.push({ name, value: tag });
  }
};

const formatDuration = (durationInSeconds: number): string => {
  const durationInMinutes = durationInSeconds / 60;
  const hrs = Math.floor(durationInMinutes / 60);
  const minutes = Math.round(durationInMinutes % 60);

  const hrsPart = hrs > 0 ? `${hrs} hrs ` : "";
  const minutesPart = `${minutes} min`;
  return `${hrsPart}${minutesPart}`;
};

const existingTags = computed((): { name: string; value: any }[] => {
  if (!props.bookDetails) {
    return [];
  }

  const bookTags = props.bookDetails;

  const tags: { name: string; value: any }[] = [];

  if (props.bookDetails.durationInSeconds) {
    tags.push({
      name: "Duration",
      value: formatDuration(props.bookDetails.durationInSeconds),
    });
  }

  addExstingTagIfExists(tags, joinPersons(bookTags.authors), "Authors");
  addExstingTagIfExists(tags, joinPersons(bookTags.narrators), "Narrators");
  addExstingTagIfExists(tags, bookTags.bookName, "Bookname");
  addExstingTagIfExists(tags, bookTags.subtitle, "Subtitle");
  addExstingTagIfExists(tags, bookTags.year, "Year");

  if (bookTags.series) {
    const seriesPart = bookTags.seriesPart ? ` - ${bookTags.seriesPart}` : "";
    tags.push({ name: "Series", value: `${bookTags.series}${seriesPart}` });
  }

  if (props.bookDetails.fileInfo?.fileName) {
    tags.push({
      name: "Filename",
      value: getFileNameExclExt(props.bookDetails.fileInfo.fileName),
    });
  }

  return tags;
});

const statusLabel = (status: MetadataSourceSearchStatus): string => {
  if (!status.success) {
    return "failed";
  }
  return status.resultCount === 1
    ? "1 result"
    : `${status.resultCount} results`;
};

const statusColor = (
  status: MetadataSourceSearchStatus,
): string | undefined => {
  if (!status.success) {
    return "error";
  }
  return status.resultCount === 0 ? "warning" : undefined;
};

const runSearch = async () => {
  if (!searchTerm.value || !selectedSources.value.length) {
    return;
  }

  searching.value = true;
  selectedResult.value = undefined;
  searchResults.value = [];
  sourceStatuses.value = [];

  try {
    const result = await MetadataSearchService.searchMultiple(
      selectedSources.value,
      searchTerm.value,
    );
    searchResults.value = result.results;
    sourceStatuses.value = result.sourceStatuses;
  } finally {
    searching.value = false;
  }
};

const chooseResult = async (result: MetadataSearchResult) => {
  gettingDetails.value = true;
  try {
    selectedResult.value = await MetadataSearchService.getBookDetails(
      result.url,
    );

    if (
      !selectedResult.value.series?.length ||
      selectedResult.value.series.length == 1
    ) {
      emit("resultChosen", selectedResult.value);
    }
  } finally {
    gettingDetails.value = false;
  }
};

const chooseSeries = (seriesIdx: number) => {
  if (!selectedResult.value) {
    emit("resultChosen", undefined);
    return;
  }

  selectedResult.value.series = [selectedResult.value.series[seriesIdx]];

  emit("resultChosen", selectedResult.value);
};

onMounted(async () => {
  if (props.bookDetails.bookName) {
    let artistPart = "";
    if (props.bookDetails.authors) {
      artistPart += joinPersons(props.bookDetails.authors);
    } else if (props.bookDetails.narrators) {
      artistPart += joinPersons(props.bookDetails.narrators);
    }

    searchTerm.value = `${artistPart ? artistPart + " - " : ""}${
      props.bookDetails.bookName
    }`;
  } else if (props.bookDetails.fileInfo?.fileName) {
    searchTerm.value = getFileNameExclExt(props.bookDetails.fileInfo.fileName);
  }

  try {
    services.value = await MetadataSearchService.getServices();
  } catch {
    throw new UserNotificationError("Failed to load metadata search sources.");
  }
});

const addSearchTerm = (term: string) => {
  const valueToAdd = searchTerm.value ? ` ${term}` : term;
  searchTerm.value += valueToAdd;
};

const { errors, onErrorDismissed } = useErrors();
</script>

<style scoped>
a {
  color: #bb86fc;
}

span.existing-name {
  color: #bb86fc;
}
</style>

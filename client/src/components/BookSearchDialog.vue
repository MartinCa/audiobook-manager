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
      ></v-text-field>

      <v-btn
        color="primary"
        @click="search('audible')"
      >
        <v-icon>mdi-magnify</v-icon>
        Audible
      </v-btn>
      <v-btn
        color="primary"
        @click="search('goodreads')"
      >
        <v-icon>mdi-magnify</v-icon>
        Goodreads
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
        <v-row>
          <v-col
            cols="12"
            class="text-center"
            >Select series</v-col
          >
          <v-col
            cols="0"
            lg="3"
          ></v-col>
          <v-col
            cols="12"
            lg="6"
          >
            <v-table>
              <thead>
                <tr>
                  <th>Series</th>
                  <th>Part</th>
                  <th></th>
                </tr>
              </thead>
              <tbody>
                <tr v-for="(s, idx) in selectedResult.series">
                  <td>
                    {{ s.seriesName }}
                  </td>
                  <td>
                    {{ s.seriesPart }}
                  </td>
                  <td>
                    <v-btn
                      color="primary"
                      v-bind="props"
                      @click="chooseSeries(idx)"
                    >
                      <v-icon>mdi-check</v-icon>
                    </v-btn>
                  </td>
                </tr>
              </tbody>
            </v-table>
          </v-col>
          <v-col
            cols="0"
            lg="3"
          ></v-col>
        </v-row>
      </template>
      <template v-else-if="!searchResults?.length">
        Search using the above input.
      </template>
      <template v-else>
        <v-table density="compact">
          <thead>
            <tr>
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
                  v-bind="props"
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
import { BookSearchResult } from "../types/BookSearchResult";
import SearchService from "../services/SearchService";
import { Audiobook } from "../types/Audiobook";
import ErrorNotifications from "./ErrorNotifications.vue";
import { useErrors } from "./errors";
import { joinPersons } from "../helpers/bookDetailsHelpers";

const fileExtRegex = new RegExp(/\.(\w{3,4})(?:$|\?)/);
const props = defineProps<{ bookDetails: Audiobook; dialogWidth?: string }>();
const emit = defineEmits<{
  (e: "resultChosen", result: BookSearchResult | undefined): void;
}>();

const searchTerm = ref("");
const searchResults: Ref<BookSearchResult[]> = ref([]);
const selectedResult: Ref<BookSearchResult | undefined> = ref(undefined);
const searching = ref(false);
const gettingDetails = ref(false);

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

const search = async (source: string) => {
  searching.value = true;
  selectedResult.value = undefined;
  searchResults.value = [];

  try {
    searchResults.value = await SearchService.searchSource(
      source,
      searchTerm.value,
    );
  } finally {
    searching.value = false;
  }
};

const chooseResult = async (result: BookSearchResult) => {
  gettingDetails.value = true;
  try {
    selectedResult.value = await SearchService.getBookDetails(result.url);

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

onMounted(() => {
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
});

const addSearchTerm = (term: string) => {
  const valueToAdd = searchTerm.value ? ` ${term}` : term;
  searchTerm.value += valueToAdd;
};

const { errors, onErrorDismissed } = useErrors();
</script>

<style scope>
a {
  color: #bb86fc;
}

span.existing-name {
  color: #bb86fc;
}
</style>

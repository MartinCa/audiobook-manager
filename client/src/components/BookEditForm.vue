<template>
  <v-toolbar class="wrap-toolbar">
    <v-btn
      color="primary"
      @click="showSearchDialog = true"
    >
      <v-icon>mdi-magnify</v-icon>
      Search
    </v-btn>
    <v-btn
      color="primary"
      @click="showManualUrlSearchDialog = true"
    >
      <v-icon>mdi-magnify</v-icon>
      Add by URL
    </v-btn>

    <v-spacer></v-spacer>

    <slot name="toolbar-actions"></slot>
  </v-toolbar>

  <v-row>
    <v-col class="text-left">
      <div class="text-subtitle-2 mb-1">Path:</div>
      <DiffDisplay
        v-if="newPath"
        :actual="currentPath"
        :expected="newPath"
      />
      <span v-else>{{ currentPath }}</span>
    </v-col>
  </v-row>

  <v-form ref="form">
    <CoverEditor
      ref="coverEditor"
      :base64-data="input.cover_base64"
      :mime-type="input.cover_mime"
      :cover-url="!input.cover_base64 ? coverUrl : undefined"
      @update:cover="onCoverUpdate"
    />
    <v-row>
      <v-col
        cols="12"
        md="6"
      >
        <v-text-field
          label="Authors"
          hide-details="auto"
          hint="Separated by ','"
          density="comfortable"
          :rules="[(v: any) => !!v || 'Authors is required']"
          v-model="input.authors"
        ></v-text-field>
      </v-col>
      <v-col
        cols="12"
        md="6"
      >
        <v-text-field
          label="Narrators"
          hide-details="auto"
          density="comfortable"
          hint="Separated by ','"
          v-model="input.narrators"
        ></v-text-field>
      </v-col>
      <v-col
        cols="12"
        md="6"
      >
        <v-text-field
          label="Book name"
          hide-details="auto"
          density="comfortable"
          :rules="[(v: any) => !!v || 'Book name is required']"
          v-model="input.bookName"
        ></v-text-field>
      </v-col>
      <v-col
        cols="12"
        md="6"
      >
        <v-text-field
          label="Subtitle"
          hide-details="auto"
          density="comfortable"
          v-model="input.subtitle"
        ></v-text-field>
      </v-col>
      <v-col
        cols="12"
        sm="6"
      >
        <v-text-field
          label="Series name"
          hide-details="auto"
          density="comfortable"
          v-model="input.series"
        >
          <template
            v-slot:prepend
            v-if="seriesMappedNamed"
          >
            <v-icon :title="seriesMappedNamed"> mdi-information </v-icon>
          </template>
        </v-text-field>
      </v-col>
      <v-col
        cols="12"
        sm="6"
      >
        <v-text-field
          label="Series part"
          hide-details="auto"
          density="comfortable"
          v-model="input.seriesPart"
        >
          <template
            v-slot:prepend
            v-if="input.seriesPartWarning"
          >
            <v-icon title="Series part might not be correct">
              mdi-alert
            </v-icon>
          </template>
        </v-text-field>
      </v-col>
      <v-col cols="12">
        <v-text-field
          label="Year"
          type="number"
          hide-details="auto"
          density="comfortable"
          :rules="[(v: any) => !!v || 'Year is required']"
          v-model="input.year"
        ></v-text-field>
      </v-col>
      <v-col
        cols="12"
        sm="8"
        md="9"
        lg="10"
      >
        <v-text-field
          label="Genres"
          hint="Separated by '/'"
          hide-details="auto"
          density="comfortable"
          v-model="input.genres"
        >
        </v-text-field>
      </v-col>
      <v-col
        cols="12"
        sm="4"
        md="3"
        lg="2"
      >
        <v-btn
          color="primary"
          size="large"
          :disabled="isNonfiction"
          block
          @click="addNonfictionGenre"
        >
          Add Nonfiction
        </v-btn>
      </v-col>
      <v-col cols="12">
        <v-textarea
          label="Description"
          hide-details="auto"
          density="comfortable"
          v-model="input.description"
        >
        </v-textarea>
      </v-col>
      <v-col
        cols="12"
        sm="6"
      >
        <v-text-field
          label="Copyright"
          hide-details="auto"
          density="comfortable"
          v-model="input.copyright"
        >
        </v-text-field>
      </v-col>
      <v-col
        cols="12"
        sm="6"
      >
        <v-text-field
          label="Publisher"
          hide-details="auto"
          density="comfortable"
          v-model="input.publisher"
        >
        </v-text-field>
      </v-col>

      <v-col
        cols="12"
        sm="6"
        class="text-left"
      >
        <v-text-field
          label="Www"
          hide-details="auto"
          density="comfortable"
          v-model="input.www"
        >
        </v-text-field>
        <a
          v-if="input.www"
          :href="input.www"
          target="_blank"
          >Preview</a
        >
      </v-col>
      <v-col
        cols="12"
        sm="6"
      >
        <v-text-field
          label="Rating"
          type="number"
          hide-details="auto"
          density="comfortable"
          v-model="input.rating"
        >
        </v-text-field>
      </v-col>
    </v-row>
    <v-row>
      <v-col
        cols="12"
        sm="4"
      >
        <v-btn
          color="warning"
          @click="emit('reset')"
        >
          Reset input
        </v-btn>
      </v-col>
      <slot name="form-actions"></slot>
    </v-row>
  </v-form>

  <v-dialog
    v-if="showSearchDialog"
    v-model="showSearchDialog"
    :width="dialogWidth"
    :fullscreen="mdAndDown"
  >
    <BookSearchDialog
      :dialog-width="dialogWidth"
      :book-details="searchBookDetails"
      @result-chosen="readSearchResult"
    />
  </v-dialog>
  <v-dialog
    v-if="showManualUrlSearchDialog"
    v-model="showManualUrlSearchDialog"
    :width="dialogWidth"
    :fullscreen="mdAndDown"
  >
    <ManualUrlSearchDialog
      :dialog-width="dialogWidth"
      @result-chosen="readSearchResult"
    />
  </v-dialog>
  <v-dialog
    v-if="showTagPreview"
    v-model="showTagPreview"
    :width="dialogWidth"
    :fullscreen="mdAndDown"
  >
    <TagPreviewDialog
      v-if="pendingSearchResult"
      :dialog-width="dialogWidth"
      :current-input="input"
      :search-result="pendingSearchResult"
      @apply="applyPreviewedTags"
      @cancel="showTagPreview = false"
    />
  </v-dialog>
</template>

<script setup lang="ts">
import { computed, ref, Ref } from "vue";
import { Audiobook } from "../types/Audiobook";
import OrganizeAudiobookInput from "../types/OrganizeAudiobookInput";
import BookSearchDialog from "./BookSearchDialog.vue";
import ManualUrlSearchDialog from "./ManualUrlSearchDialog.vue";
import TagPreviewDialog from "./TagPreviewDialog.vue";
import DiffDisplay from "./DiffDisplay.vue";
import CoverEditor from "./CoverEditor.vue";
import { BookSearchResult } from "../types/BookSearchResult";
import { useDialogWidth } from "./dialog";
import { joinPersons } from "../helpers/bookDetailsHelpers";

const props = defineProps<{
  searchBookDetails: Audiobook;
  currentPath: string;
  newPath: string;
  coverUrl?: string;
}>();

const emit = defineEmits<{
  (e: "reset"): void;
}>();

const input = defineModel<OrganizeAudiobookInput>("input", { required: true });

const form: Ref<any | null> = ref(null);
const coverEditor = ref<InstanceType<typeof CoverEditor> | null>(null);
const showSearchDialog = ref(false);
const showManualUrlSearchDialog = ref(false);
const showTagPreview = ref(false);
const pendingSearchResult: Ref<BookSearchResult | null> = ref(null);

const nonfictionGenre = "Nonfiction";

const { dialogWidth, mdAndDown } = useDialogWidth();

const genresSplit = computed(
  (): string[] => input.value.genres?.split("/") ?? [],
);

const isNonfiction = computed((): boolean =>
  genresSplit.value.some((genre) => genre === nonfictionGenre),
);

const seriesMappedNamed = computed((): string => {
  if (
    !input.value.seriesOriginal ||
    input.value.seriesOriginal == input.value.series
  ) {
    return "";
  }
  return `Series name was mapped from '${input.value.seriesOriginal}'`;
});

const onCoverUpdate = (
  base64Data: string | undefined,
  mimeType: string | undefined,
) => {
  input.value.cover_base64 = base64Data;
  input.value.cover_mime = mimeType;
};

const addNonfictionGenre = () => {
  if (isNonfiction.value) {
    return;
  }

  input.value.genres = [...genresSplit.value, nonfictionGenre].join("/");
};

const readSearchResult = (searchData: BookSearchResult | undefined) => {
  showSearchDialog.value = false;
  showManualUrlSearchDialog.value = false;

  if (searchData) {
    pendingSearchResult.value = searchData;
    showTagPreview.value = true;
  }
};

const applyPreviewedTags = (
  result: BookSearchResult,
  selectedFields: Set<string>,
) => {
  showTagPreview.value = false;

  if (selectedFields.has("authors")) {
    input.value.authors = joinPersons(result.authors);
  }
  if (selectedFields.has("narrators")) {
    input.value.narrators = joinPersons(result.narrators) ?? null;
  }
  if (selectedFields.has("bookName")) {
    input.value.bookName = result.bookName;
  }
  if (selectedFields.has("subtitle")) {
    input.value.subtitle = result.subtitle;
  }
  if (selectedFields.has("series")) {
    if (result.series?.length) {
      const seriesData = result.series[0];
      input.value.series = seriesData.seriesName;
      input.value.seriesOriginal = seriesData.originalSeriesName;
      input.value.seriesPart = seriesData.seriesPart;
      input.value.seriesPartWarning = seriesData.partWarning;
    } else {
      input.value.series = "";
      input.value.seriesOriginal = "";
      input.value.seriesPart = "";
      input.value.seriesPartWarning = false;
    }
  }
  if (selectedFields.has("year")) {
    input.value.year = result.year;
  }
  if (selectedFields.has("genres")) {
    input.value.genres = result.genres?.join("/");
  }
  if (selectedFields.has("description")) {
    input.value.description = result.description;
  }
  if (selectedFields.has("rating")) {
    input.value.rating = result.rating;
  }
  if (selectedFields.has("publisher")) {
    input.value.publisher = result.publisher;
  }
  if (selectedFields.has("copyright")) {
    input.value.copyright = result.copyright;
  }
  if (selectedFields.has("asin")) {
    input.value.asin = result.asin;
  }
  if (selectedFields.has("www")) {
    input.value.www = result.url;
  }
  if (selectedFields.has("cover") && result.imageUrl) {
    coverEditor.value?.loadImgFromUrl(result.imageUrl);
  }
};

const validate = async (): Promise<boolean> => {
  if (!form.value) {
    return false;
  }

  const formValidation = await form.value.validate();

  return formValidation.valid;
};

defineExpose({ validate });
</script>

<style scoped>
.wrap-toolbar :deep(.v-toolbar__content) {
  flex-wrap: wrap;
  height: auto;
  padding-top: 4px;
  padding-bottom: 4px;
}
</style>

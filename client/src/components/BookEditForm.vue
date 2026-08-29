<template>
  <v-toolbar class="wrap-toolbar">
    <v-btn
      color="primary"
      @click="showSearchDialog = true"
    >
      <v-icon>mdi-magnify</v-icon>
      Search
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
        <div class="author-field-wrap">
          <v-text-field
            label="Authors"
            hide-details="auto"
            hint="Separated by ','"
            density="comfortable"
            :rules="[(v: any) => !!v || 'Authors is required']"
            v-model="input.authors"
            @focus="authorFieldFocused = true"
            @blur="onAuthorFieldBlur"
          ></v-text-field>
          <v-card
            v-if="authorFieldFocused && authorSuggestions.length > 0"
            class="suggestion-menu"
            elevation="4"
          >
            <v-list density="compact">
              <v-list-item
                v-for="suggestion in authorSuggestions"
                :key="suggestion"
                @mousedown.prevent="applyAuthorSuggestion(suggestion)"
              >
                {{ suggestion }}
              </v-list-item>
            </v-list>
          </v-card>
        </div>
        <v-alert
          v-if="authorHint"
          type="info"
          variant="tonal"
          density="compact"
          class="mt-1"
          closable
          @click:close="authorHint = null"
        >
          Similar existing author:
          <a
            href="#"
            @click.prevent="applyAuthorHint()"
            >{{ authorHint }}</a
          >
        </v-alert>
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
        <v-combobox
          label="Series name"
          hide-details="auto"
          density="comfortable"
          :items="seriesNames"
          v-model="input.series"
          @blur="checkSeriesHint"
        >
          <template
            v-slot:prepend
            v-if="seriesMappedNamed"
          >
            <v-icon :title="seriesMappedNamed"> mdi-information </v-icon>
          </template>
        </v-combobox>
        <v-alert
          v-if="seriesHint"
          type="info"
          variant="tonal"
          density="compact"
          class="mt-1"
          closable
          @click:close="seriesHint = null"
        >
          Similar existing series:
          <a
            href="#"
            @click.prevent="applySeriesHint()"
            >{{ seriesHint }}</a
          >
        </v-alert>
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
      <v-col cols="12">
        <v-text-field
          label="Genres"
          hint="Separated by '/'"
          hide-details="auto"
          density="comfortable"
          v-model="input.genres"
        >
        </v-text-field>
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
      >
        <v-select
          label="Language"
          hide-details="auto"
          density="comfortable"
          clearable
          :items="languageItems"
          item-title="displayName"
          item-value="code"
          v-model="input.language"
        >
        </v-select>
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
import { computed, onMounted, ref, Ref, watch } from "vue";
import { Audiobook } from "../types/Audiobook";
import OrganizeAudiobookInput from "../types/OrganizeAudiobookInput";
import BookSearchDialog from "./BookSearchDialog.vue";
import TagPreviewDialog from "./TagPreviewDialog.vue";
import DiffDisplay from "./DiffDisplay.vue";
import CoverEditor from "./CoverEditor.vue";
import { MetadataSearchResult } from "../types/MetadataSearchResult";
import { useDialogWidth } from "./dialog";
import { joinPersons } from "../helpers/bookDetailsHelpers";
import SimilarValueService from "../services/SimilarValueService";
import LanguageService from "../services/LanguageService";
import { LanguageOption } from "../types/Language";
import { languageSelectItems, normalizeLanguage } from "../helpers/languages";
import {
  findSimilarExisting,
  narrowByQuery,
} from "../helpers/similarValueMatcher";

const props = withDefaults(
  defineProps<{
    searchBookDetails: Audiobook;
    currentPath: string;
    newPath: string;
    coverUrl?: string;
    /**
     * Seed an empty language with the library default (English). Set for a book being added,
     * where most of what is imported is English and an untagged file should not have to be
     * filled in by hand. Deliberately off for a book already in the library: silently granting
     * it a language just because its edit page was opened would hide it from Missing Tags.
     */
    defaultEmptyLanguage?: boolean;
  }>(),
  { defaultEmptyLanguage: false },
);

const emit = defineEmits<{
  (e: "reset"): void;
}>();

const input = defineModel<OrganizeAudiobookInput>("input", { required: true });

const form: Ref<any | null> = ref(null);
const coverEditor = ref<InstanceType<typeof CoverEditor> | null>(null);
const showSearchDialog = ref(false);
const showTagPreview = ref(false);
const pendingSearchResult: Ref<MetadataSearchResult | null> = ref(null);

const { dialogWidth, mdAndDown } = useDialogWidth();

// Entry-time duplicate prevention
const authorNames: Ref<string[]> = ref([]);
const seriesNames: Ref<string[]> = ref([]);
const authorFieldFocused = ref(false);
const authorHint: Ref<string | null> = ref(null);
const seriesHint: Ref<string | null> = ref(null);

// Managed language list. Empty until the fetch lands, which the select tolerates - and the
// unrecognized-value guard below means a book's existing language is never dropped from the
// options even before (or if) the list arrives.
const languages: Ref<LanguageOption[]> = ref([]);
const languageDefaultCode: Ref<string> = ref("");

const languageItems = computed((): LanguageOption[] =>
  languageSelectItems(input.value.language, languages.value),
);

/**
 * A book may carry a free-text tag ("English", "eng") from whoever tagged it; fold it onto the
 * matching option so the select shows it as chosen rather than as an "unrecognized" leftover.
 * An unmanaged language is left exactly as it is.
 */
const applyLanguageDefaults = () => {
  if (languages.value.length === 0) {
    return;
  }

  const normalized = normalizeLanguage(input.value.language, languages.value);
  if (normalized) {
    input.value.language = normalized;
  } else if (props.defaultEmptyLanguage && !input.value.language) {
    input.value.language = languageDefaultCode.value || undefined;
  }
};

const loadLanguages = async () => {
  try {
    const options = await LanguageService.getLanguageOptions();
    languages.value = options.languages;
    languageDefaultCode.value = options.defaultCode;
    applyLanguageDefaults();
  } catch {
    // Non-critical: the select falls back to whatever the book already has.
  }
};

// Reset replaces the whole input object from the parent, which puts the file's own raw tag back
// (or nothing at all), so the fold and the default have to be re-applied. Watching the ref
// itself rather than its contents is what makes that safe: assigning `input.value.language`
// below mutates the object without changing its identity, so this cannot re-enter.
watch(input, () => {
  applyLanguageDefaults();
});

const authorSuggestions = computed((): string[] => {
  const parts = (input.value.authors ?? "").split(",");
  const currentQuery = parts[parts.length - 1] ?? "";
  return narrowByQuery(currentQuery, authorNames.value);
});

const onAuthorFieldBlur = () => {
  // Delay so a suggestion click (@mousedown.prevent) registers before the menu closes.
  setTimeout(() => {
    authorFieldFocused.value = false;
  }, 150);
  checkAuthorHint();
};

const applyAuthorSuggestion = (suggestion: string) => {
  const parts = (input.value.authors ?? "").split(",");
  parts[parts.length - 1] = parts.length > 1 ? ` ${suggestion}` : suggestion;
  input.value.authors = parts.join(",");
  authorFieldFocused.value = false;
};

const applyAuthorHint = () => {
  if (authorHint.value) {
    input.value.authors = authorHint.value;
    authorHint.value = null;
  }
};

const applySeriesHint = () => {
  if (seriesHint.value) {
    input.value.series = seriesHint.value;
    seriesHint.value = null;
  }
};

const checkAuthorHint = () => {
  authorHint.value = null;

  const primaryAuthor = (input.value.authors ?? "").split(",")[0]?.trim();
  if (primaryAuthor) {
    const matches = findSimilarExisting(primaryAuthor, authorNames.value);
    if (matches.length > 0) {
      authorHint.value = matches[0];
    }
  }
};

const checkSeriesHint = () => {
  seriesHint.value = null;

  if (input.value.series) {
    const matches = findSimilarExisting(input.value.series, seriesNames.value);
    if (matches.length > 0) {
      seriesHint.value = matches[0];
    }
  }
};

const checkSimilarHints = () => {
  checkAuthorHint();
  checkSeriesHint();
};

const loadNameLists = async () => {
  try {
    [authorNames.value, seriesNames.value] = await Promise.all([
      SimilarValueService.getAuthorNames(),
      SimilarValueService.getSeriesNames(),
    ]);
  } catch {
    // Non-critical: autocomplete/hints simply won't be available.
  }
};

const mergeNames = (existing: string[], added: string[]): string[] => {
  const missing = added.filter((name) => !existing.includes(name));
  return missing.length > 0 ? [...existing, ...missing] : existing;
};

const noteSavedNames = () => {
  const authors = (input.value.authors ?? "")
    .split(",")
    .map((a) => a.trim())
    .filter(Boolean);
  const series = input.value.series?.trim();

  // Reassign rather than push: the matcher caches each list's folded form against the array
  // itself, so growing one in place leaves that cache describing a list that no longer exists.
  if (authors.length > 0) {
    SimilarValueService.addKnownAuthorNames(authors);
    authorNames.value = mergeNames(authorNames.value, authors);
  }

  if (series) {
    SimilarValueService.addKnownSeriesNames([series]);
    seriesNames.value = mergeNames(seriesNames.value, [series]);
  }
};

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

const readSearchResult = (searchData: MetadataSearchResult | undefined) => {
  showSearchDialog.value = false;

  if (searchData) {
    pendingSearchResult.value = searchData;
    showTagPreview.value = true;
  }
};

const applyPreviewedTags = (
  result: MetadataSearchResult,
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
  if (selectedFields.has("language")) {
    // Sources report a display name ("English") or nothing at all. A recognised value wins; one
    // the library doesn't manage leaves the current selection alone rather than replacing a real
    // value with something the select can't offer.
    const scraped = normalizeLanguage(result.language, languages.value);
    if (scraped) {
      input.value.language = scraped;
    } else if (!input.value.language) {
      input.value.language = languageDefaultCode.value || undefined;
    }
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

  if (selectedFields.has("authors") || selectedFields.has("series")) {
    checkSimilarHints();
  }
};

const validate = async (): Promise<boolean> => {
  if (!form.value) {
    return false;
  }

  const formValidation = await form.value.validate();

  return formValidation.valid;
};

onMounted(async () => {
  await Promise.all([loadNameLists(), loadLanguages()]);
});

defineExpose({ validate, noteSavedNames });
</script>

<style scoped>
.wrap-toolbar :deep(.v-toolbar__content) {
  flex-wrap: wrap;
  height: auto;
  padding-top: 4px;
  padding-bottom: 4px;
}

.author-field-wrap {
  position: relative;
}

.suggestion-menu {
  position: absolute;
  top: 100%;
  left: 0;
  right: 0;
  z-index: 10;
  max-height: 220px;
  overflow-y: auto;
}
</style>

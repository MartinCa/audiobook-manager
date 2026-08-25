<template>
  <ErrorNotifications
    :errors="errors"
    @error-dismissed="onErrorDismissed"
  />

  <v-progress-circular
    v-if="!bookDetails"
    indeterminate
    color="primary"
  ></v-progress-circular>
  <template v-else>
    <BookEditForm
      ref="bookEditForm"
      v-model:input="input"
      :search-book-details="bookDetails"
      :current-path="bookPath"
      :new-path="newPath"
      @reset="resetInput"
    >
      <template #toolbar-actions>
        <v-btn
          color="primary"
          :disabled="organizing"
          @click="organizeBook()"
        >
          <template v-if="organizing">
            <v-progress-circular
              indeterminate
              size="23"
              :width="2"
            />
          </template>
          <template v-else>
            <v-icon>mdi-book-plus</v-icon>
            Organize
          </template>
        </v-btn>
      </template>
      <template #form-actions>
        <v-col
          cols="12"
          sm="4"
        >
          <v-btn
            color="primary"
            :disabled="organizing"
            @click="organizeBook()"
          >
            <template v-if="organizing">
              <v-progress-circular
                indeterminate
                size="23"
                :width="2"
              />
            </template>
            <template v-else>Organize</template>
          </v-btn>
        </v-col>
        <v-col
          cols="12"
          sm="4"
        >
          <v-btn
            color="error"
            @click="showDeleteDialog = true"
          >
            Delete
          </v-btn>
        </v-col>
      </template>
    </BookEditForm>
    <v-dialog
      v-if="showDeleteDialog"
      v-model="showDeleteDialog"
      :width="dialogWidth"
      :fullscreen="mdAndDown"
    >
      <BookDeleteDialog
        :dialog-width="dialogWidth"
        :book-details="bookDetails"
        @delete-book="removeBook"
      />
    </v-dialog>
  </template>
</template>

<script setup lang="ts">
import { onMounted, Ref, ref, watch } from "vue";
import { Audiobook, AudiobookImage } from "../types/Audiobook";
import OrganizeAudiobookInput from "../types/OrganizeAudiobookInput";
import BookDeleteDialog from "./BookDeleteDialog.vue";
import ErrorNotifications from "./ErrorNotifications.vue";
import BookEditForm from "./BookEditForm.vue";
import { useDialogWidth } from "./dialog";
import { useErrors } from "./errors";
import AudiobookService from "../services/AudiobookService";
import { debounce } from "lodash";

const props = defineProps<{
  bookPath: string;
}>();

const emit = defineEmits<{
  (e: "bookDeleted"): void;
  (e: "bookQueued", id: string): void;
}>();

const bookDetails: Ref<Audiobook | null> = ref(null);
const bookEditForm = ref<InstanceType<typeof BookEditForm> | null>(null);
const input: Ref<OrganizeAudiobookInput> = ref({});
const organizing = ref(false);
const showDeleteDialog = ref(false);
const newPath = ref("");

const { dialogWidth, mdAndDown } = useDialogWidth();

watch(
  input,
  async (newValue, oldValue) => {
    await updateNewBookPath();
  },
  { deep: true },
);

const updateNewBookPath = debounce(async () => {
  var book = convertInputToAudiobook();
  if (book) {
    try {
      newPath.value = await AudiobookService.generateNewPath(book);
    } catch {
      newPath.value = "";
    }
  }
}, 300);

const resetInput = () => {
  const book = bookDetails.value;
  const rating = book?.rating ? Number(book?.rating) : undefined;
  input.value = {
    authors: book?.authors.map((x) => x.name).join(", "),
    narrators: book?.narrators.map((x) => x.name).join(", "),
    bookName: book?.bookName,
    subtitle: book?.subtitle,
    series: book?.series,
    seriesPart: book?.seriesPart,
    year: book?.year,
    genres: book?.genres.join("/"),
    description: book?.description,
    copyright: book?.copyright,
    publisher: book?.publisher,
    asin: book?.asin,
    www: book?.www,
    rating: rating,
    cover_base64: bookDetails.value?.cover?.base64Data,
    cover_mime: bookDetails.value?.cover?.mimeType,
  };
};
const convertInputToAudiobook = (): Audiobook | null => {
  if (!bookDetails.value) {
    return null;
  }

  const inp = input.value;

  let cover: AudiobookImage | undefined = undefined;
  if (inp.cover_base64 && inp.cover_mime) {
    cover = {
      base64Data: inp.cover_base64,
      mimeType: inp.cover_mime,
    };
  }

  let newBook: Audiobook = {
    authors: inp.authors?.split(",").map((x) => ({ name: x.trim() })) ?? [],
    narrators: inp.narrators?.split(",").map((x) => ({ name: x.trim() })) ?? [],
    bookName: inp.bookName,
    subtitle: inp.subtitle,
    series: inp.series,
    seriesPart: inp.seriesPart,
    year: inp.year,
    genres: inp.genres?.split("/") ?? [],
    description: inp.description,
    copyright: inp.copyright,
    publisher: inp.publisher,
    rating: inp.rating?.toString(),
    asin: inp.asin,
    www: inp.www,
    cover: cover,
    durationInSeconds: bookDetails.value.durationInSeconds,
    fileInfo: bookDetails.value.fileInfo,
  };

  return newBook;
};

const organizeBook = async () => {
  const formValid = await bookEditForm.value?.validate();

  if (!formValid) {
    return;
  }

  const data = convertInputToAudiobook();
  if (!data) {
    // TODO: Show error
    return;
  }

  organizing.value = true;

  try {
    const organizeId = await AudiobookService.organizeBook(data);
    emit("bookQueued", organizeId);
  } finally {
    organizing.value = false;
  }
};
const getBookDetails = async () => {
  const book = await AudiobookService.parseBookDetails(props.bookPath);
  bookDetails.value = book;
  resetInput();
};

const removeBook = (remove: boolean) => {
  if (remove) {
    emit("bookDeleted");
  }

  showDeleteDialog.value = false;
};
onMounted(() => {
  getBookDetails();
});

const { errors, onErrorDismissed } = useErrors();
</script>

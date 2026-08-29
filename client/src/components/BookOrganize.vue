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
      default-empty-language
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
    <v-dialog
      v-if="showDuplicateDialog && duplicateCheck && pendingOrganizeData"
      v-model="showDuplicateDialog"
      :width="dialogWidth"
      :fullscreen="mdAndDown"
    >
      <DuplicateTargetDialog
        :dialog-width="dialogWidth"
        :new-path="pendingOrganizeData.fileInfo!.fullPath"
        :new-size-in-bytes="pendingOrganizeData.fileInfo!.sizeInBytes"
        :new-duration-in-seconds="pendingOrganizeData.durationInSeconds"
        :target-path="duplicateCheck.targetPath"
        :existing-size-in-bytes="duplicateCheck.existing?.sizeInBytes"
        :existing-duration-in-seconds="
          duplicateCheck.existing?.durationInSeconds
        "
        @existing-deleted="onExistingDeleted"
        @new-deleted="onNewFileDeleted"
        @cancelled="onDuplicateCancelled"
      />
    </v-dialog>
  </template>
</template>

<script setup lang="ts">
import { onMounted, onUnmounted, Ref, ref, watch } from "vue";
import { Audiobook } from "../types/Audiobook";
import { TargetPathCheckResult } from "../types/TargetPathCheck";
import OrganizeAudiobookInput from "../types/OrganizeAudiobookInput";
import BookDeleteDialog from "./BookDeleteDialog.vue";
import DuplicateTargetDialog from "./DuplicateTargetDialog.vue";
import ErrorNotifications from "./ErrorNotifications.vue";
import BookEditForm from "./BookEditForm.vue";
import { useDialogWidth } from "./dialog";
import { useErrors } from "./errors";
import { UserNotificationError } from "../types/Errors";
import AudiobookService from "../services/AudiobookService";
import { debounce } from "lodash";
import { convertInputToAudiobook as buildAudiobook } from "../helpers/organizeAudiobookInput";

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
const showDuplicateDialog = ref(false);
const duplicateCheck: Ref<TargetPathCheckResult | null> = ref(null);
const pendingOrganizeData: Ref<Audiobook | null> = ref(null);

const { dialogWidth, mdAndDown } = useDialogWidth();

// Watches only the fields path generation actually depends on. Deliberately never reads
// cover_base64/cover_mime here: a getter that read them (even to overwrite them afterwards)
// would still track them as reactive dependencies, so editing the cover would keep
// retriggering this debounced call and deep-diffing the large cover string for nothing.
watch(
  () => ({
    authors: input.value.authors,
    narrators: input.value.narrators,
    bookName: input.value.bookName,
    subtitle: input.value.subtitle,
    series: input.value.series,
    seriesPart: input.value.seriesPart,
    year: input.value.year,
    genres: input.value.genres,
    description: input.value.description,
    copyright: input.value.copyright,
    publisher: input.value.publisher,
    asin: input.value.asin,
    www: input.value.www,
    rating: input.value.rating,
  }),
  async () => {
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

// A pending path regeneration would otherwise fire after the component is gone - mutating dead
// refs and issuing a request nobody reads. This component is mounted inside an expansion panel
// in the discovered-books list, whose rows are removed while open (an import finishing), so it
// really does unmount mid-debounce.
onUnmounted(() => {
  updateNewBookPath.cancel();
});

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
    language: book?.language,
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

  return buildAudiobook(input.value, {
    durationInSeconds: bookDetails.value.durationInSeconds,
    fileInfo: bookDetails.value.fileInfo,
  });
};

const organizeBook = async () => {
  const formValid = await bookEditForm.value?.validate();

  if (!formValid) {
    return;
  }

  const data = convertInputToAudiobook();
  if (!data) {
    throw new UserNotificationError(
      "Failed to convert input to audiobook data.",
    );
  }

  organizing.value = true;

  try {
    const check = await AudiobookService.checkTargetPath(data);
    if (check.exists) {
      pendingOrganizeData.value = data;
      duplicateCheck.value = check;
      showDuplicateDialog.value = true;
      return;
    }

    await queueOrganize(data);
  } finally {
    organizing.value = false;
  }
};

const queueOrganize = async (data: Audiobook) => {
  const organizeId = await AudiobookService.organizeBook(data);
  bookEditForm.value?.noteSavedNames();
  emit("bookQueued", organizeId);
};

const onExistingDeleted = async () => {
  showDuplicateDialog.value = false;
  duplicateCheck.value = null;
  const data = pendingOrganizeData.value;
  pendingOrganizeData.value = null;
  if (!data) {
    return;
  }

  organizing.value = true;
  try {
    await queueOrganize(data);
  } finally {
    organizing.value = false;
  }
};

const onNewFileDeleted = () => {
  showDuplicateDialog.value = false;
  duplicateCheck.value = null;
  pendingOrganizeData.value = null;
  emit("bookDeleted");
};

const onDuplicateCancelled = () => {
  showDuplicateDialog.value = false;
  duplicateCheck.value = null;
  pendingOrganizeData.value = null;
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

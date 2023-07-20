<template>
  <v-card :width="dialogWidth">
    <v-toolbar
      dark
      prominent
    >
      <v-btn
        icon
        dark
        @click="closeDialog"
      >
        <v-icon>mdi-close</v-icon>
      </v-btn>
    </v-toolbar>

    <ErrorNotifications
      :errors="errors"
      @error-dismissed="onErrorDismissed"
    />

    <v-card-text>
      <v-row>
        <v-col> Are you sure you want to delete the following files? </v-col>
      </v-row>

      <v-row>
        <v-col>
          <v-list lines="two">
            <v-list-item
              v-for="dirContent in directoryContents"
              :title="dirContent.fileName"
              :subtitle="formatFileSize(dirContent.sizeInBytes)"
            >
            </v-list-item>
          </v-list>
        </v-col>
      </v-row>

      <v-row>
        <v-col>
          <v-btn
            color="error"
            class=""
            @click="deleteBook"
          >
            Delete
          </v-btn>
        </v-col>
        <v-col>
          <v-btn
            color="info"
            class=""
            @click="closeDialog"
          >
            Cancel
          </v-btn>
        </v-col>
      </v-row>
    </v-card-text>
  </v-card>
</template>

<script setup lang="ts">
import { onMounted, Ref, ref } from "vue";
import { Audiobook } from "../types/Audiobook";
import ErrorNotifications from "./ErrorNotifications.vue";
import { useErrors } from "./errors";
import BookFileInfo from "../types/BookFileInfo";
import FilesService from "../services/FilesService";

const props = defineProps<{ bookDetails: Audiobook; dialogWidth?: string }>();
const emit = defineEmits<{
  (e: "delete-book", result: boolean): void;
}>();

const directoryContents: Ref<BookFileInfo[]> = ref([]);

onMounted(async () => {
  if (props.bookDetails.fileInfo) {
    directoryContents.value = await FilesService.getDirectoryContents(
      props.bookDetails.fileInfo.fullPath,
    );
  } else {
    closeDialog();
  }
});

const { errors, onErrorDismissed } = useErrors();

const formatFileSize = (fileSize: number) =>
  `${(fileSize / 1000000).toFixed(2)} MB`;

const deleteBook = async () => {
  if (props.bookDetails.fileInfo) {
    await FilesService.deleteBook(props.bookDetails.fileInfo.fullPath);
    emit("delete-book", true);
  } else {
    closeDialog();
  }
};

const closeDialog = () => {
  emit("delete-book", false);
};
</script>

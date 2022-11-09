<template>
    <v-alert type="error"
             class="alert-error"
             v-for="err in errors"
             @click="dismissError(err)">
        <div class="d-flex flex-row align-center justify-space-between">
            <div>
                {{ err.message }}
            </div>

            <v-btn variant="outlined"
                   size="small"
                   @click="dismissError(err)">
                Dismiss
            </v-btn>
        </div>
    </v-alert>
</template>

<script setup lang="ts">

import { UserNotificationError } from '../types/Errors'

const props = defineProps<{
    errors: UserNotificationError[]
}>();

const emit = defineEmits<{
    (e: "errorDismissed", err: UserNotificationError): void
}>();

const dismissError = (err: UserNotificationError) => {
    emit("errorDismissed", err);
}

</script>

<style>
.alert-error {
    cursor: pointer;
}
</style>

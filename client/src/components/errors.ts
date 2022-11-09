import axios from "axios";
import { onErrorCaptured, ref, Ref } from "vue";
import { UserNotificationError } from "../types/Errors";


export function useErrors() {
    const errors: Ref<UserNotificationError[]> = ref([])

    onErrorCaptured((err) => {
        if (err instanceof UserNotificationError) {
            errors.value.push(err);
            return false;
        }
        if (err instanceof axios.AxiosError) {
            errors.value.push(new UserNotificationError(err.message));
            return false;
        }
    });

    const onErrorDismissed = (err: UserNotificationError) => {
        errors.value = errors.value.filter(e => e != err);
    }

    return { errors, onErrorDismissed };
}

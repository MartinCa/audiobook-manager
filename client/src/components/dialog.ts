import { useDisplay } from "vuetify";
import { computed } from "vue";

export function useDialogWidth() {
  const { mdAndDown } = useDisplay();

  const dialogWidth = computed((): string => {
    if (mdAndDown.value) {
      return "unset";
    }
    return "1200";
  });

  return { dialogWidth, mdAndDown };
}

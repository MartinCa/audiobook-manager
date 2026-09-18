import { useState, type ComponentProps, type ReactNode } from "react";
import { Loader2 } from "lucide-react";
import { Button } from "@/components/ui/button";
import { AppDialog } from "@/components/AppDialog";

export interface ConfirmDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  title: ReactNode;
  description?: ReactNode;
  /** Body content below the description, e.g. a file list or a warning callout. */
  children?: ReactNode;
  onConfirm: () => void | Promise<void>;
  confirmText?: string;
  /** Label shown on the confirm button while onConfirm's promise is pending. Defaults to confirmText. */
  pendingText?: string;
  cancelText?: string;
  confirmVariant?: ComponentProps<typeof Button>["variant"];
  /** Disables the confirm button independent of the pending state, e.g. while a form is invalid. */
  confirmDisabled?: boolean;
  contentClassName?: string;
}

/**
 * AppDialog specialized for a Cancel/Confirm flow: owns the pending state around onConfirm so
 * callers don't hand-roll it per dialog. Dialog closes itself on a confirm that resolves; a
 * confirm that throws leaves it open with pending cleared so the caller's own error toast/state
 * stays visible.
 */
export function ConfirmDialog({
  open,
  onOpenChange,
  title,
  description,
  children,
  onConfirm,
  confirmText = "Confirm",
  pendingText,
  cancelText = "Cancel",
  confirmVariant = "default",
  confirmDisabled = false,
  contentClassName,
}: ConfirmDialogProps) {
  const [pending, setPending] = useState(false);

  const handleConfirm = async () => {
    setPending(true);
    try {
      await onConfirm();
      onOpenChange(false);
    } finally {
      setPending(false);
    }
  };

  // Swallow here, not silently drop: a rejected onConfirm is the caller's error to surface (a
  // toast, typically), and the dialog just needs to not leave an unobserved rejected promise.
  const handleConfirmClick = () => {
    handleConfirm().catch(() => {});
  };

  return (
    <AppDialog
      open={open}
      onOpenChange={onOpenChange}
      title={title}
      description={description}
      contentClassName={contentClassName}
      footer={
        <>
          <Button
            variant="outline"
            className="w-full sm:w-auto"
            onClick={() => onOpenChange(false)}
            disabled={pending}
          >
            {cancelText}
          </Button>
          <Button
            variant={confirmVariant}
            className="w-full sm:w-auto"
            onClick={handleConfirmClick}
            disabled={pending || confirmDisabled}
          >
            {pending ? (
              <>
                <Loader2 className="mr-2 h-4 w-4 animate-spin" />
                {pendingText ?? confirmText}
              </>
            ) : (
              confirmText
            )}
          </Button>
        </>
      }
    >
      {children}
    </AppDialog>
  );
}

export default ConfirmDialog;

import type { ReactNode } from "react";
import { cn } from "@/lib/utils";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";

export interface AppDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  title: ReactNode;
  description?: ReactNode;
  children: ReactNode;
  /** Fixed row below the scrollable body, e.g. Cancel/Confirm buttons. Omit for a body-only dialog. */
  footer?: ReactNode;
  showCloseButton?: boolean;
  contentClassName?: string;
  headerClassName?: string;
  bodyClassName?: string;
  footerClassName?: string;
}

/**
 * Mobile-safe dialog shell: fixed header, one scrollable body region, fixed footer.
 * See DESIGN.md "Complex dialogs with inner scrolling" for the pattern this codifies.
 */
export function AppDialog({
  open,
  onOpenChange,
  title,
  description,
  children,
  footer,
  showCloseButton,
  contentClassName,
  headerClassName,
  bodyClassName,
  footerClassName,
}: AppDialogProps) {
  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent
        showCloseButton={showCloseButton}
        className={cn(
          "flex max-h-[90dvh] w-[calc(100vw-2rem)] flex-col overflow-hidden p-4 sm:max-w-lg sm:p-6",
          contentClassName,
        )}
      >
        <DialogHeader className={headerClassName}>
          <DialogTitle>{title}</DialogTitle>
          {description ? <DialogDescription>{description}</DialogDescription> : null}
        </DialogHeader>

        <div className={cn("min-h-0 flex-1 overflow-y-auto py-2", bodyClassName)}>{children}</div>

        {footer ? (
          <div
            className={cn(
              "border-border flex flex-col-reverse justify-end gap-2 border-t pt-4 sm:flex-row sm:items-center",
              footerClassName,
            )}
          >
            {footer}
          </div>
        ) : null}
      </DialogContent>
    </Dialog>
  );
}

export default AppDialog;

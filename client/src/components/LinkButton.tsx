import type { ComponentProps } from "react";
import { Button } from "@/components/ui/button";

type LinkButtonProps = Omit<ComponentProps<typeof Button>, "nativeButton">;

export function LinkButton(props: LinkButtonProps) {
  return <Button nativeButton={false} {...props} />;
}

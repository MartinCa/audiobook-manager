import { useNavigate } from "@tanstack/react-router";
import { Wrench, ShieldAlert, Tag, Layers, ChevronDown } from "lucide-react";
import { Button } from "@/components/ui/button";
import {
  DropdownMenu,
  DropdownMenuTrigger,
  DropdownMenuContent,
  DropdownMenuGroup,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
} from "@/components/ui/dropdown-menu";

export interface LibraryToolsMenuProps {
  align?: "start" | "end" | "center";
  variant?: "outline" | "default" | "ghost" | "secondary";
  size?: "default" | "sm" | "lg" | "icon";
  className?: string;
}

export function LibraryToolsMenu({
  align = "end",
  variant = "outline",
  size = "sm",
  className,
}: LibraryToolsMenuProps) {
  const navigate = useNavigate();

  return (
    <DropdownMenu>
      <DropdownMenuTrigger
        render={
          <Button variant={variant} size={size} className={className}>
            <Wrench className="mr-1.5 h-3.5 w-3.5" />
            <span>Tools</span>
            <ChevronDown className="ml-1.5 h-3.5 w-3.5 opacity-60" />
          </Button>
        }
      />
      <DropdownMenuContent align={align} className="w-48">
        <DropdownMenuGroup>
          <DropdownMenuLabel className="text-muted-foreground text-xs font-semibold uppercase">
            Library Tools
          </DropdownMenuLabel>
        </DropdownMenuGroup>
        <DropdownMenuSeparator />
        <DropdownMenuItem
          onClick={() => {
            void navigate({ to: "/library/consistency" });
          }}
          className="cursor-pointer text-xs"
        >
          <ShieldAlert className="text-primary mr-2 h-4 w-4" />
          <span>Consistency Check</span>
        </DropdownMenuItem>
        <DropdownMenuItem
          onClick={() => {
            void navigate({ to: "/library/missing-tags" });
          }}
          className="cursor-pointer text-xs"
        >
          <Tag className="text-primary mr-2 h-4 w-4" />
          <span>Missing Tags</span>
        </DropdownMenuItem>
        <DropdownMenuItem
          onClick={() => {
            void navigate({ to: "/library/similar-values" });
          }}
          className="cursor-pointer text-xs"
        >
          <Layers className="text-primary mr-2 h-4 w-4" />
          <span>Similar Values</span>
        </DropdownMenuItem>
      </DropdownMenuContent>
    </DropdownMenu>
  );
}

export default LibraryToolsMenu;

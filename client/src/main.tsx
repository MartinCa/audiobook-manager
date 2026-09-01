import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { createRouter, RouterProvider } from "@tanstack/react-router";
import { QueryClientProvider } from "@tanstack/react-query";
import { createQueryClient } from "@/lib/query";
import { ThemeProvider } from "@/components/theme-provider";
import { SignalRProvider } from "@/components/SignalRProvider";
import { TooltipProvider } from "@/components/ui/tooltip";
import { routeTree } from "./routeTree.gen";
import "./index.css";

const queryClient = createQueryClient();
const router = createRouter({ routeTree });

declare module "@tanstack/react-router" {
  interface Register {
    router: typeof router;
  }
}

createRoot(document.getElementById("root")!).render(
  <StrictMode>
    <QueryClientProvider client={queryClient}>
      <ThemeProvider defaultTheme="system" storageKey="theme">
        <SignalRProvider>
          <TooltipProvider>
            <RouterProvider router={router} />
          </TooltipProvider>
        </SignalRProvider>
      </ThemeProvider>
    </QueryClientProvider>
  </StrictMode>,
);

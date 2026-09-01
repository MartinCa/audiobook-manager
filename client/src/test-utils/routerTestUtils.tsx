import type { ReactElement } from "react";
import {
  createRootRoute,
  createRoute,
  createRouter,
  createMemoryHistory,
  Outlet,
  RouterProvider,
} from "@tanstack/react-router";

/**
 * Renders `ui` behind a minimal, self-contained TanStack Router instance (a root route plus a
 * single index route rendering `ui`), so a component under test that uses <Link>/useNavigate
 * has real router context without depending on this app's actual route tree.
 */
export function RouterTestWrapper({ ui }: { ui: ReactElement }) {
  const rootRoute = createRootRoute({ component: () => <Outlet /> });
  const indexRoute = createRoute({
    getParentRoute: () => rootRoute,
    path: "/",
    component: () => ui,
  });

  const router = createRouter({
    routeTree: rootRoute.addChildren([indexRoute]),
    history: createMemoryHistory({ initialEntries: ["/"] }),
  });

  return <RouterProvider router={router} />;
}

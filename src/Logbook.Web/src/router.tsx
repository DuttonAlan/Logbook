import { Link, createRootRoute, createRoute, createRouter } from '@tanstack/react-router'
import { Layout } from './components/Layout.tsx'
import { ApplicationPage } from './features/applications/ApplicationPage.tsx'
import { ApplicationsPage } from './features/applications/ApplicationsPage.tsx'
import { parseLogSearch } from './features/logs/filters.ts'
import { LogsPage } from './features/logs/LogsPage.tsx'

const rootRoute = createRootRoute({
  component: Layout,
  notFoundComponent: () => (
    <main className="page">
      <h1>Page not found</h1>
      <p>
        <Link to="/">Go to the logs</Link>
      </p>
    </main>
  ),
})

const logsRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/',
  validateSearch: parseLogSearch,
  component: LogsPage,
})

const applicationsRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/applications',
  component: ApplicationsPage,
})

const applicationRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/applications/$applicationId',
  component: ApplicationPage,
})

export const router = createRouter({
  routeTree: rootRoute.addChildren([logsRoute, applicationsRoute, applicationRoute]),
})

declare module '@tanstack/react-router' {
  interface Register {
    router: typeof router
  }
}

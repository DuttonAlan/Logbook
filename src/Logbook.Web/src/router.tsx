import { Link, createRootRoute, createRoute, createRouter } from '@tanstack/react-router'
import { Layout } from './components/Layout.tsx'
import { ApplicationPage } from './features/applications/ApplicationPage.tsx'
import { ApplicationsPage } from './features/applications/ApplicationsPage.tsx'
import { parseLogSearch } from './features/log-explorer/filters.ts'
import { LogsPage } from './features/log-explorer/LogsPage.tsx'
import { ExceptionsPage } from './features/telemetry/ExceptionsPage.tsx'
import { RequestsPage } from './features/telemetry/RequestsPage.tsx'
import { parseExceptionSearch, parseRequestSearch } from './features/telemetry/search.ts'
import { TracePage } from './features/telemetry/TracePage.tsx'

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

const requestsRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/requests',
  validateSearch: parseRequestSearch,
  component: RequestsPage,
})

const exceptionsRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/exceptions',
  validateSearch: parseExceptionSearch,
  component: ExceptionsPage,
})

const traceRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/traces/$traceId',
  component: TracePage,
})

export const router = createRouter({
  routeTree: rootRoute.addChildren([logsRoute, requestsRoute, exceptionsRoute, traceRoute, applicationsRoute, applicationRoute]),
})

declare module '@tanstack/react-router' {
  interface Register {
    router: typeof router
  }
}

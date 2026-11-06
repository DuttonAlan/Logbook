import { useQuery } from '@tanstack/react-query'
import { Link, Outlet } from '@tanstack/react-router'
import { clearAdminKey } from '../auth/adminKey.ts'

interface HealthResponse {
  status: 'Healthy' | 'Degraded' | 'Unhealthy'
}

async function getHealth(signal: AbortSignal): Promise<HealthResponse> {
  // /health answers 503 with a body when unhealthy, so the body is read for every status.
  const response = await fetch('/health', { signal })

  return (await response.json()) as HealthResponse
}

function ServerStatus() {
  const { data, isError } = useQuery({
    queryKey: ['health'],
    queryFn: ({ signal }) => getHealth(signal),
    refetchInterval: 30_000,
    retry: false,
  })

  const status = isError ? 'Not reachable' : (data?.status ?? '…')
  const tone = isError || status === 'Unhealthy' ? 'bad' : status === 'Degraded' ? 'warn' : 'good'

  return (
    <span className={`server-status ${tone}`} title="Status of the server">
      <span className="dot" aria-hidden="true" />
      {status}
    </span>
  )
}

export function Layout() {
  return (
    <div className="app">
      <header className="topbar">
        <Link to="/" className="brand">
          <img src="/favicon.svg" alt="" width="20" height="20" /> Logbook
        </Link>
        <nav className="nav" aria-label="Main">
          <Link to="/" activeOptions={{ exact: true, includeSearch: false }}>
            Logs
          </Link>
          <Link to="/applications">Applications</Link>
        </nav>
        <div className="topbar-end">
          <ServerStatus />
          <button type="button" className="button small quiet" onClick={clearAdminKey}>
            Forget admin key
          </button>
        </div>
      </header>
      <Outlet />
    </div>
  )
}

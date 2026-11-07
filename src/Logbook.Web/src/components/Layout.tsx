import { useQuery } from '@tanstack/react-query'
import { Link, Outlet } from '@tanstack/react-router'
import { useSyncExternalStore } from 'react'
import { signOut } from '../api/auth.ts'
import { getSession, signedOut, subscribeToSession } from '../auth/session.ts'

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
  const userName = useSyncExternalStore(subscribeToSession, () => getSession()?.userName)

  async function leave() {
    try {
      await signOut()
    } finally {
      // Also if it failed, as what is on the screen should go away at once. An answer of 401 means it is over anyway.
      signedOut()
    }
  }

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
          {userName && <span className="hint">{userName}</span>}
          <button type="button" className="button small quiet" onClick={() => void leave()}>
            Sign out
          </button>
        </div>
      </header>
      <Outlet />
    </div>
  )
}

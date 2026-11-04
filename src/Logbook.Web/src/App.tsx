import { useEffect, useState } from 'react'

type HealthStatus = 'Healthy' | 'Degraded' | 'Unhealthy'

export default function App() {
  const [status, setStatus] = useState<HealthStatus | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    // /health answers 503 with a body when unhealthy, so the body is read for every status.
    fetch('/health')
      .then((response) => response.json() as Promise<{ status: HealthStatus }>)
      .then((body) => setStatus(body.status))
      .catch((e: Error) => setError(e.message))
  }, [])

  return (
    <main className="shell">
      <h1>📒 Logbook</h1>
      <p>Lightweight, self-hosted logging for .NET.</p>
      {status && <p className={status === 'Unhealthy' ? 'err' : 'ok'}>Server status: {status}</p>}
      {error && <p className="err">Server not reachable: {error}</p>}
      <p className="muted">The log explorer is coming in milestone M3.</p>
    </main>
  )
}

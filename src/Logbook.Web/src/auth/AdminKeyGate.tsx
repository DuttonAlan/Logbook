import { useState, useSyncExternalStore, type FormEvent, type ReactNode } from 'react'
import { ApiError, send } from '../api/client.ts'
import { adminKeyWasRejected, getAdminKey, setAdminKey, subscribeToAdminKey } from './adminKey.ts'

// Asks for the admin key until sign-in exists (milestone M4). Everything but the health check needs it.
export function AdminKeyGate({ children }: { children: ReactNode }) {
  const adminKey = useSyncExternalStore(subscribeToAdminKey, getAdminKey)

  return adminKey ? children : <AdminKeyForm />
}

function AdminKeyForm() {
  const [key, setKey] = useState('')
  const [isChecking, setIsChecking] = useState(false)
  const [error, setError] = useState(() => (adminKeyWasRejected() ? 'The server no longer accepts the admin key. Enter it again.' : ''))

  async function submit(event: FormEvent) {
    event.preventDefault()

    const candidate = key.trim()

    setIsChecking(true)
    setError('')

    try {
      await send('/api/v1/applications', {}, candidate)
      setAdminKey(candidate)
    } catch (e) {
      setError(e instanceof ApiError && e.status === 401 ? 'This is not the admin key of this server.' : (e as Error).message)
    } finally {
      setIsChecking(false)
    }
  }

  return (
    <main className="gate">
      <form className="panel gate-form" onSubmit={(event) => void submit(event)}>
        <h1>
          <img src="/favicon.svg" alt="" width="24" height="24" /> Logbook
        </h1>
        <label className="field">
          <span>Admin key</span>
          <input
            className="input"
            type="password"
            autoComplete="current-password"
            autoFocus
            required
            value={key}
            onChange={(event) => setKey(event.target.value)}
          />
        </label>
        <p className="hint">
          The value of the setting <code>AdminApi:Key</code>. It is kept until you close this tab.
        </p>
        {error && (
          <p className="message error" role="alert">
            {error}
          </p>
        )}
        <button type="submit" className="button primary" disabled={isChecking || key.trim() === ''}>
          {isChecking ? 'Checking…' : 'Continue'}
        </button>
      </form>
    </main>
  )
}

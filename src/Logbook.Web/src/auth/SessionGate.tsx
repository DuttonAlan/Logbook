import { useEffect, useState, useSyncExternalStore, type FormEvent, type ReactNode } from 'react'
import { getCurrentSession, openIdConnectSignInUrl, signIn, signOut } from '../api/auth.ts'
import { ApiError } from '../api/client.ts'
import { getSession, getSignInNotice, setSession, signOutFailed, signOutSucceeded, subscribeToSession, type Session, type SignInNotice } from './session.ts'

const NOTICES: Record<Exclude<SignInNotice, null>, string> = {
  'session-expired': 'Your session has ended. Sign in again.',
  'access-denied': 'The provider signed you in, but your account has no access to Logbook.',
  'sign-in-failed': 'Signing in through the provider failed. Try again.',
  'sign-out-failed': 'Signing out failed, so this browser is still signed in. Reloading the page would show your logs again.',
}

// Everything but the health check needs a session.
export function SessionGate({ children }: { children: ReactNode }) {
  const session = useSyncExternalStore(subscribeToSession, getSession)
  const [loadError, setLoadError] = useState('')

  const [attempt, setAttempt] = useState(0)

  useEffect(() => {
    getCurrentSession().then(setSession, (e: Error) => setLoadError(e.message))
  }, [attempt])

  function retry() {
    setLoadError('')
    setAttempt((current) => current + 1)
  }

  if (session?.isSignedIn) {
    return children
  }

  return (
    <main className="gate">
      <div className="panel gate-form">
        <h1>
          <img src="/favicon.svg" alt="" width="24" height="24" /> Logbook
        </h1>
        {session ? (
          <SignInOptions session={session} />
        ) : loadError ? (
          <>
            <p className="message error" role="alert">
              {loadError}
            </p>
            <button type="button" className="button" onClick={retry}>
              Try again
            </button>
          </>
        ) : (
          <p>Loading…</p>
        )}
      </div>
    </main>
  )
}

function RetrySignOut() {
  const [isSigningOut, setIsSigningOut] = useState(false)

  async function retry() {
    setIsSigningOut(true)

    try {
      await signOut()
      signOutSucceeded()
    } catch {
      signOutFailed()
    } finally {
      setIsSigningOut(false)
    }
  }

  return (
    <button type="button" className="button" disabled={isSigningOut} onClick={() => void retry()}>
      {isSigningOut ? 'Signing out…' : 'Try signing out again'}
    </button>
  )
}

function SignInOptions({ session }: { session: Session }) {
  const notice = useSyncExternalStore(subscribeToSession, getSignInNotice)

  return (
    <>
      {notice && (
        <p className="message error" role="alert">
          {NOTICES[notice]}
        </p>
      )}
      {notice === 'sign-out-failed' && <RetrySignOut />}
      {session.openIdConnectProvider && (
        <a className="button primary" href={openIdConnectSignInUrl(window.location.pathname + window.location.search)}>
          Sign in with {session.openIdConnectProvider}
        </a>
      )}
      {session.canSignInWithPassword && <PasswordForm session={session} />}
    </>
  )
}

function PasswordForm({ session }: { session: Session }) {
  const [userName, setUserName] = useState('')
  const [password, setPassword] = useState('')
  const [isSigningIn, setIsSigningIn] = useState(false)
  const [error, setError] = useState('')

  async function submit(event: FormEvent) {
    event.preventDefault()

    setIsSigningIn(true)
    setError('')

    try {
      await signIn(userName, password)
      setSession(await getCurrentSession())
    } catch (e) {
      setError(e instanceof ApiError && e.status === 401 ? 'The name or the password is wrong.' : (e as Error).message)
    } finally {
      setIsSigningIn(false)
    }
  }

  return (
    <form className="gate-fields" onSubmit={(event) => void submit(event)}>
      <label className="field">
        <span>Name</span>
        <input
          className="input"
          autoComplete="username"
          autoFocus={!session.openIdConnectProvider}
          required
          value={userName}
          onChange={(event) => setUserName(event.target.value)}
        />
      </label>
      <label className="field">
        <span>Password</span>
        <input
          className="input"
          type="password"
          autoComplete="current-password"
          required
          value={password}
          onChange={(event) => setPassword(event.target.value)}
        />
      </label>
      {error && (
        <p className="message error" role="alert">
          {error}
        </p>
      )}
      <button type="submit" className={session.openIdConnectProvider ? 'button' : 'button primary'} disabled={isSigningIn || userName === '' || password === ''}>
        {isSigningIn ? 'Signing in…' : 'Sign in'}
      </button>
    </form>
  )
}

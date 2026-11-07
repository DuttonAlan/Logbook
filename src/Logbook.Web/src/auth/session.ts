export interface Session {
  isSignedIn: boolean
  userName: string | null
  canSignInWithPassword: boolean
  openIdConnectProvider: string | null
}

export type SignInNotice = 'session-expired' | 'access-denied' | 'sign-in-failed' | 'sign-out-failed' | null

const listeners = new Set<() => void>()

let session: Session | null = null
let notice: SignInNotice = takeNoticeFromAddress()

function notify() {
  listeners.forEach((listener) => listener())
}

// The server sends the browser back with this after a failed sign-in through the provider. It is removed right away, so
// that it does not come back on a reload, and before the router first reads the address.
function takeNoticeFromAddress(): SignInNotice {
  const url = new URL(window.location.href)
  const error = url.searchParams.get('signInError')

  if (error === null) {
    return null
  }

  url.searchParams.delete('signInError')
  window.history.replaceState(window.history.state, '', url)

  return error === 'access-denied' ? 'access-denied' : 'sign-in-failed'
}

// Null until the server has answered.
export function getSession(): Session | null {
  return session
}

export function getSignInNotice(): SignInNotice {
  return notice
}

export function setSession(current: Session) {
  session = current

  if (current.isSignedIn) {
    notice = null
  }

  notify()
}

export function signedOut() {
  if (session) {
    setSession({ ...session, isSignedIn: false, userName: null })
  }
}

// Called when the server answers 401 to a session, e.g. after it has expired or the password has changed.
export function expireSession() {
  if (session?.isSignedIn) {
    notice = 'session-expired'
    signedOut()
  }
}

// The cookie can only be removed by the server, so this browser is still signed in, even though nothing is shown anymore.
export function signOutFailed() {
  if (session && !session.isSignedIn) {
    notice = 'sign-out-failed'
    notify()
  }
}

export function signOutSucceeded() {
  if (notice === 'sign-out-failed') {
    notice = null
    notify()
  }
}

export function subscribeToSession(listener: () => void): () => void {
  listeners.add(listener)
  return () => listeners.delete(listener)
}

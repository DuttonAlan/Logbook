import { signOutFinished, signOutStarted, type Session } from '../auth/session.ts'
import { ApiError, getJson, send } from './client.ts'

const BASE = '/api/v1/auth'

export function getCurrentSession() {
  return getJson<Session>(`${BASE}/session`)
}

export async function signIn(userName: string, password: string) {
  await send(`${BASE}/sign-in`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ userName, password }),
  })
}

// Sign-in waits for the sign-out, so a request that never gets an answer must not block it for good.
const SIGN_OUT_TIMEOUT = 10_000

// Never fails: the session tells how it went.
export async function signOut() {
  signOutStarted()

  try {
    await send(`${BASE}/sign-out`, { method: 'POST', signal: AbortSignal.timeout(SIGN_OUT_TIMEOUT) })
    signOutFinished(true)
  } catch (e) {
    // An answer of 401 means the session has already expired, which is what was asked for.
    signOutFinished(e instanceof ApiError && e.status === 401)
  }
}

// Where the button of the provider leads. The server only accepts a path on this server as the way back.
export function openIdConnectSignInUrl(returnUrl: string) {
  return `${BASE}/oidc/sign-in?returnUrl=${encodeURIComponent(returnUrl)}`
}

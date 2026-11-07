import type { Session } from '../auth/session.ts'
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

export async function signOut() {
  try {
    await send(`${BASE}/sign-out`, { method: 'POST' })
  } catch (e) {
    // The session has already expired, which is what was asked for.
    if (!(e instanceof ApiError && e.status === 401)) {
      throw e
    }
  }
}

// Where the button of the provider leads. The server only accepts a path on this server as the way back.
export function openIdConnectSignInUrl(returnUrl: string) {
  return `${BASE}/oidc/sign-in?returnUrl=${encodeURIComponent(returnUrl)}`
}

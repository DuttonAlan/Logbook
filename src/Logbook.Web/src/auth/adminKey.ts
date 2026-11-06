// The admin key is the only access control until sign-in exists (milestone M4). It stays in sessionStorage: it survives
// a reload but not closing the tab, and it never goes into a URL or a cookie.
const STORAGE_KEY = 'logbook.adminKey'

const listeners = new Set<() => void>()

let wasRejected = false

function notify() {
  listeners.forEach((listener) => listener())
}

export function getAdminKey(): string | null {
  return sessionStorage.getItem(STORAGE_KEY)
}

export function setAdminKey(key: string) {
  wasRejected = false
  sessionStorage.setItem(STORAGE_KEY, key)
  notify()
}

export function clearAdminKey() {
  sessionStorage.removeItem(STORAGE_KEY)
  notify()
}

// Called when the server answers 401 to a key that was accepted before, e.g. after the setting has changed.
export function rejectAdminKey() {
  wasRejected = true
  clearAdminKey()
}

export function adminKeyWasRejected(): boolean {
  return wasRejected
}

export function subscribeToAdminKey(listener: () => void): () => void {
  listeners.add(listener)
  return () => listeners.delete(listener)
}

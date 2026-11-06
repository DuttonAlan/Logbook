import { getAdminKey, rejectAdminKey } from '../auth/adminKey.ts'

interface ProblemDetails {
  title?: string
  detail?: string
  errors?: Record<string, string[]>
}

export class ApiError extends Error {
  readonly status: number

  constructor(status: number, message: string) {
    super(message)
    this.name = 'ApiError'
    this.status = status
  }
}

async function toApiError(response: Response): Promise<ApiError> {
  let problem: ProblemDetails = {}

  try {
    problem = (await response.json()) as ProblemDetails
  } catch {
    // Not every failure has a body, e.g. a proxy answering 502.
  }

  const details = Object.values(problem.errors ?? {}).flat()
  const message = details.length > 0 ? details.join(' ') : (problem.detail ?? problem.title)

  return new ApiError(response.status, message ?? `The server answered with status ${response.status}.`)
}

// Sends the request with the stored key, or with the given one to try a key before storing it.
export async function send(path: string, init: RequestInit = {}, adminKey = getAdminKey()): Promise<Response> {
  const headers = new Headers(init.headers)

  if (adminKey) {
    headers.set('Authorization', `Bearer ${adminKey}`)
  }

  const response = await fetch(path, { ...init, headers })

  if (response.ok) {
    return response
  }

  if (response.status === 401 && adminKey === getAdminKey()) {
    rejectAdminKey()
  }

  throw await toApiError(response)
}

export async function getJson<T>(path: string, signal?: AbortSignal): Promise<T> {
  const response = await send(path, { signal })

  return (await response.json()) as T
}

export async function postJson<T>(path: string, body?: unknown, method = 'POST'): Promise<T> {
  const response = await send(path, {
    method,
    headers: { 'Content-Type': 'application/json' },
    body: body === undefined ? undefined : JSON.stringify(body),
  })

  return (response.status === 204 ? undefined : await response.json()) as T
}

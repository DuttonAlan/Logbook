import { getJson, postJson, send } from './client.ts'
import type { Application, ApplicationSummary, CreatedApplication, IssuedApiKey } from './types.ts'

const BASE = '/api/v1/applications'

export const applicationKeys = {
  all: ['applications'] as const,
  detail: (id: string) => ['applications', id] as const,
}

export function getApplications(signal?: AbortSignal) {
  return getJson<ApplicationSummary[]>(BASE, signal)
}

export function getApplication(id: string, signal?: AbortSignal) {
  return getJson<Application>(`${BASE}/${id}`, signal)
}

export function createApplication(name: string) {
  return postJson<CreatedApplication>(BASE, { name })
}

export function renameApplication(id: string, name: string) {
  return postJson<void>(`${BASE}/${id}`, { name }, 'PUT')
}

export function changeRetention(id: string, retentionDays: number | null) {
  return postJson<void>(`${BASE}/${id}/retention`, { retentionDays }, 'PUT')
}

export async function deleteApplication(id: string) {
  await send(`${BASE}/${id}`, { method: 'DELETE' })
}

export function issueApiKey(id: string, expiresAt: string | null) {
  return postJson<IssuedApiKey>(`${BASE}/${id}/api-keys`, { expiresAt })
}

export async function revokeApiKey(id: string, apiKeyId: string) {
  await send(`${BASE}/${id}/api-keys/${apiKeyId}/revoke`, { method: 'POST' })
}

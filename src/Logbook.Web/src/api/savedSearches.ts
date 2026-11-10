import { getJson, postJson, send } from './client.ts'
import type { SavedSearch } from './types.ts'

const BASE = '/api/v1/saved-searches'

export const savedSearchKeys = {
  all: ['saved-searches'] as const,
}

export function getSavedSearches(signal?: AbortSignal) {
  return getJson<SavedSearch[]>(BASE, signal)
}

export function createSavedSearch(name: string, query: string) {
  return postJson<SavedSearch>(BASE, { name, query })
}

export function changeSavedSearch(id: string, name: string, query: string) {
  return postJson<SavedSearch>(`${BASE}/${id}`, { name, query }, 'PUT')
}

export async function deleteSavedSearch(id: string) {
  await send(`${BASE}/${id}`, { method: 'DELETE' })
}

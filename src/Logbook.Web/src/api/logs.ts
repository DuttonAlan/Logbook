import { parseExactJson } from '../lib/json.ts'
import { send } from './client.ts'
import type { LogEventPage, LogEventQuery } from './types.ts'

const BASE = '/api/v1/logs'

export const LOG_PAGE_SIZE = 200

function toParams(query: LogEventQuery): URLSearchParams {
  const params = new URLSearchParams()

  if (query.from) params.set('from', query.from)
  if (query.to) params.set('to', query.to)
  if (query.applicationId) params.set('applicationId', query.applicationId)
  if (query.environment) params.set('environment', query.environment)
  if (query.text) params.set('text', query.text)
  if (query.traceId) params.set('traceId', query.traceId)
  query.levels?.forEach((level) => params.append('level', level))
  query.properties?.forEach((property) => params.append('property', property))

  return params
}

export async function searchLogs(query: LogEventQuery, cursor: string | null, signal?: AbortSignal): Promise<LogEventPage> {
  const params = toParams(query)

  params.set('limit', String(LOG_PAGE_SIZE))

  if (cursor) {
    params.set('cursor', cursor)
  }

  const response = await send(`${BASE}?${params}`, { signal })

  return parseExactJson<LogEventPage>(await response.text())
}

export function openLogStream(query: LogEventQuery, signal: AbortSignal): Promise<Response> {
  return send(`${BASE}/stream?${toParams(query)}`, { signal, headers: { Accept: 'text/event-stream' } })
}

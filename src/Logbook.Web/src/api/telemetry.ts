import { isRawJsonNumber, parseExactJson } from '../lib/json.ts'
import { send } from './client.ts'
import type { ExceptionGroupQuery, ExceptionGroups, SpanPage, SpanQuery, Trace } from './types.ts'

export const SPAN_PAGE_SIZE = 100

export async function searchSpans(query: SpanQuery, cursor: string | null, signal?: AbortSignal): Promise<SpanPage> {
  const params = new URLSearchParams()

  if (query.from) params.set('from', query.from)
  if (query.to) params.set('to', query.to)
  if (query.kind) params.set('kind', query.kind)
  if (query.applicationId) params.set('applicationId', query.applicationId)
  if (query.environment) params.set('environment', query.environment)
  if (query.name) params.set('name', query.name)
  if (query.success !== undefined) params.set('success', String(query.success))
  if (query.minDurationMs !== undefined) params.set('minDurationMs', String(query.minDurationMs))
  params.set('limit', String(SPAN_PAGE_SIZE))

  if (cursor) {
    params.set('cursor', cursor)
  }

  const response = await send(`/api/v1/spans?${params}`, { signal })

  return (await response.json()) as SpanPage
}

export async function getTrace(traceId: string, signal?: AbortSignal): Promise<Trace> {
  const response = await send(`/api/v1/traces/${encodeURIComponent(traceId)}`, { signal })

  const trace = parseExactJson<Trace>(await response.text())

  // Exact for the properties of the Log Events. A duration is only calculated with, and a small one comes as e.g. 5E-05,
  // which the exact parsing keeps as text.
  return {
    ...trace,
    spans: trace.spans.map((span) => {
      const duration: unknown = span.durationMs

      return isRawJsonNumber(duration) ? { ...span, durationMs: Number(duration.rawJSON) } : span
    }),
  }
}

export async function getExceptionGroups(query: ExceptionGroupQuery, signal?: AbortSignal): Promise<ExceptionGroups> {
  const params = new URLSearchParams()

  if (query.from) params.set('from', query.from)
  if (query.to) params.set('to', query.to)
  if (query.applicationId) params.set('applicationId', query.applicationId)
  if (query.environment) params.set('environment', query.environment)
  params.set('sort', query.sort)
  params.set('limit', '100')

  const response = await send(`/api/v1/exceptions?${params}`, { signal })

  return parseExactJson<ExceptionGroups>(await response.text())
}

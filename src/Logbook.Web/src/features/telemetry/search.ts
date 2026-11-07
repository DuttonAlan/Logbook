import type { ExceptionGroupSort, SpanKind } from '../../api/types.ts'
import type { TimeRange } from '../log-explorer/filters.ts'
import { parseSimpleRange } from './timeRange.ts'

// In the URL, so that a view can be reloaded and shared. Everything optional is left out when not used.
export interface RequestSearch {
  kind?: SpanKind
  range?: TimeRange
  app?: string
  name?: string
  failed?: boolean
}

export interface ExceptionSearch {
  range?: TimeRange
  app?: string
  sort?: ExceptionGroupSort
}

function asText(value: unknown): string | undefined {
  return typeof value === 'string' && value !== '' ? value : undefined
}

export function parseRequestSearch(search: Record<string, unknown>): RequestSearch {
  return {
    kind: search.kind === 'Dependency' ? 'Dependency' : undefined,
    range: parseSimpleRange(search.range),
    app: asText(search.app),
    name: asText(search.name),
    failed: search.failed === true ? true : undefined,
  }
}

export function parseExceptionSearch(search: Record<string, unknown>): ExceptionSearch {
  return {
    range: parseSimpleRange(search.range),
    app: asText(search.app),
    sort: search.sort === 'Count' ? 'Count' : undefined,
  }
}

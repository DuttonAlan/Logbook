import { LOG_LEVELS, type LogEventQuery, type LogLevel } from '../../api/types.ts'

export const TIME_RANGES = [
  { value: '15m', label: 'Last 15 minutes', milliseconds: 15 * 60_000 },
  { value: '1h', label: 'Last hour', milliseconds: 60 * 60_000 },
  { value: '6h', label: 'Last 6 hours', milliseconds: 6 * 60 * 60_000 },
  { value: '24h', label: 'Last 24 hours', milliseconds: 24 * 60 * 60_000 },
  { value: '7d', label: 'Last 7 days', milliseconds: 7 * 24 * 60 * 60_000 },
  { value: 'all', label: 'All time', milliseconds: null },
  { value: 'custom', label: 'Custom…', milliseconds: null },
] as const

export type TimeRange = (typeof TIME_RANGES)[number]['value']

export const DEFAULT_TIME_RANGE: TimeRange = '24h'

// The limit of the log API.
export const MAX_PROPERTY_FILTERS = 10

// The filters in the URL, so that a search can be reloaded and shared. Everything optional is left out when not used.
export interface LogSearch {
  range?: TimeRange
  from?: string
  to?: string
  level?: LogLevel[]
  app?: string
  env?: string
  q?: string
  prop?: string[]
  trace?: string
  exception?: string
  live?: boolean
}

// The router reads values that look like numbers as numbers, so a search for "42" arrives as 42.
function asText(value: unknown): string | undefined {
  if (typeof value === 'number' || typeof value === 'boolean') {
    return String(value)
  }

  return typeof value === 'string' && value !== '' ? value : undefined
}

function asTextList(value: unknown): string[] | undefined {
  const list = (Array.isArray(value) ? value : [value]).map(asText).filter((item) => item !== undefined)

  return list.length > 0 ? list : undefined
}

export function parseLogSearch(search: Record<string, unknown>): LogSearch {
  const range = TIME_RANGES.find(({ value }) => value === search.range)?.value
  const level = asTextList(search.level)?.filter((item): item is LogLevel => (LOG_LEVELS as readonly string[]).includes(item))

  return {
    range,
    from: asText(search.from),
    to: asText(search.to),
    level: level?.length ? level : undefined,
    app: asText(search.app),
    env: asText(search.env),
    q: asText(search.q),
    prop: asTextList(search.prop),
    trace: asText(search.trace),
    exception: asText(search.exception),
    live: search.live === true ? true : undefined,
  }
}

export function hasFilters(search: LogSearch): boolean {
  return Boolean(search.level || search.app || search.env || search.q || search.prop || search.trace || search.exception)
}

// Everything but the time range, which is what the live tail can filter by as well.
export function toContentQuery(search: LogSearch): LogEventQuery {
  return {
    levels: search.level,
    applicationId: search.app,
    environment: search.env,
    text: search.q,
    traceId: search.trace,
    properties: search.prop,
    exceptionFingerprint: search.exception,
  }
}

// A relative range starts at a fixed point (now), so that the query stays the same while the pages load.
export function toSearchQuery(search: LogSearch, now: number): LogEventQuery {
  const query = toContentQuery(search)
  const range = search.range ?? DEFAULT_TIME_RANGE

  if (range === 'custom') {
    return { ...query, from: search.from, to: search.to }
  }

  const milliseconds = TIME_RANGES.find(({ value }) => value === range)?.milliseconds

  return milliseconds ? { ...query, from: new Date(now - milliseconds).toISOString() } : query
}

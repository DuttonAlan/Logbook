import { DEFAULT_TIME_RANGE, type TimeRange } from '../log-explorer/filters.ts'
import { SIMPLE_TIME_RANGES } from '../telemetry/timeRange.ts'

// A chart needs a start, so these pages have no "All time".
export const CHART_TIME_RANGES = SIMPLE_TIME_RANGES.filter(({ value }) => value !== 'all')

export const METRIC_AGGREGATIONS = ['Average', 'Sum', 'Count', 'Min', 'Max'] as const

export type MetricAggregation = (typeof METRIC_AGGREGATIONS)[number]

// The limit of the metrics API.
export const MAX_DIMENSION_FILTERS = 10

// In the URL, so that a view can be reloaded and shared. Everything optional is left out when not used.
export interface OverviewSearch {
  range?: TimeRange
  app?: string
}

export interface MetricSearch {
  range?: TimeRange
  app?: string
  name?: string
  agg?: MetricAggregation
  dim?: string[]
}

function asText(value: unknown): string | undefined {
  if (typeof value === 'number' || typeof value === 'boolean') {
    return String(value)
  }

  return typeof value === 'string' && value !== '' ? value : undefined
}

function parseChartRange(value: unknown): TimeRange | undefined {
  return CHART_TIME_RANGES.find((range) => range.value === value)?.value
}

export function parseOverviewSearch(search: Record<string, unknown>): OverviewSearch {
  return { range: parseChartRange(search.range), app: asText(search.app) }
}

export function parseMetricSearch(search: Record<string, unknown>): MetricSearch {
  const dim = (Array.isArray(search.dim) ? search.dim : [search.dim]).map(asText).filter((item) => item !== undefined)

  return {
    range: parseChartRange(search.range),
    app: asText(search.app),
    name: asText(search.name),
    agg: METRIC_AGGREGATIONS.find((aggregation) => aggregation === search.agg),
    dim: dim.length > 0 ? dim : undefined,
  }
}

export function chartFrom(range: TimeRange | undefined, now: number): string {
  const milliseconds = CHART_TIME_RANGES.find(({ value }) => value === (range ?? DEFAULT_TIME_RANGE))?.milliseconds ?? 24 * 60 * 60_000

  return new Date(now - milliseconds).toISOString()
}

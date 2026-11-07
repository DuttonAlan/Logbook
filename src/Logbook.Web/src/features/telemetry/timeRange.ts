import { DEFAULT_TIME_RANGE, TIME_RANGES, type TimeRange } from '../log-explorer/filters.ts'

// The ranges without a custom one, which these pages have no use for: they answer "what is going on lately?".
export const SIMPLE_TIME_RANGES = TIME_RANGES.filter(({ value }) => value !== 'custom')

export function parseSimpleRange(value: unknown): TimeRange | undefined {
  return SIMPLE_TIME_RANGES.find((range) => range.value === value)?.value
}

// A relative range starts at a fixed point (now), so that the query stays the same while the pages load.
export function toFrom(range: TimeRange | undefined, now: number): string | undefined {
  const milliseconds = SIMPLE_TIME_RANGES.find(({ value }) => value === (range ?? DEFAULT_TIME_RANGE))?.milliseconds

  return milliseconds ? new Date(now - milliseconds).toISOString() : undefined
}

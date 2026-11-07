import type { LogEvent, Span } from '../../api/types.ts'

export type TimelineRow =
  | { kind: 'span'; id: string; depth: number; offset: number; width: number; span: Span }
  | { kind: 'log'; id: string; depth: number; offset: number; event: LogEvent }

export interface Timeline {
  rows: TimelineRow[]
  durationMs: number
}

// In milliseconds with fractions. A Date only keeps whole milliseconds, while spans often last only a few, so it would put
// a call outside of the request that made it.
export function toTime(timestamp: string): number {
  const fraction = /\.(\d+)/.exec(timestamp)?.[1]
  const wholeSeconds = new Date(timestamp.replace(/\.\d+/, '')).getTime()

  return fraction ? wholeSeconds + Number(`0.${fraction}`) * 1000 : wholeSeconds
}

// Puts each span under the one that caused it, and the Log Events under the span they were written in, all in the order
// of time. What has no known parent starts at the top: a parent can be missing, e.g. if it was in an application that does
// not send to Logbook, or if its span has not arrived yet.
export function buildTimeline(spans: Span[], logEvents: LogEvent[]): Timeline {
  const start = Math.min(...spans.map(({ timestamp }) => toTime(timestamp)), ...logEvents.map(({ timestamp }) => toTime(timestamp)))

  const end = Math.max(
    ...spans.map(({ timestamp, durationMs }) => toTime(timestamp) + durationMs),
    ...logEvents.map(({ timestamp }) => toTime(timestamp)),
  )

  const durationMs = end - start
  const toOffset = (time: number) => (durationMs > 0 ? (time - start) / durationMs : 0)

  // Spans can arrive twice from different senders with the same Id only by mistake; the first one wins as the parent.
  const spanIds = new Set(spans.map(({ spanId }) => spanId))
  const children = new Map<string | null, (Span | LogEvent)[]>()

  function addChild(parent: string | null, item: Span | LogEvent) {
    const siblings = children.get(parent)

    if (siblings) {
      siblings.push(item)
    } else {
      children.set(parent, [item])
    }
  }

  for (const span of spans) {
    // A span that names itself as its parent would never be reached otherwise.
    addChild(span.parentSpanId && span.parentSpanId !== span.spanId && spanIds.has(span.parentSpanId) ? span.parentSpanId : null, span)
  }

  for (const event of logEvents) {
    addChild(event.spanId && spanIds.has(event.spanId) ? event.spanId : null, event)
  }

  const rows: TimelineRow[] = []
  const visited = new Set<string>()

  function visit(parent: string | null, depth: number) {
    const items = [...(children.get(parent) ?? [])].sort((a, b) => toTime(a.timestamp) - toTime(b.timestamp))

    for (const item of items) {
      if ('kind' in item) {
        // The data comes from senders, so parents could form a cycle.
        if (!visited.has(item.id)) {
          visited.add(item.id)

          const offset = toOffset(toTime(item.timestamp))

          rows.push({
            kind: 'span',
            id: item.id,
            depth,
            offset,
            width: durationMs > 0 ? item.durationMs / durationMs : 1,
            span: item,
          })

          visit(item.spanId, depth + 1)
        }
      } else {
        rows.push({ kind: 'log', id: item.id, depth, offset: toOffset(toTime(item.timestamp)), event: item })
      }
    }
  }

  visit(null, 0)

  // Spans in a cycle have no way in from the top, and must still be shown.
  for (const span of spans) {
    if (!visited.has(span.id)) {
      visited.add(span.id)
      rows.push({ kind: 'span', id: span.id, depth: 0, offset: toOffset(toTime(span.timestamp)), width: durationMs > 0 ? span.durationMs / durationMs : 1, span })
    }
  }

  return { rows, durationMs }
}

import { useEffect, useState } from 'react'
import { ApiError } from '../../api/client.ts'
import { LOG_PAGE_SIZE, openLogStream, searchLogs } from '../../api/logs.ts'
import type { LogEvent, LogEventQuery } from '../../api/types.ts'
import { parseExactJson } from '../../lib/json.ts'
import { readServerSentEvents } from '../../lib/sse.ts'

export type LiveTailStatus = 'off' | 'connecting' | 'live' | 'reconnecting' | 'failed'

export interface LiveTail {
  events: LogEvent[]
  status: LiveTailStatus
  hasGap: boolean
  error: string | null
  dismissGap: () => void
}

// What stays in the browser: the list is virtualized, but the Log Events themselves take memory.
const MAX_EVENTS = 5000

// Rendering for every Log Event would keep the browser busy during a burst.
const RENDER_INTERVAL = 250

const MAX_RETRY_DELAY = 30_000

function newestFirst(a: LogEvent, b: LogEvent): number {
  return Date.parse(b.timestamp) - Date.parse(a.timestamp) || (a.id < b.id ? 1 : a.id > b.id ? -1 : 0)
}

// Log Events can arrive twice, from the stream and from the search after connecting, and late, with an older timestamp.
function merge(current: LogEvent[], incoming: LogEvent[]): LogEvent[] {
  const ids = new Set(current.map(({ id }) => id))
  const fresh = incoming.filter(({ id }) => !ids.has(id) && Boolean(ids.add(id)))

  return fresh.length === 0 ? current : [...fresh, ...current].sort(newestFirst).slice(0, MAX_EVENTS)
}

function wait(milliseconds: number, signal: AbortSignal): Promise<void> {
  return new Promise((resolve) => {
    const timer = setTimeout(resolve, milliseconds)

    signal.addEventListener(
      'abort',
      () => {
        clearTimeout(timer)
        resolve()
      },
      { once: true },
    )
  })
}

// Follows the Log Events matching the query while enabled. The query must keep its identity while it stays the same.
export function useLiveTail(query: LogEventQuery, enabled: boolean): LiveTail {
  const [events, setEvents] = useState<LogEvent[]>([])
  const [status, setStatus] = useState<LiveTailStatus>('off')
  const [hasGap, setHasGap] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    setEvents([])
    setHasGap(false)
    setError(null)

    if (!enabled) {
      setStatus('off')
      return
    }

    const controller = new AbortController()
    const { signal } = controller

    let waiting: LogEvent[] = []
    let newestSeen = 0

    function receive(logEvents: LogEvent[]) {
      waiting.push(...logEvents)
      newestSeen = logEvents.reduce((newest, { timestamp }) => Math.max(newest, Date.parse(timestamp)), newestSeen)
    }

    // After the stream is connected, so that nothing falls between the end of this page and the start of the stream.
    async function catchUp() {
      // Before the search: a live Log Event can arrive while it runs, and would hide that this page does not reach back.
      const seenBefore = newestSeen

      try {
        const page = await searchLogs(query, null, signal)
        const oldest = page.items.at(-1)

        // A page full of Log Events newer than anything seen before: more may have been missed while disconnected.
        if (seenBefore > 0 && page.items.length === LOG_PAGE_SIZE && oldest && Date.parse(oldest.timestamp) > seenBefore) {
          setHasGap(true)
        }

        receive(page.items)
      } catch {
        // The stream goes on. If the server is gone, the stream notices that, too.
      }
    }

    async function follow() {
      let attempt = 0

      while (!signal.aborted) {
        setStatus(attempt === 0 ? 'connecting' : 'reconnecting')

        try {
          const response = await openLogStream(query, signal)

          await readServerSentEvents(response.body!, ({ name, data }) => {
            if (name === 'connected') {
              attempt = 0
              setStatus('live')
              void catchUp()
            } else if (name === 'log') {
              receive([parseExactJson<LogEvent>(data)])
            } else if (name === 'gap') {
              setHasGap(true)
            }
          })
        } catch (e) {
          if (e instanceof ApiError && e.status < 500) {
            setStatus('failed')
            setError(e.message)
            return
          }
        }

        attempt++

        await wait(Math.min(1000 * 2 ** (attempt - 1), MAX_RETRY_DELAY), signal)
      }
    }

    const renderTimer = setInterval(() => {
      if (waiting.length > 0) {
        const incoming = waiting

        waiting = []
        setEvents((current) => merge(current, incoming))
      }
    }, RENDER_INTERVAL)

    void follow()

    return () => {
      controller.abort()
      clearInterval(renderTimer)
    }
  }, [query, enabled])

  return { events, status, hasGap, error, dismissGap: () => setHasGap(false) }
}

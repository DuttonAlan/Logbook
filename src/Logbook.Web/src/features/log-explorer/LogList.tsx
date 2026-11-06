import { useVirtualizer } from '@tanstack/react-virtual'
import { useEffect, useImperativeHandle, useRef, type KeyboardEvent, type Ref } from 'react'
import type { LogEvent } from '../../api/types.ts'
import { formatDate, formatLogTime } from '../../lib/format.ts'
import { LevelBadge } from './LevelBadge.tsx'

const ROW_HEIGHT = 30

// The next page starts loading this many rows before the end comes into view.
const LOAD_AHEAD = 40

// Further down than this counts as reading, which holds the live tail.
const SCROLLED_AWAY_AT = ROW_HEIGHT

export interface LogListHandle {
  scrollToTop: () => void
}

interface LogListProps {
  ref?: Ref<LogListHandle>
  events: LogEvent[]
  selectedId: string | undefined
  applicationNames: Map<string, string>
  onSelect: (event: LogEvent) => void
  onScrolledAwayChange?: (isAway: boolean) => void
  hasNextPage?: boolean
  isFetchingNextPage?: boolean
  // After a failed page, the next one is only requested on demand, or it would be requested forever.
  nextPageFailed?: boolean
  onFetchNextPage?: () => void
}

function firstLine(message: string): string {
  const end = message.search(/[\r\n]/)

  return end === -1 ? message : message.slice(0, end)
}

export function LogList({
  ref,
  events,
  selectedId,
  applicationNames,
  onSelect,
  onScrolledAwayChange,
  hasNextPage,
  isFetchingNextPage,
  nextPageFailed,
  onFetchNextPage,
}: LogListProps) {
  const scroller = useRef<HTMLDivElement>(null)

  const virtualizer = useVirtualizer({
    count: events.length,
    getScrollElement: () => scroller.current,
    estimateSize: () => ROW_HEIGHT,
    getItemKey: (index) => events[index].id,
    overscan: 20,
  })

  useImperativeHandle(ref, () => ({ scrollToTop: () => scroller.current?.scrollTo({ top: 0 }) }))

  const rows = virtualizer.getVirtualItems()
  const lastVisible = rows.at(-1)?.index ?? -1

  useEffect(() => {
    if (hasNextPage && !isFetchingNextPage && !nextPageFailed && lastVisible >= events.length - LOAD_AHEAD) {
      onFetchNextPage?.()
    }
  }, [hasNextPage, isFetchingNextPage, nextPageFailed, lastVisible, events.length, onFetchNextPage])

  function moveSelection(event: KeyboardEvent) {
    if (event.key !== 'ArrowDown' && event.key !== 'ArrowUp') {
      return
    }

    event.preventDefault()

    const current = events.findIndex(({ id }) => id === selectedId)
    const next = Math.max(0, Math.min(events.length - 1, current + (event.key === 'ArrowDown' ? 1 : -1)))

    if (events[next]) {
      onSelect(events[next])
      virtualizer.scrollToIndex(next)

      // The row may only exist after the scrolling has rendered it.
      requestAnimationFrame(() => scroller.current?.querySelector<HTMLElement>(`[data-index="${next}"]`)?.focus())
    }
  }

  return (
    <div className="log-list">
      <div className="log-row log-header" aria-hidden="true">
        <span>Time</span>
        <span>Level</span>
        <span className="log-application">Application</span>
        <span className="log-environment">Environment</span>
        <span>Message</span>
      </div>
      <div
        ref={scroller}
        className="log-scroller"
        onKeyDown={moveSelection}
        onScroll={(event) => onScrolledAwayChange?.(event.currentTarget.scrollTop > SCROLLED_AWAY_AT)}
      >
        <div role="list" aria-label="Log Events" style={{ height: virtualizer.getTotalSize(), position: 'relative' }}>
          {rows.map((row) => {
            const event = events[row.index]

            return (
              <div
                role="listitem"
                key={row.key}
                style={{ position: 'absolute', top: 0, left: 0, right: 0, height: row.size, transform: `translateY(${row.start}px)` }}
              >
                <button
                  type="button"
                  data-index={row.index}
                  className={`log-row log-entry level-row-${event.level.toLowerCase()}`}
                  aria-current={event.id === selectedId ? 'true' : undefined}
                  onClick={() => onSelect(event)}
                >
                  <span className="log-time" title={formatDate(event.timestamp)}>
                    {formatLogTime(event.timestamp)}
                  </span>
                  <LevelBadge level={event.level} />
                  <span className="log-application">{applicationNames.get(event.applicationId) ?? 'Deleted'}</span>
                  <span className="log-environment">{event.environment}</span>
                  <span className="log-message">{firstLine(event.message) || <em>Empty message</em>}</span>
                </button>
              </div>
            )
          })}
        </div>
        {nextPageFailed && (
          <p className="log-list-end">
            <span className="message error">The next Log Events could not be loaded.</span>{' '}
            <button type="button" className="button small" onClick={onFetchNextPage}>
              Try again
            </button>
          </p>
        )}
        {isFetchingNextPage && <p className="log-list-end">Loading more…</p>}
      </div>
    </div>
  )
}

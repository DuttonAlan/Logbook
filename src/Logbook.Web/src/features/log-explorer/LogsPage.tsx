import { keepPreviousData, useInfiniteQuery, useQuery } from '@tanstack/react-query'
import { Link, getRouteApi } from '@tanstack/react-router'
import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { applicationKeys, getApplications } from '../../api/applications.ts'
import { searchLogs } from '../../api/logs.ts'
import type { LogEvent } from '../../api/types.ts'
import { FilterBar } from './FilterBar.tsx'
import { LogDetail } from './LogDetail.tsx'
import { LogList, type LogListHandle } from './LogList.tsx'
import { MAX_PROPERTY_FILTERS, hasFilters, toContentQuery, toSearchQuery, type LogSearch } from './filters.ts'
import { useLiveTail, type LiveTailStatus } from './useLiveTail.ts'

const route = getRouteApi('/')

const LIVE_STATUS_TEXT: Record<LiveTailStatus, string> = {
  off: '',
  connecting: 'Connecting…',
  live: 'Live',
  reconnecting: 'Connection lost, reconnecting…',
  failed: 'Stopped',
}

export function LogsPage() {
  const search = route.useSearch()
  const navigate = route.useNavigate()

  const [selected, setSelected] = useState<LogEvent>()
  const [now, setNow] = useState(() => Date.now())
  const [isScrolledAway, setIsScrolledAway] = useState(false)
  const [isPaused, setIsPaused] = useState(false)
  const [held, setHeld] = useState<LogEvent[] | null>(null)
  const [knownEnvironments, setKnownEnvironments] = useState<string[]>([])

  const list = useRef<LogListHandle>(null)
  const isLive = search.live === true

  const onChange = useCallback(
    (patch: Partial<LogSearch>, options?: { replace?: boolean }) => {
      // A relative time range counts from the moment of the search, not from when the page was opened.
      setNow(Date.now())
      void navigate({ search: (previous) => ({ ...previous, ...patch }), replace: options?.replace })
    },
    [navigate],
  )

  const applications = useQuery({ queryKey: applicationKeys.all, queryFn: ({ signal }) => getApplications(signal) })

  const applicationNames = useMemo(() => new Map(applications.data?.map(({ id, name }) => [id, name])), [applications.data])

  const searchQuery = useMemo(() => toSearchQuery(search, now), [search, now])
  const contentQuery = useMemo(() => toContentQuery(search), [search])

  // The key of the content query rather than the object, as the search has a new identity after every navigation.
  const contentKey = JSON.stringify(contentQuery)
  const liveQuery = useMemo(() => JSON.parse(contentKey) as typeof contentQuery, [contentKey])

  const results = useInfiniteQuery({
    queryKey: ['logs', searchQuery, now],
    queryFn: ({ pageParam, signal }) => searchLogs(searchQuery, pageParam, signal),
    initialPageParam: null as string | null,
    getNextPageParam: (page) => page.nextCursor,
    placeholderData: keepPreviousData,
    // It would copy exact numbers into plain objects when it merges a refetch, and these pages are large to compare.
    structuralSharing: false,
    enabled: !isLive,
  })

  const tail = useLiveTail(liveQuery, isLive)

  const found = useMemo(() => results.data?.pages.flatMap((page) => page.items) ?? [], [results.data])

  // The list holds still while someone reads further down or has paused it, and new Log Events wait above.
  const isHolding = isLive && (isPaused || isScrolledAway)

  useEffect(() => {
    setHeld((current) => (isHolding ? (current ?? tail.events) : null))
  }, [isHolding, tail.events])

  const shown = isLive ? (held ?? tail.events) : found

  const newCount = useMemo(() => {
    if (!held) {
      return 0
    }

    const ids = new Set(held.map(({ id }) => id))

    return tail.events.filter(({ id }) => !ids.has(id)).length
  }, [held, tail.events])

  // What is held belongs to the previous search, and the new one starts at the top.
  useEffect(() => {
    setIsPaused(false)
    setIsScrolledAway(false)
    setHeld(null)
    list.current?.scrollToTop()
  }, [isLive, contentKey])

  useEffect(() => {
    // Only grows, so that the suggestions do not vanish when a filter is applied.
    setKnownEnvironments((known) => {
      const merged = new Set(known)

      shown.forEach(({ environment }) => merged.add(environment))

      return merged.size === known.length ? known : [...merged].sort()
    })
  }, [shown])

  const closeDetail = useCallback(() => setSelected(undefined), [])

  useEffect(() => {
    function onKeyDown(event: KeyboardEvent) {
      if (event.key === 'Escape' && !document.querySelector('dialog[open]')) {
        closeDetail()
      }
    }

    window.addEventListener('keydown', onKeyDown)

    return () => window.removeEventListener('keydown', onKeyDown)
  }, [closeDetail])

  const fetchNextPage = results.fetchNextPage
  const onFetchNextPage = useCallback(() => void fetchNextPage(), [fetchNextPage])

  function catchUp() {
    setIsPaused(false)
    setIsScrolledAway(false)
    list.current?.scrollToTop()
  }

  function addPropertyFilter(filter: string) {
    const properties = search.prop ?? []

    if (!properties.includes(filter)) {
      onChange({ prop: [...properties, filter] })
    }
  }

  function showTrace(traceId: string) {
    // Without the other filters: a trace can span several Applications and Environments, and its age is not known.
    onChange({
      trace: traceId,
      range: 'all',
      from: undefined,
      to: undefined,
      level: undefined,
      app: undefined,
      env: undefined,
      q: undefined,
      prop: undefined,
      exception: undefined,
      live: undefined,
    })
  }

  function showOccurrences(fingerprint: string) {
    // Without the other filters, or it would only show the occurrences that happen to match them. A group belongs to one
    // Application, as the same stack in another one is another group.
    onChange({
      exception: fingerprint,
      app: selected?.applicationId,
      range: 'all',
      from: undefined,
      to: undefined,
      level: undefined,
      env: undefined,
      q: undefined,
      prop: undefined,
      trace: undefined,
      live: undefined,
    })
  }

  const isLoading = isLive ? tail.status === 'connecting' : results.isPending
  const error = isLive ? tail.error : results.isError && !results.isFetchNextPageError ? results.error.message : null

  return (
    <main className="logs-page">
      <FilterBar search={search} applications={applications.data ?? []} environments={knownEnvironments} onChange={onChange}>
        {!isLive && (
          <button type="button" className="button" onClick={() => setNow(Date.now())} disabled={results.isFetching}>
            Refresh
          </button>
        )}
        <button type="button" className={`button ${isLive ? 'primary' : ''}`} aria-pressed={isLive} onClick={() => onChange({ live: isLive ? undefined : true })}>
          Live tail
        </button>
      </FilterBar>

      {isLive && (
        <div className="live-bar" role="status">
          <span className={`live-status live-status-${tail.status}`}>
            <span className="dot" aria-hidden="true" />
            {isHolding && tail.status === 'live' ? 'Live, holding the list' : LIVE_STATUS_TEXT[tail.status]}
          </span>
          {newCount > 0 && (
            <button type="button" className="button small primary" onClick={catchUp}>
              {newCount} new Log {newCount === 1 ? 'Event' : 'Events'}
            </button>
          )}
          <button type="button" className="button small" onClick={() => (isPaused ? catchUp() : setIsPaused(true))}>
            {isPaused ? 'Resume' : 'Pause'}
          </button>
          {tail.hasGap && (
            <span className="message warning">
              Some Log Events were skipped, because they arrived too fast. They can still be searched.{' '}
              <button type="button" className="link-button" onClick={tail.dismissGap}>
                Dismiss
              </button>
            </span>
          )}
        </div>
      )}

      <div className={`logs-content ${selected ? 'has-detail' : ''}`}>
        <div className="logs-results">
          {error ? (
            <div className="empty" role="alert">
              <p className="message error">{error}</p>
              {!isLive && (
                <button type="button" className="button" onClick={() => void results.refetch()}>
                  Try again
                </button>
              )}
            </div>
          ) : shown.length === 0 ? (
            <div className="empty">
              {isLoading ? (
                <p>Loading…</p>
              ) : isLive ? (
                <p>Waiting for Log Events…</p>
              ) : applications.data?.length === 0 ? (
                <>
                  <p>No Application sends logs yet.</p>
                  <Link to="/applications" className="button primary">
                    Create an Application
                  </Link>
                </>
              ) : (
                <p>{hasFilters(search) ? 'No Log Events match these filters.' : 'No Log Events in this time range.'}</p>
              )}
            </div>
          ) : (
            <LogList
              ref={list}
              events={shown}
              selectedId={selected?.id}
              applicationNames={applicationNames}
              onSelect={setSelected}
              onScrolledAwayChange={isLive ? setIsScrolledAway : undefined}
              hasNextPage={!isLive && results.hasNextPage}
              isFetchingNextPage={!isLive && results.isFetchingNextPage}
              nextPageFailed={!isLive && results.isFetchNextPageError}
              onFetchNextPage={onFetchNextPage}
            />
          )}
        </div>
        {selected && (
          <LogDetail
            key={selected.id}
            event={selected}
            applicationName={applicationNames.get(selected.applicationId)}
            onClose={closeDetail}
            canAddPropertyFilter={(search.prop?.length ?? 0) < MAX_PROPERTY_FILTERS}
            onFilterProperty={addPropertyFilter}
            onShowTrace={showTrace}
            onShowOccurrences={showOccurrences}
          />
        )}
      </div>
    </main>
  )
}

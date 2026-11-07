import { keepPreviousData, useInfiniteQuery, useQuery } from '@tanstack/react-query'
import { Link, getRouteApi } from '@tanstack/react-router'
import { useMemo, useState } from 'react'
import { applicationKeys, getApplications } from '../../api/applications.ts'
import { searchSpans } from '../../api/telemetry.ts'
import type { Span, SpanKind } from '../../api/types.ts'
import { formatDuration, formatLogTime } from '../../lib/format.ts'
import { useDebouncedValue } from '../../lib/useDebouncedValue.ts'
import { DEFAULT_TIME_RANGE, type TimeRange } from '../log-explorer/filters.ts'
import type { RequestSearch } from './search.ts'
import { SIMPLE_TIME_RANGES, toFrom } from './timeRange.ts'

const route = getRouteApi('/requests')

function SpanRow({ span, applicationName }: { span: Span; applicationName: string | undefined }) {
  return (
    <tr className={span.success ? undefined : 'span-failed'}>
      <td className="span-time">{formatLogTime(span.timestamp)}</td>
      <td className="span-name">
        <Link to="/traces/$traceId" params={{ traceId: span.traceId }} title={span.data ?? span.name}>
          {span.name}
        </Link>
      </td>
      <td className="span-status">{span.statusCode ?? (span.success ? 'OK' : 'Failed')}</td>
      <td className="span-duration">{formatDuration(span.durationMs)}</td>
      <td className="span-application">{applicationName ?? 'Deleted'}</td>
    </tr>
  )
}

export function RequestsPage() {
  const search = route.useSearch()
  const navigate = route.useNavigate()
  const [now, setNow] = useState(() => Date.now())

  const kind: SpanKind = search.kind ?? 'Request'

  const [name, setName] = useDebouncedValue(search.name ?? '', (value) => change({ name: value.trim() || undefined }, true))

  function change(patch: Partial<RequestSearch>, replace = false) {
    setNow(Date.now())
    void navigate({ search: (previous) => ({ ...previous, ...patch }), replace })
  }

  const applications = useQuery({ queryKey: applicationKeys.all, queryFn: ({ signal }) => getApplications(signal) })
  const applicationNames = useMemo(() => new Map(applications.data?.map(({ id, name }) => [id, name])), [applications.data])

  const query = useMemo(
    () => ({
      kind,
      from: toFrom(search.range, now),
      applicationId: search.app,
      name: search.name,
      success: search.failed ? false : undefined,
    }),
    [kind, search.range, search.app, search.name, search.failed, now],
  )

  const results = useInfiniteQuery({
    // With the time as well: for All time, the query itself stays the same, and Refresh would do nothing.
    queryKey: ['spans', query, now],
    queryFn: ({ pageParam, signal }) => searchSpans(query, pageParam, signal),
    initialPageParam: null as string | null,
    getNextPageParam: (page) => page.nextCursor,
    placeholderData: keepPreviousData,
  })

  const spans = results.data?.pages.flatMap((page) => page.items) ?? []
  const noun = kind === 'Request' ? 'requests' : 'dependencies'

  return (
    <main className="page wide">
      <header className="page-header">
        <h1>{kind === 'Request' ? 'Requests' : 'Dependencies'}</h1>
      </header>

      <div className="filter-row page-filters">
        <div className="segmented" role="group" aria-label="Kind">
          <button type="button" className="button" aria-pressed={kind === 'Request'} onClick={() => change({ kind: undefined })}>
            Requests
          </button>
          <button type="button" className="button" aria-pressed={kind === 'Dependency'} onClick={() => change({ kind: 'Dependency' })}>
            Dependencies
          </button>
        </div>
        <input
          className="input"
          type="search"
          aria-label="Name contains"
          placeholder="Name contains…"
          maxLength={500}
          value={name}
          onChange={(event) => setName(event.target.value)}
        />
        <select className="input" aria-label="Time range" value={search.range ?? DEFAULT_TIME_RANGE} onChange={(event) => change({ range: event.target.value as TimeRange })}>
          {SIMPLE_TIME_RANGES.map(({ value, label }) => (
            <option key={value} value={value}>
              {label}
            </option>
          ))}
        </select>
        <select className="input" aria-label="Application" value={search.app ?? ''} onChange={(event) => change({ app: event.target.value || undefined })}>
          <option value="">All Applications</option>
          {applications.data?.map(({ id, name }) => (
            <option key={id} value={id}>
              {name}
            </option>
          ))}
        </select>
        <label className="inline-field">
          <input type="checkbox" checked={search.failed ?? false} onChange={(event) => change({ failed: event.target.checked || undefined })} />
          Failed only
        </label>
        <button type="button" className="button" onClick={() => setNow(Date.now())} disabled={results.isFetching}>
          Refresh
        </button>
      </div>

      {results.isError ? (
        <div className="empty" role="alert">
          <p className="message error">{results.error.message}</p>
          <button type="button" className="button" onClick={() => void results.refetch()}>
            Try again
          </button>
        </div>
      ) : spans.length === 0 ? (
        <div className="empty">
          {results.isPending ? (
            <p>Loading…</p>
          ) : (
            <p>
              No {noun} found. <Link to="/applications">Applications</Link> send them with the package Logbook.AspNetCore.
            </p>
          )}
        </div>
      ) : (
        <>
          <table className="table spans">
            <thead>
              <tr>
                <th scope="col">Started</th>
                <th scope="col">Name</th>
                <th scope="col">Status</th>
                <th scope="col" className="span-duration">
                  Duration
                </th>
                <th scope="col">Application</th>
              </tr>
            </thead>
            <tbody>
              {spans.map((span) => (
                <SpanRow key={span.id} span={span} applicationName={applicationNames.get(span.applicationId)} />
              ))}
            </tbody>
          </table>
          {results.hasNextPage && (
            <p>
              <button type="button" className="button" disabled={results.isFetchingNextPage} onClick={() => void results.fetchNextPage()}>
                {results.isFetchingNextPage ? 'Loading…' : 'Load more'}
              </button>
            </p>
          )}
        </>
      )}
    </main>
  )
}

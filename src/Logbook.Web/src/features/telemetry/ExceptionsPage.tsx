import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { Link, getRouteApi } from '@tanstack/react-router'
import { useMemo, useState } from 'react'
import { applicationKeys, getApplications } from '../../api/applications.ts'
import { getExceptionGroups } from '../../api/telemetry.ts'
import type { ExceptionGroup } from '../../api/types.ts'
import { formatDate, formatLogTime } from '../../lib/format.ts'
import { DEFAULT_TIME_RANGE, type TimeRange } from '../log-explorer/filters.ts'
import { ExceptionView } from '../log-explorer/LogDetail.tsx'
import type { ExceptionSearch } from './search.ts'
import { SIMPLE_TIME_RANGES, toFrom } from './timeRange.ts'

const route = getRouteApi('/exceptions')

function asText(value: unknown): string | null {
  return typeof value === 'string' && value !== '' ? value : null
}

function ExceptionGroupRow({ group, applicationName }: { group: ExceptionGroup; applicationName: string | undefined }) {
  const type = asText(group.exception.type) ?? 'Unknown type'
  const message = asText(group.exception.message)

  return (
    <details className="exception-group">
      <summary>
        <span className="exception-group-main">
          <span className="exception-type">{type}</span>
          {message && <span className="exception-group-message">{message}</span>}
        </span>
        <span className="exception-group-count" title="Occurrences in the time range">
          {group.count.toLocaleString()}
        </span>
        <span className="exception-group-times">
          <span title={formatDate(group.lastOccurrence)}>Last {formatLogTime(group.lastOccurrence)}</span>
          <span title={formatDate(group.firstOccurrence)}>First {formatLogTime(group.firstOccurrence)}</span>
        </span>
        <span className="exception-group-application">{applicationName ?? 'Deleted'}</span>
      </summary>
      <div className="exception-group-body">
        <p>
          <Link className="button" to="/" search={{ range: 'all', app: group.applicationId, exception: group.fingerprint }}>
            Show all occurrences
          </Link>
        </p>
        <p className="section-label">The latest occurrence</p>
        <ExceptionView exception={group.exception} />
      </div>
    </details>
  )
}

export function ExceptionsPage() {
  const search = route.useSearch()
  const navigate = route.useNavigate()
  const [now, setNow] = useState(() => Date.now())

  function change(patch: Partial<ExceptionSearch>) {
    setNow(Date.now())
    void navigate({ search: (previous) => ({ ...previous, ...patch }) })
  }

  const applications = useQuery({ queryKey: applicationKeys.all, queryFn: ({ signal }) => getApplications(signal) })
  const applicationNames = useMemo(() => new Map(applications.data?.map(({ id, name }) => [id, name])), [applications.data])

  const query = useMemo(
    () => ({ from: toFrom(search.range, now), applicationId: search.app, sort: search.sort ?? ('LastOccurrence' as const) }),
    [search.range, search.app, search.sort, now],
  )

  const results = useQuery({
    // With the time as well: for All time, the query itself stays the same, and Refresh would do nothing.
    queryKey: ['exception-groups', query, now],
    queryFn: ({ signal }) => getExceptionGroups(query, signal),
    placeholderData: keepPreviousData,
    // It would copy exact numbers into plain objects when it merges a refetch.
    structuralSharing: false,
  })

  const groups = results.data?.items ?? []

  return (
    <main className="page wide">
      <header className="page-header">
        <h1>Exceptions</h1>
      </header>
      <p className="hint">Exceptions in the Log Events, grouped by type and stack. The count is for the time range; the first occurrence is the earliest one still stored.</p>

      <div className="filter-row page-filters">
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
        <select
          className="input"
          aria-label="Order"
          value={search.sort ?? 'LastOccurrence'}
          onChange={(event) => change({ sort: event.target.value === 'Count' ? 'Count' : undefined })}
        >
          <option value="LastOccurrence">Latest first</option>
          <option value="Count">Most frequent first</option>
        </select>
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
      ) : groups.length === 0 ? (
        <div className="empty">{results.isPending ? <p>Loading…</p> : <p>No exceptions in this time range.</p>}</div>
      ) : (
        <div className="exception-groups">
          {groups.map((group) => (
            <ExceptionGroupRow key={`${group.applicationId}/${group.fingerprint}`} group={group} applicationName={applicationNames.get(group.applicationId)} />
          ))}
        </div>
      )}
    </main>
  )
}

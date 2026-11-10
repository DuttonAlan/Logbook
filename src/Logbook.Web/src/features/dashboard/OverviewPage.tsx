import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { Link, getRouteApi } from '@tanstack/react-router'
import { useMemo, useState } from 'react'
import { applicationKeys, getApplications } from '../../api/applications.ts'
import { getOverview } from '../../api/dashboard.ts'
import { getExceptionGroups } from '../../api/telemetry.ts'
import { TimeChart } from '../../components/TimeChart.tsx'
import { formatDuration, formatLogTime } from '../../lib/format.ts'
import { DEFAULT_TIME_RANGE, type TimeRange } from '../log-explorer/filters.ts'
import { CHART_TIME_RANGES, chartFrom, type OverviewSearch } from './search.ts'

const route = getRouteApi('/overview')

const LOG_SERIES = [
  { label: 'Other levels', color: 'var(--chart-neutral)' },
  { label: 'Warnings', color: 'var(--level-warning)' },
  { label: 'Errors', color: 'var(--level-error)' },
]

const REQUEST_SERIES = [
  { label: 'Succeeded', color: 'var(--chart-neutral)' },
  { label: 'Failed', color: 'var(--bad)' },
]

function asText(value: unknown): string | null {
  return typeof value === 'string' && value !== '' ? value : null
}

function Stat({ label, value, tone }: { label: string; value: string; tone?: 'bad' }) {
  return (
    <div className="stat">
      <span className="stat-label">{label}</span>
      <span className={`stat-value ${tone ?? ''}`}>{value}</span>
    </div>
  )
}

export function OverviewPage() {
  const search = route.useSearch()
  const navigate = route.useNavigate()
  const [now, setNow] = useState(() => Date.now())

  function change(patch: Partial<OverviewSearch>) {
    setNow(Date.now())
    void navigate({ search: (previous) => ({ ...previous, ...patch }) })
  }

  const applications = useQuery({ queryKey: applicationKeys.all, queryFn: ({ signal }) => getApplications(signal) })
  const applicationNames = useMemo(() => new Map(applications.data?.map(({ id, name }) => [id, name])), [applications.data])

  const query = useMemo(() => ({ from: chartFrom(search.range, now), applicationId: search.app }), [search.range, search.app, now])

  const overview = useQuery({
    queryKey: ['overview', query],
    queryFn: ({ signal }) => getOverview(query, signal),
    placeholderData: keepPreviousData,
  })

  const exceptions = useQuery({
    queryKey: ['exception-groups', { ...query, sort: 'Count' }],
    queryFn: ({ signal }) => getExceptionGroups({ ...query, sort: 'Count' }, signal),
    placeholderData: keepPreviousData,
    structuralSharing: false,
  })

  const data = overview.data
  const logEventTotal = data?.logEvents.reduce((sum, bucket) => sum + bucket.total, 0) ?? 0
  const errorTotal = data?.logEvents.reduce((sum, bucket) => sum + bucket.errors, 0) ?? 0
  const errorRate = data && data.requestCount > 0 ? data.failedRequestCount / data.requestCount : null

  return (
    <main className="page wide">
      <header className="page-header">
        <h1>Overview</h1>
      </header>

      <div className="filter-row page-filters">
        <select className="input" aria-label="Time range" value={search.range ?? DEFAULT_TIME_RANGE} onChange={(event) => change({ range: event.target.value as TimeRange })}>
          {CHART_TIME_RANGES.map(({ value, label }) => (
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
        <button type="button" className="button" onClick={() => setNow(Date.now())} disabled={overview.isFetching}>
          Refresh
        </button>
      </div>

      {overview.isError ? (
        <div className="empty" role="alert">
          <p className="message error">{overview.error.message}</p>
          <button type="button" className="button" onClick={() => void overview.refetch()}>
            Try again
          </button>
        </div>
      ) : !data ? (
        <div className="empty">
          <p>Loading…</p>
        </div>
      ) : (
        <>
          <div className="stats">
            <Stat label="Log Events" value={logEventTotal.toLocaleString()} />
            <Stat label="Errors" value={errorTotal.toLocaleString()} tone={errorTotal > 0 ? 'bad' : undefined} />
            <Stat label="Requests" value={data.requestCount.toLocaleString()} />
            <Stat
              label="Failed requests"
              value={errorRate === null ? '–' : `${(errorRate * 100).toLocaleString(undefined, { maximumFractionDigits: 2 })} %`}
              tone={data.failedRequestCount > 0 ? 'bad' : undefined}
            />
          </div>

          <div className="dashboard-grid">
            <section className="panel">
              <TimeChart
                title="Log Events over time"
                series={LOG_SERIES}
                bucketSeconds={data.bucketSeconds}
                buckets={data.logEvents.map((bucket) => ({
                  timestamp: bucket.timestamp,
                  values: [bucket.total - bucket.warnings - bucket.errors, bucket.warnings, bucket.errors],
                }))}
              />
            </section>
            <section className="panel">
              <TimeChart
                title="Requests over time"
                series={REQUEST_SERIES}
                bucketSeconds={data.bucketSeconds}
                buckets={data.requests.map((bucket) => ({ timestamp: bucket.timestamp, values: [bucket.count - bucket.failed, bucket.failed] }))}
              />
            </section>
          </div>

          <div className="dashboard-grid">
            <section className="panel">
              <h2 className="panel-title">Slowest requests</h2>
              {data.slowestRequests.length === 0 ? (
                <p className="hint">No requests in this time range. Applications send them with the package Logbook.AspNetCore.</p>
              ) : (
                <table className="table compact">
                  <thead>
                    <tr>
                      <th scope="col">Name</th>
                      <th scope="col" className="number">
                        Count
                      </th>
                      <th scope="col" className="number">
                        Failed
                      </th>
                      <th scope="col" className="number" title="Average duration">
                        Avg
                      </th>
                      <th scope="col" className="number" title="95 percent of the requests took at most this long">
                        P95
                      </th>
                    </tr>
                  </thead>
                  <tbody>
                    {data.slowestRequests.map((request) => (
                      <tr key={`${request.applicationId}/${request.name}`}>
                        <td className="span-name">
                          <Link to="/requests" search={{ name: request.name, app: request.applicationId, range: search.range }} title={applicationNames.get(request.applicationId)}>
                            {request.name}
                          </Link>
                          {!search.app && <span className="hint"> · {applicationNames.get(request.applicationId) ?? 'Deleted'}</span>}
                        </td>
                        <td className="number">{request.count.toLocaleString()}</td>
                        <td className="number">{request.failed.toLocaleString()}</td>
                        <td className="number">{formatDuration(request.averageDurationMs)}</td>
                        <td className="number">{formatDuration(request.p95DurationMs)}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              )}
            </section>
            <section className="panel">
              <h2 className="panel-title">
                Most frequent exceptions{' '}
                <Link className="panel-link" to="/exceptions" search={{ sort: 'Count', range: search.range, app: search.app }}>
                  All exceptions
                </Link>
              </h2>
              {exceptions.isError ? (
                <p className="message error">{exceptions.error.message}</p>
              ) : (exceptions.data?.items.length ?? 0) === 0 ? (
                <p className="hint">{exceptions.isPending ? 'Loading…' : 'No exceptions in this time range.'}</p>
              ) : (
                <table className="table compact">
                  <thead>
                    <tr>
                      <th scope="col">Exception</th>
                      <th scope="col" className="number">
                        Count
                      </th>
                      <th scope="col">Last</th>
                    </tr>
                  </thead>
                  <tbody>
                    {exceptions.data?.items.slice(0, 10).map((group) => (
                      <tr key={`${group.applicationId}/${group.fingerprint}`}>
                        <td className="span-name">
                          <Link to="/" search={{ range: 'all', app: group.applicationId, exception: group.fingerprint }} title={asText(group.exception.message) ?? undefined}>
                            {asText(group.exception.type) ?? 'Unknown type'}
                          </Link>
                          {!search.app && <span className="hint"> · {applicationNames.get(group.applicationId) ?? 'Deleted'}</span>}
                        </td>
                        <td className="number">{group.count.toLocaleString()}</td>
                        <td className="span-time">{formatLogTime(group.lastOccurrence)}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              )}
            </section>
          </div>
        </>
      )}
    </main>
  )
}

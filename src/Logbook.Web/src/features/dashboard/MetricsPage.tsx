import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { getRouteApi } from '@tanstack/react-router'
import { useMemo, useState, type FormEvent } from 'react'
import { applicationKeys, getApplications } from '../../api/applications.ts'
import { getMetricNames, getMetricSeries } from '../../api/dashboard.ts'
import type { MetricBucket } from '../../api/types.ts'
import { TimeChart } from '../../components/TimeChart.tsx'
import { DEFAULT_TIME_RANGE, type TimeRange } from '../log-explorer/filters.ts'
import { CHART_TIME_RANGES, MAX_DIMENSION_FILTERS, METRIC_AGGREGATIONS, chartFrom, type MetricAggregation, type MetricSearch } from './search.ts'

const route = getRouteApi('/metrics')

// Null for a bucket without measurements, which is not the same as 0, except for the count and the sum.
function valueOf(bucket: MetricBucket, aggregation: MetricAggregation): number | null {
  switch (aggregation) {
    case 'Sum':
      return bucket.sum
    case 'Count':
      return bucket.count
    case 'Min':
      return bucket.min
    case 'Max':
      return bucket.max
    default:
      return bucket.count > 0 ? bucket.sum / bucket.count : null
  }
}

export function MetricsPage() {
  const search = route.useSearch()
  const navigate = route.useNavigate()
  const [now, setNow] = useState(() => Date.now())
  const [dimension, setDimension] = useState('')
  const [dimensionError, setDimensionError] = useState('')

  function change(patch: Partial<MetricSearch>) {
    setNow(Date.now())
    void navigate({ search: (previous) => ({ ...previous, ...patch }) })
  }

  const aggregation = search.agg ?? 'Average'
  const dimensions = useMemo(() => search.dim ?? [], [search.dim])

  const applications = useQuery({ queryKey: applicationKeys.all, queryFn: ({ signal }) => getApplications(signal) })

  const query = useMemo(() => ({ from: chartFrom(search.range, now), applicationId: search.app }), [search.range, search.app, now])

  const names = useQuery({
    queryKey: ['metric-names', query],
    queryFn: ({ signal }) => getMetricNames(query, signal),
    placeholderData: keepPreviousData,
  })

  const name = search.name ?? names.data?.[0]?.name

  const series = useQuery({
    queryKey: ['metric-series', name, query, dimensions],
    queryFn: ({ signal }) => getMetricSeries(name!, query, dimensions, signal),
    enabled: name !== undefined,
    placeholderData: keepPreviousData,
  })

  function addDimension(event: FormEvent) {
    event.preventDefault()

    const filter = dimension.trim()

    if (filter.indexOf('=') <= 0) {
      setDimensionError('Use the form Name=Value.')
    } else if (dimensions.length >= MAX_DIMENSION_FILTERS) {
      setDimensionError(`At most ${MAX_DIMENSION_FILTERS} dimension filters.`)
    } else {
      change({ dim: dimensions.includes(filter) ? dimensions : [...dimensions, filter] })
      setDimension('')
      setDimensionError('')
    }
  }

  // A metric from a link that has no points in this range stays selectable.
  const nameOptions = names.data?.map((item) => item.name) ?? []

  if (name && !nameOptions.includes(name)) {
    nameOptions.unshift(name)
  }

  return (
    <main className="page wide">
      <header className="page-header">
        <h1>Metrics</h1>
      </header>
      <p className="hint">Custom metrics the Applications send with LogbookMetrics or System.Diagnostics.Metrics, aggregated per interval.</p>

      <div className="filter-row page-filters">
        <select className="input" aria-label="Metric" value={name ?? ''} disabled={nameOptions.length === 0} onChange={(event) => change({ name: event.target.value || undefined })}>
          {nameOptions.length === 0 && <option value="">No metrics</option>}
          {nameOptions.map((option) => (
            <option key={option} value={option}>
              {option}
            </option>
          ))}
        </select>
        <select className="input" aria-label="Aggregation" value={aggregation} onChange={(event) => change({ agg: event.target.value === 'Average' ? undefined : (event.target.value as MetricAggregation) })}>
          {METRIC_AGGREGATIONS.map((option) => (
            <option key={option} value={option}>
              {option}
            </option>
          ))}
        </select>
        <select className="input" aria-label="Time range" value={search.range ?? DEFAULT_TIME_RANGE} onChange={(event) => change({ range: event.target.value as TimeRange })}>
          {CHART_TIME_RANGES.map(({ value, label }) => (
            <option key={value} value={value}>
              {label}
            </option>
          ))}
        </select>
        <select className="input" aria-label="Application" value={search.app ?? ''} onChange={(event) => change({ app: event.target.value || undefined })}>
          <option value="">All Applications</option>
          {applications.data?.map(({ id, name: applicationName }) => (
            <option key={id} value={id}>
              {applicationName}
            </option>
          ))}
        </select>
        <form className="property-form" onSubmit={addDimension}>
          <input
            className="input"
            aria-label="Add a dimension filter, as Name=Value"
            aria-invalid={dimensionError !== ''}
            placeholder="Dimension: Region=EU"
            value={dimension}
            onChange={(event) => {
              setDimension(event.target.value)
              setDimensionError('')
            }}
          />
          <button type="submit" className="button" disabled={dimension.trim() === ''}>
            Add
          </button>
        </form>
        <button type="button" className="button" onClick={() => setNow(Date.now())} disabled={series.isFetching || names.isFetching}>
          Refresh
        </button>
        {dimensionError && (
          <span className="message error" role="alert">
            {dimensionError}
          </span>
        )}
      </div>

      {dimensions.length > 0 && (
        <div className="filter-row chips page-filters">
          {dimensions.map((filter) => (
            <span key={filter} className="chip">
              <span className="chip-label" title={filter}>
                {filter}
              </span>
              <button
                type="button"
                aria-label={`Remove filter ${filter}`}
                onClick={() => {
                  const next = dimensions.filter((item) => item !== filter)

                  change({ dim: next.length > 0 ? next : undefined })
                }}
              >
                ×
              </button>
            </span>
          ))}
        </div>
      )}

      {names.isError || series.isError ? (
        <div className="empty" role="alert">
          <p className="message error">{(names.error ?? series.error)?.message}</p>
        </div>
      ) : name === undefined ? (
        <div className="empty">
          {names.isPending ? (
            <p>Loading…</p>
          ) : (
            <p>
              No metrics in this time range. Record them with <code>LogbookMetrics</code>, or name a meter in the setting <code>Logbook:Meters</code>.
            </p>
          )}
        </div>
      ) : !series.data ? (
        <div className="empty">
          <p>Loading…</p>
        </div>
      ) : (
        <section className="panel">
          <TimeChart
            title={`${series.data.name} · ${aggregation}`}
            series={[{ label: aggregation, color: 'var(--accent)' }]}
            bucketSeconds={series.data.bucketSeconds}
            buckets={series.data.buckets.map((bucket) => ({ timestamp: bucket.timestamp, values: [valueOf(bucket, aggregation)] }))}
            height={260}
          />
        </section>
      )}
    </main>
  )
}

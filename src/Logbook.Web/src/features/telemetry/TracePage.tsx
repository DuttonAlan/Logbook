import { useQuery } from '@tanstack/react-query'
import { Link, getRouteApi } from '@tanstack/react-router'
import { useMemo, useState } from 'react'
import { applicationKeys, getApplications } from '../../api/applications.ts'
import { getTrace } from '../../api/telemetry.ts'
import { CopyButton } from '../../components/CopyButton.tsx'
import { formatDate, formatDuration, formatLogTime } from '../../lib/format.ts'
import { ExceptionView } from '../log-explorer/LogDetail.tsx'
import { LevelBadge } from '../log-explorer/LevelBadge.tsx'
import { buildTimeline, type TimelineRow } from './timeline.ts'

const route = getRouteApi('/traces/$traceId')

// Keeps a very short span visible as a mark, instead of a bar without any width.
const MIN_BAR_WIDTH = 0.004

const INDENT_REM = 1

interface RowProps {
  row: TimelineRow
  applicationName: string | undefined
  isOpen: boolean
  onToggle: () => void
}

function SpanDetails({ row, applicationName }: { row: Extract<TimelineRow, { kind: 'span' }>; applicationName: string | undefined }) {
  const { span } = row

  return (
    <dl className="fields timeline-details">
      <dt>Application</dt>
      <dd>{applicationName ?? 'Deleted'}</dd>
      <dt>Environment</dt>
      <dd>{span.environment}</dd>
      <dt>Started</dt>
      <dd>
        <code>{span.timestamp}</code>
      </dd>
      {span.target && (
        <>
          <dt>Target</dt>
          <dd>{span.target}</dd>
        </>
      )}
      {span.data && (
        <>
          <dt>Details</dt>
          <dd>
            <code>{span.data}</code>
          </dd>
        </>
      )}
      <dt>Span Id</dt>
      <dd>
        <code>{span.spanId}</code>
      </dd>
    </dl>
  )
}

function Row({ row, applicationName, isOpen, onToggle }: RowProps) {
  const indent = { paddingLeft: `${row.depth * INDENT_REM}rem` }

  if (row.kind === 'span') {
    const { span } = row
    const type = span.kind === 'Request' ? 'Request' : (span.dependencyType ?? 'Dependency')

    return (
      <li className={`timeline-row ${span.success ? '' : 'span-failed'}`}>
        <button type="button" className="timeline-entry" aria-expanded={isOpen} onClick={onToggle}>
          <span className="timeline-label" style={indent}>
            <span className={`span-kind span-kind-${span.kind.toLowerCase()}`}>{type}</span>
            <span className="timeline-name" title={span.name}>
              {span.name}
            </span>
          </span>
          <span className="timeline-status">{span.statusCode ?? ''}</span>
          <span className="timeline-track">
            <span
              className={`timeline-bar ${span.kind === 'Request' ? 'request' : 'dependency'}`}
              style={{ left: `${row.offset * 100}%`, width: `${Math.max(row.width, MIN_BAR_WIDTH) * 100}%` }}
            />
          </span>
          <span className="timeline-duration">{formatDuration(span.durationMs)}</span>
        </button>
        {isOpen && <SpanDetails row={row} applicationName={applicationName} />}
      </li>
    )
  }

  const { event } = row

  return (
    <li className="timeline-row">
      <button type="button" className="timeline-entry" aria-expanded={isOpen} onClick={onToggle}>
        <span className="timeline-label" style={indent}>
          <LevelBadge level={event.level} />
          <span className="timeline-name timeline-message" title={event.message}>
            {event.message || event.messageTemplate || 'Empty message'}
          </span>
        </span>
        <span className="timeline-status" />
        <span className="timeline-track">
          <span className={`timeline-mark level-${event.level.toLowerCase()}`} style={{ left: `${row.offset * 100}%` }} />
        </span>
        <span className="timeline-duration">{formatLogTime(event.timestamp)}</span>
      </button>
      {isOpen && (
        <div className="timeline-details">
          <p className="log-detail-message">{event.message}</p>
          {event.exception && <ExceptionView exception={event.exception} />}
          {event.category && <p className="hint">{event.category}</p>}
        </div>
      )}
    </li>
  )
}

export function TracePage() {
  const { traceId } = route.useParams()
  const [open, setOpen] = useState<ReadonlySet<string>>(new Set())

  const applications = useQuery({ queryKey: applicationKeys.all, queryFn: ({ signal }) => getApplications(signal) })
  const applicationNames = useMemo(() => new Map(applications.data?.map(({ id, name }) => [id, name])), [applications.data])

  const trace = useQuery({
    queryKey: ['trace', traceId],
    queryFn: ({ signal }) => getTrace(traceId, signal),
    retry: false,
    // It would copy exact numbers into plain objects when it merges a refetch.
    structuralSharing: false,
  })

  const timeline = useMemo(() => (trace.data ? buildTimeline(trace.data.spans, trace.data.logEvents) : null), [trace.data])

  function toggle(id: string) {
    setOpen((current) => {
      const next = new Set(current)

      if (!next.delete(id)) {
        next.add(id)
      }

      return next
    })
  }

  const failed = trace.data?.spans.filter(({ success }) => !success).length ?? 0
  const started = trace.data && (trace.data.spans[0]?.timestamp ?? trace.data.logEvents[0]?.timestamp)

  return (
    <main className="page wide">
      <header className="page-header">
        <h1>Transaction</h1>
        <Link className="button" to="/" search={{ trace: traceId, range: 'all' }}>
          Log Events of this trace
        </Link>
      </header>

      <p className="hint trace-id">
        <code>{traceId}</code> <CopyButton text={traceId} label="Copy" />
      </p>

      {trace.isError ? (
        <div className="empty" role="alert">
          <p className="message error">{trace.error.message}</p>
          <button type="button" className="button" onClick={() => void trace.refetch()}>
            Try again
          </button>
        </div>
      ) : !trace.data || !timeline ? (
        <div className="empty">
          <p>Loading…</p>
        </div>
      ) : (
        <>
          <p className="trace-summary">
            {started && <span title={formatDate(started)}>{formatDate(started)}</span>}
            <span>{formatDuration(timeline.durationMs)} in total</span>
            <span>
              {trace.data.spans.length} {trace.data.spans.length === 1 ? 'span' : 'spans'}
            </span>
            <span>
              {trace.data.logEvents.length} Log {trace.data.logEvents.length === 1 ? 'Event' : 'Events'}
            </span>
            {failed > 0 && <span className="message error">{failed} failed</span>}
          </p>

          {trace.data.isTruncated && <p className="message warning">This trace is longer than can be shown. The latest part is left out.</p>}

          <ol className="timeline">
            {timeline.rows.map((row) => (
              <Row
                key={row.id}
                row={row}
                applicationName={applicationNames.get(row.kind === 'span' ? row.span.applicationId : row.event.applicationId)}
                isOpen={open.has(row.id)}
                onToggle={() => toggle(row.id)}
              />
            ))}
          </ol>
        </>
      )}
    </main>
  )
}

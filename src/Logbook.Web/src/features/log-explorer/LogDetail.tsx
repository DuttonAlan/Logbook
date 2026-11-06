import type { ReactNode } from 'react'
import type { JsonValue, LogEvent } from '../../api/types.ts'
import { CopyButton } from '../../components/CopyButton.tsx'
import { formatDate } from '../../lib/format.ts'
import { LevelBadge } from './LevelBadge.tsx'

interface LogDetailProps {
  event: LogEvent
  applicationName: string | undefined
  onClose: () => void
  onFilterProperty: (filter: string) => void
  onShowTrace: (traceId: string) => void
}

type JsonObject = { [key: string]: JsonValue }

function isObject(value: JsonValue | undefined): value is JsonObject {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

function toText(value: JsonValue | undefined): string | null {
  return typeof value === 'string' && value !== '' ? value : null
}

function ExceptionView({ exception }: { exception: JsonObject }) {
  const type = toText(exception.type)
  const message = toText(exception.message)
  const stackTrace = toText(exception.stackTrace)
  const inner = isObject(exception.inner) ? [exception.inner] : []
  const innerExceptions = Array.isArray(exception.innerExceptions) ? exception.innerExceptions.filter(isObject) : []
  const nested = [...inner, ...innerExceptions]

  // The log API accepts any JSON object here, not only what the SDK sends. Nothing of it may stay hidden.
  const known = new Set(['type', 'message', 'stackTrace', 'inner', 'innerExceptions'])
  const others = Object.entries(exception).filter(([name]) => !known.has(name))

  return (
    <div className="exception">
      {type && <p className="exception-type">{type}</p>}
      {message && <p className="exception-message">{message}</p>}
      {stackTrace && (
        <div className="code-block">
          <div className="code-block-actions">
            <CopyButton text={stackTrace} label="Copy stack trace" />
          </div>
          <pre>{stackTrace}</pre>
        </div>
      )}
      {others.length > 0 && <pre className="code-block">{JSON.stringify(Object.fromEntries(others), null, 2)}</pre>}
      {nested.map((innerException, index) => (
        <div className="exception-inner" key={index}>
          <p className="section-label">{innerExceptions.length > 0 ? `Inner exception ${index + 1}` : 'Inner exception'}</p>
          <ExceptionView exception={innerException} />
        </div>
      ))}
    </div>
  )
}

function PropertyRow({ name, value, onFilter }: { name: string; value: JsonValue; onFilter: (filter: string) => void }) {
  const isNested = typeof value === 'object' && value !== null
  const text = isNested ? JSON.stringify(value, null, 2) : String(value)

  // A name ends at the first equals sign in a filter, so one that contains it cannot be filtered by.
  const canFilter = !isNested && value !== null && !name.includes('=')

  return (
    <tr>
      <th scope="row">{name}</th>
      <td>{isNested ? <pre>{text}</pre> : <span className="property-value">{text}</span>}</td>
      <td className="property-action">
        {canFilter && (
          <button
            type="button"
            className="button small quiet"
            title={`Show only Log Events where ${name} is ${text}`}
            aria-label={`Filter by ${name} = ${text}`}
            onClick={() => onFilter(`${name}=${text}`)}
          >
            Filter
          </button>
        )}
      </td>
    </tr>
  )
}

function Field({ label, children }: { label: string; children: ReactNode }) {
  return (
    <>
      <dt>{label}</dt>
      <dd>{children}</dd>
    </>
  )
}

export function LogDetail({ event, applicationName, onClose, onFilterProperty, onShowTrace }: LogDetailProps) {
  const properties = Object.entries(event.properties ?? {})

  return (
    <aside className="log-detail" aria-label="Log Event details">
      <header className="log-detail-header">
        <LevelBadge level={event.level} />
        <span className="log-detail-time">{formatDate(event.timestamp)}</span>
        <button type="button" className="button small quiet" onClick={onClose} aria-label="Close details">
          Close
        </button>
      </header>
      <div className="log-detail-body">
        <p className="log-detail-message">{event.message || <em>Empty message</em>}</p>
        {event.messageTemplate && event.messageTemplate !== event.message && <p className="log-detail-template">{event.messageTemplate}</p>}

        <dl className="fields">
          <Field label="Application">{applicationName ?? 'Deleted'}</Field>
          <Field label="Environment">{event.environment}</Field>
          {event.category && <Field label="Category">{event.category}</Field>}
          {event.eventId !== null && <Field label="Event Id">{event.eventId}</Field>}
          <Field label="Timestamp">
            <code>{event.timestamp}</code>
          </Field>
          {event.traceId && (
            <Field label="Trace Id">
              <code>{event.traceId}</code>
            </Field>
          )}
          {event.spanId && (
            <Field label="Span Id">
              <code>{event.spanId}</code>
            </Field>
          )}
        </dl>

        {event.traceId && (
          <p>
            <button type="button" className="button" onClick={() => onShowTrace(event.traceId!)}>
              All Log Events of this trace
            </button>
          </p>
        )}

        {event.exception && (
          <section>
            <h2>Exception</h2>
            <ExceptionView exception={event.exception} />
          </section>
        )}

        {properties.length > 0 && (
          <section>
            <h2>Properties</h2>
            <table className="properties">
              <tbody>
                {properties.map(([name, value]) => (
                  <PropertyRow key={name} name={name} value={value} onFilter={onFilterProperty} />
                ))}
              </tbody>
            </table>
          </section>
        )}

        <p className="log-detail-actions">
          <CopyButton text={JSON.stringify(event, null, 2)} label="Copy as JSON" />
        </p>
      </div>
    </aside>
  )
}

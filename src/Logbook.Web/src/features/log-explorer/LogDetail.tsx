import { Link } from '@tanstack/react-router'
import type { ReactNode } from 'react'
import type { JsonValue, LogEvent } from '../../api/types.ts'
import { CopyButton } from '../../components/CopyButton.tsx'
import { formatDate } from '../../lib/format.ts'
import { isRawJsonNumber } from '../../lib/json.ts'
import { MAX_PROPERTY_FILTERS } from './filters.ts'
import { LevelBadge } from './LevelBadge.tsx'

interface LogDetailProps {
  event: LogEvent
  applicationName: string | undefined
  canAddPropertyFilter: boolean
  onClose: () => void
  onFilterProperty: (filter: string) => void
  onShowTrace: (traceId: string) => void
  onShowOccurrences: (fingerprint: string) => void
}

type JsonObject = { [key: string]: JsonValue }

function isObject(value: JsonValue | undefined): value is JsonObject {
  return typeof value === 'object' && value !== null && !Array.isArray(value) && !isRawJsonNumber(value)
}

function toText(value: JsonValue | undefined): string | null {
  return typeof value === 'string' && value !== '' ? value : null
}

export function ExceptionView({ exception }: { exception: JsonObject }) {
  const type = toText(exception.type)
  const message = toText(exception.message)
  const stackTrace = toText(exception.stackTrace)
  const inner = isObject(exception.inner) ? [exception.inner] : []
  const innerItems = Array.isArray(exception.innerExceptions) ? exception.innerExceptions : []
  const innerExceptions = innerItems.filter(isObject)
  const nested = [...inner, ...innerExceptions]

  // The log API accepts any JSON object here, not only what the SDK sends. What is not shown above must be shown below,
  // including a known field with an unexpected value.
  const others: { [key: string]: JsonValue } = { ...exception }

  if (type) delete others.type
  if (message) delete others.message
  if (stackTrace) delete others.stackTrace
  if (inner.length > 0) delete others.inner

  if (innerItems.length === innerExceptions.length && Array.isArray(exception.innerExceptions)) {
    delete others.innerExceptions
  } else if (innerExceptions.length > 0) {
    others.innerExceptions = innerItems.filter((item) => !isObject(item))
  }

  // The SDK sends null for what an exception does not have.
  const shownOthers = Object.entries(others).filter(([, value]) => value !== null)

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
      {shownOthers.length > 0 && <pre className="code-block">{JSON.stringify(Object.fromEntries(shownOthers), null, 2)}</pre>}
      {nested.map((innerException, index) => (
        <div className="exception-inner" key={index}>
          <p className="section-label">{innerExceptions.length > 0 ? `Inner exception ${index + 1}` : 'Inner exception'}</p>
          <ExceptionView exception={innerException} />
        </div>
      ))}
    </div>
  )
}

interface PropertyRowProps {
  name: string
  value: JsonValue
  canAddFilter: boolean
  onFilter: (filter: string) => void
}

function PropertyRow({ name, value, canAddFilter, onFilter }: PropertyRowProps) {
  const isNested = isObject(value) || Array.isArray(value)

  // Through JSON, which is the only way to get the digits of a number that is kept exactly.
  const text = typeof value === 'string' ? value : JSON.stringify(value, null, 2)

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
            disabled={!canAddFilter}
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

export function LogDetail({ event, applicationName, canAddPropertyFilter, onClose, onFilterProperty, onShowTrace, onShowOccurrences }: LogDetailProps) {
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
          <p className="log-detail-links">
            <Link className="button" to="/traces/$traceId" params={{ traceId: event.traceId }}>
              Open the transaction
            </Link>
            <button type="button" className="button" onClick={() => onShowTrace(event.traceId!)}>
              All Log Events of this trace
            </button>
          </p>
        )}

        {event.exception && (
          <section>
            <h2>Exception</h2>
            <ExceptionView exception={event.exception} />
            {event.exceptionFingerprint && (
              <p>
                <button type="button" className="button" onClick={() => onShowOccurrences(event.exceptionFingerprint!)}>
                  All occurrences of this exception
                </button>
              </p>
            )}
          </section>
        )}

        {properties.length > 0 && (
          <section>
            <h2>Properties</h2>
            {!canAddPropertyFilter && <p className="hint">At most {MAX_PROPERTY_FILTERS} property filters. Remove one to add another.</p>}
            <table className="properties">
              <tbody>
                {properties.map(([name, value]) => (
                  <PropertyRow key={name} name={name} value={value} canAddFilter={canAddPropertyFilter} onFilter={onFilterProperty} />
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

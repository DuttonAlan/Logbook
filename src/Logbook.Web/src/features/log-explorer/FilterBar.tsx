import { useState, type FormEvent, type ReactNode } from 'react'
import { LOG_LEVELS, type ApplicationSummary, type LogLevel } from '../../api/types.ts'
import { fromLocalInput, toLocalInput } from '../../lib/format.ts'
import { useDebouncedValue } from '../../lib/useDebouncedValue.ts'
import { DEFAULT_TIME_RANGE, MAX_PROPERTY_FILTERS, TIME_RANGES, hasFilters, type LogSearch, type TimeRange } from './filters.ts'

interface FilterBarProps {
  search: LogSearch
  applications: ApplicationSummary[]
  environments: string[]
  onChange: (patch: Partial<LogSearch>, options?: { replace?: boolean }) => void
  children?: ReactNode
}

function Chip({ label, onRemove }: { label: string; onRemove: () => void }) {
  return (
    <span className="chip">
      <span className="chip-label" title={label}>
        {label}
      </span>
      <button type="button" aria-label={`Remove filter ${label}`} onClick={onRemove}>
        ×
      </button>
    </span>
  )
}

export function FilterBar({ search, applications, environments, onChange, children }: FilterBarProps) {
  const [text, setText] = useDebouncedValue(search.q ?? '', (q) => onChange({ q: q || undefined }, { replace: true }))
  const [environment, setEnvironment] = useDebouncedValue(search.env ?? '', (env) => onChange({ env: env.trim() || undefined }, { replace: true }))
  const [property, setProperty] = useState('')
  const [propertyError, setPropertyError] = useState('')

  const range = search.range ?? DEFAULT_TIME_RANGE
  const properties = search.prop ?? []

  function toggleLevel(level: LogLevel) {
    const levels = search.level ?? []
    const next = levels.includes(level) ? levels.filter((item) => item !== level) : [...levels, level]

    onChange({ level: next.length > 0 ? next : undefined })
  }

  function addProperty(event: FormEvent) {
    event.preventDefault()

    const filter = property.trim()

    if (filter.indexOf('=') <= 0) {
      setPropertyError('Use the form Name=Value.')
    } else if (properties.length >= MAX_PROPERTY_FILTERS) {
      setPropertyError(`At most ${MAX_PROPERTY_FILTERS} property filters.`)
    } else {
      onChange({ prop: properties.includes(filter) ? properties : [...properties, filter] })
      setProperty('')
      setPropertyError('')
    }
  }

  return (
    <section className="filters" aria-label="Filters">
      <div className="filter-row">
        <input
          className="input filter-text"
          type="search"
          aria-label="Search the messages"
          placeholder='Search messages: order failed, "exact phrase", a or b, -excluded'
          value={text}
          onChange={(event) => setText(event.target.value)}
        />
        <select
          className="input"
          aria-label="Time range"
          disabled={search.live}
          title={search.live ? 'The live tail shows what arrives from now on' : undefined}
          value={range}
          onChange={(event) => onChange({ range: event.target.value as TimeRange })}
        >
          {TIME_RANGES.map(({ value, label }) => (
            <option key={value} value={value}>
              {label}
            </option>
          ))}
        </select>
        {children}
      </div>

      {range === 'custom' && !search.live && (
        <div className="filter-row">
          <label className="inline-field">
            From
            <input
              className="input"
              type="datetime-local"
              value={toLocalInput(search.from)}
              onChange={(event) => onChange({ from: fromLocalInput(event.target.value) }, { replace: true })}
            />
          </label>
          <label className="inline-field">
            To
            <input
              className="input"
              type="datetime-local"
              value={toLocalInput(search.to)}
              onChange={(event) => onChange({ to: fromLocalInput(event.target.value) }, { replace: true })}
            />
          </label>
        </div>
      )}

      <div className="filter-row">
        <div className="level-toggles" role="group" aria-label="Levels">
          {LOG_LEVELS.map((level) => (
            <button
              key={level}
              type="button"
              className={`level-toggle level-${level.toLowerCase()}`}
              aria-pressed={search.level?.includes(level) ?? false}
              onClick={() => toggleLevel(level)}
            >
              {level}
            </button>
          ))}
        </div>
        <select
          className="input"
          aria-label="Application"
          value={search.app ?? ''}
          onChange={(event) => onChange({ app: event.target.value || undefined })}
        >
          <option value="">All Applications</option>
          {/* An Application from a link that no longer exists stays visible as the active filter. */}
          {search.app && !applications.some(({ id }) => id === search.app) && <option value={search.app}>Unknown Application</option>}
          {applications.map(({ id, name }) => (
            <option key={id} value={id}>
              {name}
            </option>
          ))}
        </select>
        <input
          className="input filter-environment"
          aria-label="Environment"
          placeholder="Environment"
          list="known-environments"
          maxLength={100}
          value={environment}
          onChange={(event) => setEnvironment(event.target.value)}
        />
        <datalist id="known-environments">
          {environments.map((name) => (
            <option key={name} value={name} />
          ))}
        </datalist>
        <form className="property-form" onSubmit={addProperty}>
          <input
            className="input"
            aria-label="Add a property filter, as Name=Value"
            aria-invalid={propertyError !== ''}
            placeholder="Property: OrderId=42"
            value={property}
            onChange={(event) => {
              setProperty(event.target.value)
              setPropertyError('')
            }}
          />
          <button type="submit" className="button" disabled={property.trim() === ''}>
            Add
          </button>
        </form>
        {propertyError && (
          <span className="message error" role="alert">
            {propertyError}
          </span>
        )}
      </div>

      {hasFilters(search) && (
        <div className="filter-row chips">
          {search.trace && <Chip label={`Trace ${search.trace}`} onRemove={() => onChange({ trace: undefined })} />}
          {search.exception && <Chip label={`Exception group ${search.exception}`} onRemove={() => onChange({ exception: undefined })} />}
          {properties.map((filter) => (
            <Chip
              key={filter}
              label={filter}
              onRemove={() => {
                const next = properties.filter((item) => item !== filter)

                onChange({ prop: next.length > 0 ? next : undefined })
              }}
            />
          ))}
          <button
            type="button"
            className="button small quiet"
            onClick={() => onChange({ level: undefined, app: undefined, env: undefined, q: undefined, prop: undefined, trace: undefined, exception: undefined })}
          >
            Clear all filters
          </button>
        </div>
      )}
    </section>
  )
}

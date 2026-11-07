const timeOfDay = new Intl.DateTimeFormat(undefined, {
  hour: '2-digit',
  minute: '2-digit',
  second: '2-digit',
  fractionalSecondDigits: 3,
  hour12: false,
})

const dayOfYear = new Intl.DateTimeFormat(undefined, { month: 'short', day: '2-digit' })

const fullDate = new Intl.DateTimeFormat(undefined, { dateStyle: 'medium', timeStyle: 'medium' })

// The day only when it is not today, so that the column stays easy to scan.
export function formatLogTime(timestamp: string): string {
  const date = new Date(timestamp)
  const time = timeOfDay.format(date)

  return date.toDateString() === new Date().toDateString() ? time : `${dayOfYear.format(date)} ${time}`
}

export function formatDate(timestamp: string): string {
  return fullDate.format(new Date(timestamp))
}

// The value for an <input type="datetime-local">, which has no time zone: it shows local time.
export function toLocalInput(timestamp: string | undefined): string {
  if (!timestamp) {
    return ''
  }

  const date = new Date(timestamp)

  if (Number.isNaN(date.getTime())) {
    return ''
  }

  return new Date(date.getTime() - date.getTimezoneOffset() * 60_000).toISOString().slice(0, 16)
}

export function fromLocalInput(value: string): string | undefined {
  return value ? new Date(value).toISOString() : undefined
}

// Milliseconds for what is short, as most spans are, and seconds from there on.
export function formatDuration(milliseconds: number): string {
  if (milliseconds < 10) {
    return `${milliseconds.toFixed(1)} ms`
  }

  return milliseconds < 1000 ? `${Math.round(milliseconds)} ms` : `${(milliseconds / 1000).toFixed(2)} s`
}

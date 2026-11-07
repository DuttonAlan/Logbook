import type { RawJsonNumber } from '../lib/json.ts'

export const LOG_LEVELS = ['Trace', 'Debug', 'Information', 'Warning', 'Error', 'Critical'] as const

export type LogLevel = (typeof LOG_LEVELS)[number]

export type JsonValue = string | number | RawJsonNumber | boolean | null | JsonValue[] | { [key: string]: JsonValue }

export interface LogEvent {
  id: string
  applicationId: string
  timestamp: string
  level: LogLevel
  message: string
  messageTemplate: string | null
  category: string | null
  environment: string
  eventId: number | null
  exception: { [key: string]: JsonValue } | null
  traceId: string | null
  spanId: string | null
  properties: { [key: string]: JsonValue } | null
}

export interface LogEventPage {
  items: LogEvent[]
  nextCursor: string | null
}

export interface LogEventQuery {
  from?: string
  to?: string
  levels?: LogLevel[]
  applicationId?: string
  environment?: string
  text?: string
  traceId?: string
  properties?: string[]
}

export interface ApplicationSummary {
  id: string
  name: string
  createdAt: string
}

export interface ApiKey {
  id: string
  createdAt: string
  expiresAt: string | null
  revokedAt: string | null
  isActive: boolean
}

export interface Application extends ApplicationSummary {
  apiKeys: ApiKey[]
  modifiedAt: string | null
  retentionDays: number | null
  effectiveRetentionDays: number
}

export interface IssuedApiKey {
  id: string
  key: string
  createdAt: string
  expiresAt: string | null
}

export interface CreatedApplication {
  id: string
  apiKey: IssuedApiKey
}

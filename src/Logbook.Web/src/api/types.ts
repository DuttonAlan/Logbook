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
  exceptionFingerprint: string | null
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
  exceptionFingerprint?: string
}

export type SpanKind = 'Request' | 'Dependency'

export interface Span {
  id: string
  applicationId: string
  timestamp: string
  kind: SpanKind
  name: string
  durationMs: number
  success: boolean
  environment: string
  traceId: string
  spanId: string
  parentSpanId: string | null
  statusCode: number | null
  dependencyType: string | null
  target: string | null
  data: string | null
}

export interface SpanPage {
  items: Span[]
  nextCursor: string | null
}

export interface SpanQuery {
  from?: string
  to?: string
  kind?: SpanKind
  applicationId?: string
  environment?: string
  name?: string
  success?: boolean
  minDurationMs?: number
}

export interface Trace {
  traceId: string
  spans: Span[]
  logEvents: LogEvent[]
  isTruncated: boolean
}

export type ExceptionGroupSort = 'LastOccurrence' | 'Count'

export interface ExceptionGroup {
  applicationId: string
  fingerprint: string
  count: number
  firstOccurrence: string
  lastOccurrence: string
  exception: { [key: string]: JsonValue }
}

export interface ExceptionGroups {
  items: ExceptionGroup[]
}

export interface ExceptionGroupQuery {
  from?: string
  to?: string
  applicationId?: string
  environment?: string
  sort: ExceptionGroupSort
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

export interface LogEventBucket {
  timestamp: string
  total: number
  warnings: number
  errors: number
}

export interface RequestBucket {
  timestamp: string
  count: number
  failed: number
}

export interface SlowRequest {
  applicationId: string
  name: string
  count: number
  failed: number
  averageDurationMs: number
  p95DurationMs: number
  maxDurationMs: number
}

export interface Overview {
  from: string
  to: string
  bucketSeconds: number
  logEvents: LogEventBucket[]
  requests: RequestBucket[]
  requestCount: number
  failedRequestCount: number
  slowestRequests: SlowRequest[]
}

export interface ChartQuery {
  from?: string
  applicationId?: string
  environment?: string
}

export interface MetricName {
  name: string
  lastSeen: string
}

export interface MetricBucket {
  timestamp: string
  count: number
  sum: number
  min: number | null
  max: number | null
}

export interface MetricSeries {
  name: string
  from: string
  to: string
  bucketSeconds: number
  buckets: MetricBucket[]
}

export interface SavedSearch {
  id: string
  name: string
  query: string
  createdAt: string
  modifiedAt: string | null
}

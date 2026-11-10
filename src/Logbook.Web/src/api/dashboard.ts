import { getJson } from './client.ts'
import type { ChartQuery, MetricName, MetricSeries, Overview } from './types.ts'

function toParams(query: ChartQuery): URLSearchParams {
  const params = new URLSearchParams()

  if (query.from) params.set('from', query.from)
  if (query.applicationId) params.set('applicationId', query.applicationId)
  if (query.environment) params.set('environment', query.environment)

  return params
}

export function getOverview(query: ChartQuery, signal?: AbortSignal): Promise<Overview> {
  const params = toParams(query)

  params.set('slowest', '10')

  return getJson<Overview>(`/api/v1/overview?${params}`, signal)
}

export async function getMetricNames(query: ChartQuery, signal?: AbortSignal): Promise<MetricName[]> {
  const response = await getJson<{ items: MetricName[] }>(`/api/v1/metrics/names?${toParams(query)}`, signal)

  return response.items
}

export function getMetricSeries(name: string, query: ChartQuery, dimensions: string[], signal?: AbortSignal): Promise<MetricSeries> {
  const params = toParams(query)

  params.set('name', name)
  dimensions.forEach((dimension) => params.append('dimension', dimension))

  return getJson<MetricSeries>(`/api/v1/metrics?${params}`, signal)
}

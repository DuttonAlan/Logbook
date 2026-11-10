import { useEffect, useRef, useState } from 'react'

export interface ChartSeries {
  label: string
  // A CSS color, e.g. var(--chart-error). Stacked from the bottom in the order given.
  color: string
}

export interface ChartBucket {
  timestamp: string
  // One value per series. Null means no data, which is not the same as 0.
  values: (number | null)[]
}

interface TimeChartProps {
  title: string
  series: ChartSeries[]
  buckets: ChartBucket[]
  bucketSeconds: number
  formatValue?: (value: number) => string
  height?: number
}

const PLOT_TOP = 8
const AXIS_HEIGHT = 22
const AXIS_WIDTH = 52
const BAR_GAP = 2
const SEGMENT_GAP = 2
const RADIUS = 3

const defaultFormat = (value: number) => value.toLocaleString(undefined, { maximumFractionDigits: 2 })

// 1, 2 or 5 times a power of ten, so that the gridlines fall on round numbers.
function niceMax(value: number): number {
  if (value <= 0) {
    return 1
  }

  const power = 10 ** Math.floor(Math.log10(value))
  const step = [1, 2, 5, 10].find((factor) => factor * power >= value) ?? 10

  return step * power
}

function formatTick(timestamp: string, bucketSeconds: number): string {
  const date = new Date(timestamp)

  if (bucketSeconds >= 24 * 60 * 60) {
    return date.toLocaleDateString(undefined, { month: 'short', day: 'numeric' })
  }

  const time = date.toLocaleTimeString(undefined, { hour: '2-digit', minute: '2-digit', hour12: false })

  return bucketSeconds >= 3 * 60 * 60 || date.getHours() + date.getMinutes() === 0 ? `${date.toLocaleDateString(undefined, { month: 'short', day: 'numeric' })} ${time}` : time
}

function formatBucket(timestamp: string, bucketSeconds: number): string {
  const start = new Date(timestamp)
  const end = new Date(start.getTime() + bucketSeconds * 1000)
  const options: Intl.DateTimeFormatOptions = { month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit', hour12: false }

  return `${start.toLocaleString(undefined, options)} – ${end.toLocaleString(undefined, options)}`
}

// A bar with rounded top corners, anchored square to the baseline.
function topRoundedBar(x: number, y: number, width: number, height: number): string {
  const r = Math.min(RADIUS, width / 2, height)

  return `M${x},${y + height} V${y + r} Q${x},${y} ${x + r},${y} H${x + width - r} Q${x + width},${y} ${x + width},${y + r} V${y + height} Z`
}

// Stacks the positive values from the baseline up, with a gap of the surface between neighboring segments.
function stackSegments(values: (number | null)[], scale: (value: number) => number, baseline: number) {
  const segments: { y: number; segment: number; seriesIndex: number }[] = []

  values.forEach((value, seriesIndex) => {
    if (value === null || value <= 0) {
      return
    }

    const gap = segments.length > 0 ? SEGMENT_GAP : 0
    const bottom = segments.length > 0 ? segments[segments.length - 1].y : baseline
    const segment = Math.max(1, scale(value) - gap)

    segments.push({ y: bottom - gap - segment, segment, seriesIndex })
  })

  return segments
}

function useWidth() {
  const ref = useRef<HTMLDivElement>(null)
  const [width, setWidth] = useState(0)

  useEffect(() => {
    const element = ref.current

    if (!element) {
      return
    }

    const observer = new ResizeObserver(([entry]) => setWidth(Math.floor(entry.contentRect.width)))

    observer.observe(element)

    return () => observer.disconnect()
  }, [])

  return [ref, width] as const
}

export function TimeChart({ title, series, buckets, bucketSeconds, formatValue = defaultFormat, height = 180 }: TimeChartProps) {
  const [container, width] = useWidth()
  const [hovered, setHovered] = useState<number | null>(null)

  const totals = buckets.map((bucket) => bucket.values.reduce<number>((sum, value) => sum + (value ?? 0), 0))
  const max = niceMax(Math.max(0, ...totals))
  const plotWidth = Math.max(0, width - AXIS_WIDTH)
  const plotHeight = height - PLOT_TOP - AXIS_HEIGHT
  const slot = buckets.length > 0 ? plotWidth / buckets.length : 0
  const barWidth = Math.max(1, slot - BAR_GAP)
  const scale = (value: number) => (value / max) * plotHeight
  const tickEvery = Math.max(1, Math.ceil(buckets.length / Math.max(1, Math.floor(plotWidth / 90))))
  const gridValues = [0, max / 2, max]
  const hoveredBucket = hovered === null ? undefined : buckets[hovered]

  return (
    <figure className="chart">
      <figcaption className="chart-header">
        <span className="chart-title">{title}</span>
        {series.length > 1 && (
          <span className="chart-legend">
            {series.map(({ label, color }) => (
              <span key={label} className="chart-legend-item">
                <span className="chart-swatch" style={{ background: color }} aria-hidden="true" />
                {label}
              </span>
            ))}
          </span>
        )}
      </figcaption>
      <div ref={container} className="chart-plot" style={{ height }} onMouseLeave={() => setHovered(null)}>
        {width > 0 && (
          <svg width={width} height={height} role="img" aria-label={`${title}, ${buckets.length} intervals. The table below has the values.`}>
            {gridValues.map((value) => {
              const y = PLOT_TOP + plotHeight - scale(value)

              return (
                <g key={value}>
                  <line className="chart-grid" x1={AXIS_WIDTH} x2={width} y1={y} y2={y} />
                  <text className="chart-axis" x={AXIS_WIDTH - 6} y={y} dy="0.32em" textAnchor="end">
                    {formatValue(value)}
                  </text>
                </g>
              )
            })}
            {buckets.map((bucket, index) => {
              const x = AXIS_WIDTH + index * slot + BAR_GAP / 2
              const drawn = stackSegments(bucket.values, scale, PLOT_TOP + plotHeight)

              return (
                <g key={bucket.timestamp} opacity={hovered === null || hovered === index ? 1 : 0.55}>
                  {drawn.map(({ y, segment, seriesIndex }, position) => {
                    const isTop = position === drawn.length - 1

                    return isTop ? (
                      <path key={seriesIndex} d={topRoundedBar(x, y, barWidth, segment)} fill={series[seriesIndex].color} />
                    ) : (
                      <rect key={seriesIndex} x={x} y={y} width={barWidth} height={segment} fill={series[seriesIndex].color} />
                    )
                  })}
                  {/* Taller and wider than the bar, so that thin and empty buckets can be hovered too. */}
                  <rect
                    className="chart-hit"
                    x={AXIS_WIDTH + index * slot}
                    y={PLOT_TOP}
                    width={slot}
                    height={plotHeight}
                    onMouseEnter={() => setHovered(index)}
                  />
                  {index % tickEvery === 0 && (
                    <text className="chart-axis" x={x} y={height - 6}>
                      {formatTick(bucket.timestamp, bucketSeconds)}
                    </text>
                  )}
                </g>
              )
            })}
          </svg>
        )}
        {hoveredBucket && hovered !== null && (
          <div
            className="chart-tooltip"
            style={{
              left: Math.min(AXIS_WIDTH + hovered * slot + slot / 2, width - 120),
              top: PLOT_TOP,
            }}
          >
            <div className="chart-tooltip-time">{formatBucket(hoveredBucket.timestamp, bucketSeconds)}</div>
            {series.map(({ label, color }, seriesIndex) => (
              <div key={label} className="chart-tooltip-row">
                <span className="chart-swatch" style={{ background: color }} aria-hidden="true" />
                <span>{label}</span>
                <span className="chart-tooltip-value">{hoveredBucket.values[seriesIndex] === null ? '–' : formatValue(hoveredBucket.values[seriesIndex] ?? 0)}</span>
              </div>
            ))}
          </div>
        )}
      </div>
      <details className="chart-table">
        <summary>Show as a table</summary>
        <table className="table">
          <thead>
            <tr>
              <th scope="col">Interval</th>
              {series.map(({ label }) => (
                <th key={label} scope="col">
                  {label}
                </th>
              ))}
            </tr>
          </thead>
          <tbody>
            {buckets.map((bucket) => (
              <tr key={bucket.timestamp}>
                <td>{formatBucket(bucket.timestamp, bucketSeconds)}</td>
                {bucket.values.map((value, seriesIndex) => (
                  <td key={series[seriesIndex].label}>{value === null ? '–' : formatValue(value)}</td>
                ))}
              </tr>
            ))}
          </tbody>
        </table>
      </details>
    </figure>
  )
}
